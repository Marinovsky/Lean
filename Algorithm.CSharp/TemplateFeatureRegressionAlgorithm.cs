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

using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Securities.Future;
using QuantConnect.Securities.FutureOption;
using QuantConnect.Securities.IndexOption;
using QuantConnect.Securities.Option;
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
        private List<OrderType> _orderTypes;
        private Dictionary<OrderType, Action<Slice>> _orderTypeMethods;
        private bool _symbolsHaveBeenSetup;
        private BaseBrokerageAlgorithmSettings _brokerageAlgorithmSettings;
        private int _openOrdersTimeout;

        public override void Initialize()
        {
            SetStartDate(2024, 07, 20);
            SetEndDate(2024, 07, 29);
            SetCash(100000000);
            SetCash("USDT", 10000);

            _brokerageAlgorithmSettings = new DefaultBrokerageAlgorithmSettings();
            SetBrokerageModel(_brokerageAlgorithmSettings.BrokerageName);
            AddSymbols();

            _orderTypeMethods = new()
            {
                { OrderType.Market, new Action<Slice>(slice => ExecuteMarketOrders()) },
                { OrderType.Limit, new Action<Slice>(slice => ExecuteLimitOrders()) },
                { OrderType.StopMarket, new Action<Slice>(slice => ExecuteStopMarketOrders()) },
                { OrderType.StopLimit, new Action<Slice>(slice => ExecuteStopLimitOrders()) },
                { OrderType.MarketOnOpen, new Action<Slice>(slice => ExecuteMarketOnOpenOrders()) },
                { OrderType.MarketOnClose, new Action<Slice>(slice => ExecuteMarketOnCloseOrders()) },
                { OrderType.OptionExercise, new Action<Slice>(slice => ExecuteOptionExerciseOrder()) },
                { OrderType.LimitIfTouched, new Action<Slice>(slice => ExecuteLimitIfTouchedOrders()) },
                { OrderType.ComboMarket, new Action<Slice>(slice => ExecuteComboMarketOrder(slice)) },
                { OrderType.ComboLimit, new Action<Slice>(slice => ExecuteComboLimitOrder(slice)) },
                { OrderType.ComboLegLimit, new Action<Slice>(slice => ExecuteComboLegLimitOrder(slice)) },
                { OrderType.TrailingStop, new Action<Slice>(slice => ExecuteTrailingStopOrders()) },
            };
        }

        protected virtual void AddSymbols()
        {
            foreach(var symbol in _brokerageAlgorithmSettings.SecurityTypesToAdd)
            {
                if (symbol.Underlying?.SecurityType == SecurityType.Future)
                {
                    AddFutureOption(symbol.Underlying, _brokerageAlgorithmSettings.FutureOptionFilter);
                    continue;
                }

                var security = AddSecurity(symbol, _brokerageAlgorithmSettings.Resolution);
                dynamic filter;
                switch (symbol.SecurityType)
                {
                    case SecurityType.Option:
                        filter = _brokerageAlgorithmSettings.OptionFilter;
                        break;
                    case SecurityType.Future:
                        filter = _brokerageAlgorithmSettings.FutureFilter;
                        break;
                    case SecurityType.IndexOption:
                        filter = _brokerageAlgorithmSettings.IndexOptionFilter;
                        break;
                    case SecurityType.FutureOption:
                        filter = _brokerageAlgorithmSettings.FutureOptionFilter;
                        break;
                    default:
                        filter = null;
                        break;
                }

                if (filter != null)
                {
                    switch (symbol.SecurityType)
                    {
                        case SecurityType.Option:
                            (security as Option).SetFilter(filter);
                            break;
                        case SecurityType.Future:
                            (security as Future).SetFilter(filter);
                            break;
                        case SecurityType.FutureOption:
                            (security as FutureOption).SetFilter(filter);
                            break;
                        case SecurityType.IndexOption:
                            (security as IndexOption).SetFilter(filter);
                            break;
                    }
                }
            }
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

            if (!SetFutureOptionContract(slice))
            {
                Debug($"{Time}: Waiting for future option contract to be set...");
                return;
            }

            if (!_symbolsHaveBeenSetup)
            {
                _brokerageAlgorithmSettings.InitializeSymbols();
                _orderTypes = _brokerageAlgorithmSettings.SymbolToTestPerOrderType.Keys.ToList();
                _symbolsHaveBeenSetup = true;
            }

            foreach (var symbol in _brokerageAlgorithmSettings.SecurityTypes)
            {
                if (!symbol.IsCanonical() && Securities[symbol].Price == 0)
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
                AssertHistory();
                Quit();
            }
        }

        protected virtual void AssertHistory()
        {
            IEnumerable<IBaseData> history = default;
            foreach (var symbol in _brokerageAlgorithmSettings.SecurityTypes)
            {
                foreach(var resolution in _brokerageAlgorithmSettings.ResolutionsPerSecurity[symbol.SecurityType])
                {
                    foreach(var type in _brokerageAlgorithmSettings.DataTypesPerSecurity[symbol.SecurityType])
                    {
                        Debug($"{type.Name} history request for symbol {symbol} at {resolution} resolution ");
                        if (type == typeof(QuoteBar))
                        {
                            history = History<QuoteBar>(symbol, TimeSpan.FromDays(10), resolution).ToList();
                        }
                        else if (type == typeof(TradeBar))
                        {
                            history = History<TradeBar>(symbol, TimeSpan.FromDays(10), resolution).ToList();
                        }
                    }
                }
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
            if (_brokerageAlgorithmSettings.CanonicalFutureSymbol != null && _brokerageAlgorithmSettings.FutureContract == null)
            {
                foreach (var chain in slice.FutureChains.Values)
                {
                    var contract = (from futuresContract in chain.OrderBy(x => x.Expiry)
                                    where futuresContract.Expiry > Time.Date.AddDays(29)
                                    select futuresContract
                    ).FirstOrDefault();
                    if (contract != null)
                    {
                        _brokerageAlgorithmSettings.FutureContract = contract.Symbol;
                        break;
                    }
                }
            }
            return _brokerageAlgorithmSettings.FutureContract != null;
        }

        protected virtual bool SetOptionContract(Slice slice)
        {
            if (_brokerageAlgorithmSettings.CanonicalOptionSymbol != null && _brokerageAlgorithmSettings.OptionContract == null)
            {
                foreach (var optionChain in slice.OptionChains.Values)
                {
                    var atmContract = optionChain
                        .Where(x => x.Symbol.Canonical == _brokerageAlgorithmSettings.CanonicalOptionSymbol)
                        .OrderByDescending(x => x.Expiry)
                        .ThenBy(x => Math.Abs(optionChain.Underlying.Price - x.Strike))
                        .ThenByDescending(x => x.Right)
                        .FirstOrDefault();
                    if (atmContract != null)
                    {
                        _brokerageAlgorithmSettings.OptionContract = atmContract.Symbol;
                        break;
                    }
                }
            }
            return _brokerageAlgorithmSettings.OptionContract != null;
        }

        protected virtual bool SetFutureOptionContract(Slice slice)
        {
            if (_brokerageAlgorithmSettings.CanonicalFutureOptionSymbol != null && _brokerageAlgorithmSettings.FutureOptionContract == null)
            {
                foreach (var optionChain in slice.OptionChains.Values)
                {
                    var atmContract = optionChain
                        .Where(x => x.Symbol.SecurityType == SecurityType.FutureOption)
                        .OrderByDescending(x => x.Expiry)
                        .ThenBy(x => Math.Abs(optionChain.Underlying.Price - x.Strike))
                        .ThenByDescending(x => x.Right)
                        .FirstOrDefault();
                    if (atmContract != null)
                    {
                        _brokerageAlgorithmSettings.FutureOptionContract = atmContract.Symbol;
                        break;
                    }
                }
            }
            return _brokerageAlgorithmSettings.FutureOptionContract != null;
        }

        protected virtual bool SetIndexOptionContract(Slice slice)
        {
            if (_brokerageAlgorithmSettings.CanonicalIndexOptionSymbol != null && _brokerageAlgorithmSettings.IndexOptionContract == null)
            {
                foreach (var optionChain in slice.OptionChains.Values)
                {
                    var atmContract = optionChain
                        .Where(x => x.Symbol.Canonical == _brokerageAlgorithmSettings.CanonicalIndexOptionSymbol)
                        .OrderByDescending(x => x.Expiry)
                        .ThenBy(x => Math.Abs(optionChain.Underlying.Price - x.Strike))
                        .ThenByDescending(x => x.Right)
                        .FirstOrDefault();
                    if (atmContract != null)
                    {
                        _brokerageAlgorithmSettings.IndexOptionContract = atmContract.Symbol;
                        break;
                    }
                }
            }
            return _brokerageAlgorithmSettings.IndexOptionContract != null;
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
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.Market])
            {
                MarketOrder(symbol, 1);
            }
        }

        protected virtual void ExecuteLimitOrders()
        {
            Debug($"{Time}: Sending limit orders");
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.Limit])
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
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.StopMarket])
            {
                // Buy Stop order is always placed above the current market price
                StopMarketOrder(symbol, 1, GetOrderPrice(symbol, aboveTheMarket: true));
            }
        }

        protected virtual void ExecuteStopLimitOrders()
        {
            if (Transactions.GetOpenOrders().Count > 0)
            {
                if (_openOrdersTimeout++ > _brokerageAlgorithmSettings.OpenOrdersTimeout)
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
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.StopLimit])
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
                foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.MarketOnOpen])
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
                foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.MarketOnClose])
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
            MarketOrder(_brokerageAlgorithmSettings.OptionContract, 1);

            // Exercise option
            Debug($"{Time}: Exercising option contract");
            ExerciseOption(_brokerageAlgorithmSettings.OptionContract, 1);
        }

        protected virtual void ExecuteLimitIfTouchedOrders()
        {
            Debug($"{Time}: Sending LimitIfTouched orders");
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.MarketOnClose])
            {
                var aboveTheMarket = GetOrderPrice(symbol, aboveTheMarket: false);
                LimitIfTouchedOrder(symbol, 1, aboveTheMarket, aboveTheMarket);
            }
        }

        protected virtual void ExecuteComboMarketOrder(Slice slice)
        {
            OptionChain chain;
            if (IsMarketOpen(_brokerageAlgorithmSettings.OptionContract) && slice.OptionChains.TryGetValue(_brokerageAlgorithmSettings.CanonicalOptionSymbol, out chain))
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
            if (IsMarketOpen(_brokerageAlgorithmSettings.OptionContract) && slice.OptionChains.TryGetValue(_brokerageAlgorithmSettings.CanonicalOptionSymbol, out chain))
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
            if (IsMarketOpen(_brokerageAlgorithmSettings.OptionContract) && slice.OptionChains.TryGetValue(_brokerageAlgorithmSettings.CanonicalOptionSymbol, out chain))
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
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.TrailingStop])
            {
                TrailingStopOrder(symbol, 1, 0.1m, true);
            }
        }
    }
}
