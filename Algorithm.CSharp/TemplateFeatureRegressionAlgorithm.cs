/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.Algorithm.CSharp
{
    public class TemplateFeatureRegressionAlgorithm: QCAlgorithm
    {
        private int _testCaseIndex;
        private bool _submittedMarketOnCloseToday;
        private DateTime _last = DateTime.MinValue;
        private Symbol _canonicalOption;
        private Symbol _canonicalIndexOption;
        private List<OrderType> _orderTypes;
        private Dictionary<OrderType, Action<Slice>> _orderTypeMethods;

        protected virtual Symbol EquitySymbol { get; set; }
        protected virtual Symbol OptionContract { get; set; }
        protected virtual Symbol ForexSymbol { get; set; }
        protected virtual Symbol FutureContract { get; set; }
        protected virtual Symbol CfdSymbol { get; set; }
        protected virtual Symbol CryptoSymbol { get; set; }
        protected virtual Symbol FutureOptionContract { get; set; }
        protected virtual Symbol IndexOptionContract { get; set; }
        protected virtual Symbol CryptoFutureSymbol { get; set; }
        protected virtual Dictionary<OrderType, List<Symbol>> SymbolToTestPerOrderType { get; set; }

        protected int OpenOrdersTimeout { get; set; }
        protected BrokerageName Brokerage { get; set; } = BrokerageName.Default;
        protected Resolution Resolution { get; set; } = Resolution.Minute;

        public override void Initialize()
        {
            SetStartDate(2024, 07, 20);
            SetEndDate(2024, 07, 29);
            SetCash(100000000);

            SetBrokerageModel(Brokerage);
            AddSymbols();
            _orderTypes = SymbolToTestPerOrderType.Keys.ToList();
            _orderTypeMethods = new()
            {
                { OrderType.Market, new Action<Slice>(slice => ExecuteMarketOrders()) },
                { OrderType.Limit, new Action<Slice>((slice) => ExecuteLimitOrders()) },
                { OrderType.StopMarket, new Action<Slice>((slice) => ExecuteStopMarketOrders()) },
                { OrderType.StopLimit, new Action<Slice>((slice) => ExecuteStopLimitOrders()) },
                { OrderType.MarketOnOpen, new Action<Slice>((slice) => ExecuteMarketOnOpenOrders()) },
                { OrderType.MarketOnClose, new Action<Slice>((slice) => ExecuteMarketOnCloseOrders()) },
                { OrderType.OptionExercise, new Action<Slice>((slice) => ExecuteOptionExerciseOrder()) },
                { OrderType.LimitIfTouched, new Action<Slice>((slice) => ExecuteLimitIfTouchedOrders()) },
                { OrderType.ComboMarket, new Action<Slice>((slice) => ExecuteComboMarketOrder(slice)) },
                { OrderType.ComboLimit, new Action<Slice>((slice) => ExecuteComboLimitOrder(slice)) },
                { OrderType.ComboLegLimit, new Action<Slice>((slice) => ExecuteComboLegLimitOrder(slice)) },
                { OrderType.TrailingStop, new Action<Slice>((slice) => ExecuteTrailingStopOrders()) },
            };
        }

        protected virtual void AddSymbols()
        {
            var defaultSecurityTypes = new List<Symbol>()
            {
                EquitySymbol,
                OptionContract,
                ForexSymbol,
                FutureContract,
                CfdSymbol,
                CryptoSymbol,
                FutureContract,
                IndexOptionContract,
                CryptoFutureSymbol,
            };

            SymbolToTestPerOrderType = new Dictionary<OrderType, List<Symbol>>() {
                { OrderType.Market, defaultSecurityTypes },
                { OrderType.Limit, defaultSecurityTypes },
                { OrderType.StopMarket, defaultSecurityTypes },
                { OrderType.StopLimit, defaultSecurityTypes },
                { OrderType.MarketOnOpen, defaultSecurityTypes.Where(x => x.SecurityType != SecurityType.Future && x.SecurityType != SecurityType.CryptoFuture).ToList() },
                { OrderType.MarketOnClose, defaultSecurityTypes.Where(x => x.SecurityType != SecurityType.Future && x.SecurityType != SecurityType.CryptoFuture).ToList() },
                { OrderType.OptionExercise, new List<Symbol>() { OptionContract, IndexOptionContract } },
                { OrderType.LimitIfTouched, defaultSecurityTypes },
                { OrderType.ComboMarket, new List<Symbol>() { OptionContract } },
                { OrderType.ComboLimit, new List<Symbol>() { OptionContract } },
                { OrderType.ComboLegLimit, new List<Symbol>() { OptionContract } },
                { OrderType.TrailingStop, defaultSecurityTypes } 
            };

            EquitySymbol = AddEquity("AAPL", Resolution).Symbol;
            ForexSymbol = AddForex("EURGBP", Resolution).Symbol;
            CryptoSymbol = AddCrypto("BTCUSD", Resolution).Symbol;
            CfdSymbol = AddCfd("XAUUSD", Resolution).Symbol;
            CryptoFutureSymbol = AddCryptoFuture("BTCUSD", Resolution).Symbol;

            var option = AddOption(EquitySymbol);
            _canonicalOption = option.Symbol;
            option.SetFilter(u => u.Strikes(-2, +2).Expiration(0, 60));

            var spx = AddIndex("SPX", Resolution).Symbol;
            var spxOptions = AddIndexOption(spx, Resolution);
            _canonicalIndexOption = spxOptions.Symbol;

            var future = AddFuture("ES", Resolution);
            future.SetFilter(TimeSpan.Zero, TimeSpan.FromDays(182));
        }

        public override void OnData(Slice slice)
        {
            if (!SetFutureContract(slice))
            {
                Debug($"{Time}: Waiting for future contract to be set...");
                return;
            }

            if (!SetOptionContract(slice))
            {
                Debug($"{Time}: Waiting for option contract to be set...");
                return;
            }

            if (!SetIndexOptionContract(slice))
            {
                Debug($"{Time}: Waiting for index option contract to be set...");
                return;
            }

            foreach (var symbol in Securities.Keys)
            {
                if (Securities[symbol].Price == 0)
                {
                    Debug($"{Time}: Waiting for {symbol} to have price...");
                    return;
                }
            }

            var testCase = _orderTypes[_testCaseIndex];
            _orderTypeMethods[testCase](slice);
            _testCaseIndex++;
            if (_testCaseIndex == _orderTypes.Count)
            {
                Quit();
            }
        }

        protected virtual decimal GetOrderPrice(Symbol symbol, bool aboveTheMarket)
        {
            var assetPrice = Securities[symbol].Price;

            if (aboveTheMarket)
            {
                if (symbol.SecurityType.IsOption() && assetPrice >= 2.95m)
                {
                    return (assetPrice + 0.05m).DiscretelyRoundBy(0.05m);
                }
                assetPrice = assetPrice + Math.Min(assetPrice * 0.001m, 0.25m);
            }
            else
            {
                if (symbol.SecurityType.IsOption() && assetPrice >= 2.95m)
                {
                    return (assetPrice - 0.05m).DiscretelyRoundBy(0.05m);
                }
                assetPrice = assetPrice - Math.Min(assetPrice * 0.001m, 0.25m);
            }
            return assetPrice;
        }

        protected virtual bool SetFutureContract(Slice slice)
        {
            if (FutureContract == null)
            {
                foreach (var chain in slice.FutureChains.Values)
                {
                    var contract = (from futuresContract in chain.OrderBy(x => x.Expiry)
                                    where futuresContract.Expiry > Time.Date.AddDays(90)
                                    select futuresContract
                    ).FirstOrDefault();
                    FutureContract = contract.Symbol;
                }
            }
            return FutureContract != null;
        }

        protected virtual bool SetOptionContract(Slice slice)
        {
            if (OptionContract == null)
            {
                foreach (var optionChain in slice.OptionChains.Values)
                {
                    var atmContract = optionChain
                        .Where(x => x.Symbol.Canonical == _canonicalOption)
                        .OrderByDescending(x => x.Expiry)
                        .ThenBy(x => Math.Abs(optionChain.Underlying.Price - x.Strike))
                        .ThenByDescending(x => x.Right)
                        .FirstOrDefault();
                    OptionContract = atmContract.Symbol;
                }
            }
            return OptionContract != null;
        }

        protected virtual bool SetIndexOptionContract(Slice slice)
        {
            if (IndexOptionContract == null)
            {
                foreach (var optionChain in slice.OptionChains.Values)
                {
                    var atmContract = optionChain
                        .Where(x => x.Symbol.Canonical == _canonicalIndexOption)
                        .OrderByDescending(x => x.Expiry)
                        .ThenBy(x => Math.Abs(optionChain.Underlying.Price - x.Strike))
                        .ThenByDescending(x => x.Right)
                        .FirstOrDefault();
                    if (atmContract != null)
                    {
                        IndexOptionContract = atmContract.Symbol;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return IndexOptionContract != null;
        }

        protected virtual void ExecuteMarketOrders()
        {
            if (Portfolio.Invested)
            {
                Debug($"{Time}: Liquidating so we start from scratch");
                Liquidate();
                return;
            }
            if (Transactions.GetOpenOrders().Count > 0)
            {
                Debug($"{Time}: Cancelling open orders so we start from scratch");
                Transactions.CancelOpenOrders();
                return;
            }

            Debug($"{Time}: Sending market orders");
            foreach (var symbol in SymbolToTestPerOrderType[OrderType.Market])
            {
                MarketOrder(symbol, 1);
            }
        }

        protected virtual void ExecuteLimitOrders()
        {
            Debug($"{Time}: Sending limit orders");
            foreach (var symbol in SymbolToTestPerOrderType[OrderType.Limit])
            {
                // bellow market price so triggers asap
                LimitOrder(symbol, -1, GetOrderPrice(symbol, aboveTheMarket: false));
            }
        }

        protected virtual void ExecuteStopMarketOrders()
        {
            if (Portfolio.Invested)
            {
                // should be filled
                Debug($"{Time}: Liquidating so we start from scratch");
                Liquidate();
                return;
            }

            Debug($"{Time}: Sending StopMarketOrder orders");
            foreach (var symbol in SymbolToTestPerOrderType[OrderType.StopMarket])
            {
                // Buy Stop order is always placed above the current market price
                StopMarketOrder(symbol, 1, GetOrderPrice(symbol, aboveTheMarket: true));
            }
        }

        protected virtual void ExecuteStopLimitOrders()
        {
            if (Transactions.GetOpenOrders().Count > 0)
            {
                if (OpenOrdersTimeout++ > 5)
                {
                    Debug($"{Time}: Tiemout waiting for orders to fill, cancelling");
                    Transactions.CancelOpenOrders();
                    return;
                }
                else
                {
                    Debug($"{Time}: Has open orders, waiting...");
                    return;
                }
            }

            Debug($"{Time}: Sending StopLimitOrder orders");
            foreach (var symbol in SymbolToTestPerOrderType[OrderType.StopLimit])
            {
                var aboveTheMarket = GetOrderPrice(symbol, aboveTheMarket: false);
                StopLimitOrder(symbol, -1, aboveTheMarket, aboveTheMarket);
            }
        }

        protected virtual void ExecuteMarketOnOpenOrders()
        {
            if (Time.Date != _last.Date) // each morning submit a market on open order
            {
                Debug($"{Time}: Sending MarketOnOpen orders");
                foreach (var symbol in SymbolToTestPerOrderType[OrderType.MarketOnOpen])
                {
                    MarketOnOpenOrder(symbol, 1);
                }

                _submittedMarketOnCloseToday = false;
                _last = Time;
            }
        }

        protected virtual void ExecuteMarketOnCloseOrders()
        {
            if (!_submittedMarketOnCloseToday) // once the exchange opens submit a market on close order
            {
                _submittedMarketOnCloseToday = true;
                _last = Time;
                Debug($"{Time}: Sending MarketOnClose orders");
                foreach (var symbol in SymbolToTestPerOrderType[OrderType.MarketOnClose])
                {
                    if (Securities[symbol].Exchange.ExchangeOpen)
                    {
                        MarketOnCloseOrder(symbol, 1);
                    }
                }
            }
        }

        protected virtual void ExecuteOptionExerciseOrder()
        {
            MarketOrder(OptionContract, 1);

            // Exercise option
            Debug($"{Time}: Exercising option contract");
            ExerciseOption(OptionContract, 1);
        }

        protected virtual void ExecuteLimitIfTouchedOrders()
        {
            Debug($"{Time}: Sending LimitIfTouched orders");
            foreach (var symbol in SymbolToTestPerOrderType[OrderType.MarketOnClose])
            {
                var aboveTheMarket = GetOrderPrice(symbol, aboveTheMarket: false);
                LimitIfTouchedOrder(symbol, 1, aboveTheMarket, aboveTheMarket);
            }
        }

        protected virtual void ExecuteComboMarketOrder(Slice slice)
        {
            OptionChain chain;
            if (IsMarketOpen(OptionContract) && slice.OptionChains.TryGetValue(OptionContract.Canonical, out chain))
            {
                var callContracts = chain.Where(contract => contract.Right == OptionRight.Call)
                    .GroupBy(x => x.Expiry)
                    .OrderBy(grouping => grouping.Key)
                    .First()
                    .OrderBy(x => x.Strike)
                    .ToList();

                // Let's wait until we have at least three contracts
                if (callContracts.Count < 2)
                {
                    return;
                }

                Debug($"{Time}: Sending combo market orders");
                var orderLegs = new List<Leg>()
                    {
                        Leg.Create(callContracts[0].Symbol, 1, GetOrderPrice(callContracts[0].Symbol, aboveTheMarket: false)),
                        Leg.Create(callContracts[1].Symbol, -1, GetOrderPrice(callContracts[1].Symbol, aboveTheMarket: false)),
                    };
                ComboMarketOrder(orderLegs, 2);
            }
        }

        protected virtual void ExecuteComboLimitOrder(Slice slice)
        {
            OptionChain chain;
            if (IsMarketOpen(OptionContract) && slice.OptionChains.TryGetValue(OptionContract.Canonical, out chain))
            {
                var callContracts = chain.Where(contract => contract.Right == OptionRight.Call)
                    .GroupBy(x => x.Expiry)
                    .OrderBy(grouping => grouping.Key)
                    .First()
                    .OrderBy(x => x.Strike)
                    .ToList();

                // Let's wait until we have at least three contracts
                if (callContracts.Count < 2)
                {
                    return;
                }

                Debug($"{Time}: Sending combo limit orders");
                var orderLegs = new List<Leg>()
                    {
                        Leg.Create(callContracts[0].Symbol, 1),
                        Leg.Create(callContracts[1].Symbol, -1),
                    };
                ComboLimitOrder(orderLegs, 2, GetOrderPrice(callContracts[0].Symbol, false));
            }
        }

        protected virtual void ExecuteComboLegLimitOrder(Slice slice)
        {
            OptionChain chain;
            if (IsMarketOpen(OptionContract) && slice.OptionChains.TryGetValue(OptionContract.Canonical, out chain))
            {
                var callContracts = chain.Where(contract => contract.Right == OptionRight.Call)
                    .GroupBy(x => x.Expiry)
                    .OrderBy(grouping => grouping.Key)
                    .First()
                    .OrderBy(x => x.Strike)
                    .ToList();

                // Let's wait until we have at least three contracts
                if (callContracts.Count < 2)
                {
                    return;
                }

                Debug($"{Time}: Sending combo leg limit orders");
                var orderLegs = new List<Leg>()
                    {
                        Leg.Create(callContracts[0].Symbol, 1, GetOrderPrice(callContracts[0].Symbol, aboveTheMarket: false)),
                        Leg.Create(callContracts[1].Symbol, -1, GetOrderPrice(callContracts[1].Symbol, aboveTheMarket: false)),
                    };
                ComboLegLimitOrder(orderLegs, 2);
            }
        }

        protected virtual void ExecuteTrailingStopOrders()
        {
            Debug($"{Time}: Sending TrailingStop orders");
            foreach (var symbol in SymbolToTestPerOrderType[OrderType.TrailingStop])
            {
                TrailingStopOrder(symbol, 1, 0.1m, true);
            }
        }
    }
}
