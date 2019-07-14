﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ZoneRecoveryAlgorithm
{
    public class RecoveryTurn
    {
        private ZoneLevels _zoneLevels;
        private double _previousPrice;
        private double _currentPrice;
        private MarketPosition _entryPosition;
        public RecoveryTurn PreviousTurn;
        private double _commissionRate;
        private IActiveTurn _activeTurn;

        public MarketPosition Position { get; }
        public double EntryPrice { get; }
        public double LotSize { get; }
        public double Spread { get; }
        public double Commission { get; }
        public double MaxSlippageRate { get; }
        public bool IsActive { get; private set; }
        public int TurnIndex { get; }

        private double _unrealizedNetProfit;
        public double UnrealizedNetProfit { get { return _unrealizedNetProfit; } }

        private double _unrealizedGrossProfit;
        public double UnrealizedGrossProfit { get { return _unrealizedGrossProfit; } }

        public RecoveryTurn(IActiveTurn activeTurn, RecoveryTurn previousTurn, ZoneLevels zoneLevel, MarketPosition entryPosition, MarketPosition turnPosition, double entryBidPrice, double entryAskPrice, double lotSize, double spread, double commissionRate, double slippage)
        {
            _activeTurn = activeTurn;

            _activeTurn.Update(this);

            if (previousTurn==null)
            {
                TurnIndex = 0;            
            }
            else
            {
                TurnIndex = previousTurn.TurnIndex + 1;
            }

            _zoneLevels = zoneLevel;
            _entryPosition = entryPosition;
            PreviousTurn = previousTurn;

            IsActive = true;
            Position = turnPosition;
            if (Position == MarketPosition.Long)
            {
                EntryPrice = entryAskPrice;
            }
            if (Position == MarketPosition.Short)
            {
                EntryPrice = entryBidPrice;
            }

            _currentPrice = EntryPrice;
            _commissionRate = commissionRate;

            LotSize = lotSize;
            Commission = lotSize * commissionRate;
            Spread = spread;
            MaxSlippageRate = slippage;
        }

        public (PriceActionResult, RecoveryTurn) PriceAction(double bid, double ask)
        {
            PreviousTurn?.PriceAction(bid, ask);

            _previousPrice = _currentPrice;
            _currentPrice = (bid + ask) / 2d;
            
            double spread = bid - ask;

            bool isTakeProfitLevelHit = false;
            bool isRecoveryLevelHit = false;

            if (Position == MarketPosition.Long)
            {
                _unrealizedNetProfit = ((_currentPrice - EntryPrice) * LotSize) - Commission;
                _unrealizedGrossProfit = (_currentPrice - EntryPrice) * LotSize;
                isTakeProfitLevelHit = IsActive && _currentPrice >= _zoneLevels.UpperTradingZone;
                isRecoveryLevelHit = IsActive && _previousPrice > _zoneLevels.LowerRecoveryZone && _currentPrice <= _zoneLevels.LowerRecoveryZone;
            }
            else if (Position == MarketPosition.Short)
            {
                _unrealizedNetProfit = ((EntryPrice - _currentPrice) * LotSize) - Commission;
                _unrealizedGrossProfit = (EntryPrice - _currentPrice) * LotSize;
                isTakeProfitLevelHit = IsActive && _currentPrice <= _zoneLevels.LowerTradingZone;
                isRecoveryLevelHit = IsActive && _previousPrice < _zoneLevels.UpperRecoveryZone && _currentPrice >= _zoneLevels.UpperRecoveryZone;
            }

            if (isTakeProfitLevelHit)
            {
                return (PriceActionResult.TakeProfitLevelHit, null);
            }
            else if (isRecoveryLevelHit)
            {
                if (IsMaximumSlippageHit(_currentPrice))
                {
                    return (PriceActionResult.MaxSlippageLevelHit, null);
                }
                else
                {
                    var newPosition = Position.Reverse();
                    double previousTurnTargetNetReturns = GetTotalPreviousNetReturns(newPosition, PreviousTurn);
                    double lotSize = GetLossRecoveryLotSize(_zoneLevels, previousTurnTargetNetReturns, spread, _commissionRate);

                    IsActive = false;

                    return (PriceActionResult.RecoveryLevelHit, new RecoveryTurn(_activeTurn, this, _zoneLevels, _entryPosition, newPosition, bid, ask, lotSize, spread, _commissionRate, MaxSlippageRate));
                }
            }
            else
            {
                return (PriceActionResult.Nothing, null);
            }                                                    
        }

        private bool IsMaximumSlippageHit(double currentPrice)
        {
            if (Position == MarketPosition.Long)
            {
                return (_zoneLevels.LowerRecoveryZone - currentPrice) / (_zoneLevels.UpperRecoveryZone - _zoneLevels.LowerRecoveryZone) > MaxSlippageRate;
            }
            else if (Position == MarketPosition.Short)
            {
                return (currentPrice - _zoneLevels.UpperRecoveryZone) / (_zoneLevels.UpperRecoveryZone - _zoneLevels.LowerRecoveryZone) > MaxSlippageRate;
            }
            else
            {
                return false;
            }
        }

        private double GetLossRecoveryLotSize(ZoneLevels zoneLevels, double previousTurnTargetNetReturns, double spread, double commissionRate)
        {
            /*
            Total Gain Potential(TPG) - Total Loss Potential(TPL) = 0
            Commission(CO) = Recovery Size(RS) * Commission Rate(CR)
            Total Gain Potential(TPG) = Recovery Size(RS) * (Recovery Level(RL) - Stop Loss Level(SL)) -Commission(CO)
            Total Loss Potential(TPL) = Previous Recovery Size(PRS) * ABS(Previous Entry Level(PEL) - Previous Stop Loss Level(PSL)) + Previous Turn Commission(PTC) -Prior Turns Total Return PTTR
            Total Gain Potential(TPG) = Total Loss Potential(TPL)


            TPG - TPL = 0
            CO = RS * CR
            TPG = (RS * (RL - SL)) - CO
            TPL = PRS * ABS(PEL - PSL) + PTC - PTTR
            TPG = TPL
            (RS * (RL - SL)) - (RS * CR) = PRS * ABS(PEL - PSL) + PTC - PTTR
            RS((RL - SL) - CR) = PRS * ABS(PEL - PSL) + PTC - PTTR
            RS = (PRS * ABS(PEL - PSL) + PTC - PTTR) / (RL - SL - CR)
            */

            if (Position == MarketPosition.Long)
            {
                return (LotSize * (EntryPrice - zoneLevels.LowerTradingZone) + Commission  + spread - previousTurnTargetNetReturns) / (zoneLevels.LowerRecoveryZone - zoneLevels.LowerTradingZone - _commissionRate);
            }
            else if (Position == MarketPosition.Short)
            {
                return (LotSize * (zoneLevels.UpperTradingZone - EntryPrice) + Commission + spread - previousTurnTargetNetReturns) / (zoneLevels.UpperTradingZone - zoneLevels.UpperRecoveryZone - _commissionRate);
            }
            else
            {
                return 0;
            }            
        }

        private double GetTotalPreviousNetReturns(MarketPosition entryPosition, RecoveryTurn previousTurn)
        {
            var turn = previousTurn;
            double totalNetReturns = 0;
            while (turn != null)
            {
                totalNetReturns += turn.GetNetReturns(entryPosition);
                
                turn = turn.PreviousTurn;
            }

            return totalNetReturns;
        }

        public double GetNetReturns(MarketPosition activePosition)
        {
            if (activePosition == MarketPosition.Short)
            {
                if (Position == MarketPosition.Long)
                {
                    return - (LotSize * (_zoneLevels.UpperRecoveryZone - _zoneLevels.LowerTradingZone) + Commission + Spread);
                }
                else if (Position == MarketPosition.Short)
                {
                    return LotSize * (_zoneLevels.LowerRecoveryZone - _zoneLevels.LowerTradingZone) - Commission - Spread;
                }
                else
                {
                    return 0;
                }
            }
            else if (activePosition == MarketPosition.Long)
            {
                if (Position == MarketPosition.Long)
                {
                    return LotSize * (_zoneLevels.UpperTradingZone - _zoneLevels.UpperRecoveryZone) - Commission - Spread;
                }
                else if (Position == MarketPosition.Short)
                {
                    return - (LotSize * (_zoneLevels.UpperTradingZone - _zoneLevels.LowerRecoveryZone) + Commission + Spread);
                }
                else
                {
                    return 0;
                }
            }
            else
            {
                return 0;
            }
        }

    }
}