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

using QuantConnect.Algorithm.CSharp.Benchmarks;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Packets;
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
    public abstract class TemplateFeatureRegressionAlgorithm: QCAlgorithm
    {
        public abstract BrokerageName Brokerage { get; set; }
        private int _testCaseIndex;
        private bool _submittedMarketOnCloseToday;
        private DateTime _last = DateTime.MinValue;
        private List<OrderType> _orderTypes;
        private Dictionary<OrderType, Func<Slice, List<OrderTicket>>> _orderTypeMethods;
        private bool _symbolsHaveBeenSetup;
        private BrokerageAlgorithmSettings _brokerageAlgorithmSettings;
        private int _openOrdersTimeout;
        private Dictionary<Symbol, int> _pointsFoundPerSymbol;
        private int _filledOrders;
        protected abstract string BrokerageSettingsURL { get; set; }

        public override void Initialize()
        {
            SetStartDate(2024, 07, 20);
            SetEndDate(2024, 07, 29);
            SetCash(100000000);
            SetCash("USDT", 10000);

            _brokerageAlgorithmSettings = new BrokerageAlgorithmSettings(BrokerageSettingsURL);
            _brokerageAlgorithmSettings.BrokerageName = Brokerage;
            SetBrokerageModel(_brokerageAlgorithmSettings.BrokerageName);
            AddSymbols();

            _orderTypeMethods = new()
            {
                { OrderType.Market, new Func<Slice, List<OrderTicket>>(slice => ExecuteMarketOrders()) },
                { OrderType.Limit, new Func<Slice, List<OrderTicket>>(slice => ExecuteLimitOrders()) },
                { OrderType.StopMarket, new Func<Slice, List<OrderTicket>>(slice => ExecuteStopMarketOrders()) },
                { OrderType.StopLimit, new Func<Slice, List<OrderTicket>>(slice => ExecuteStopLimitOrders()) },
                { OrderType.MarketOnOpen, new Func<Slice, List<OrderTicket>>(slice => ExecuteMarketOnOpenOrders()) },
                { OrderType.MarketOnClose, new Func<Slice, List<OrderTicket>>(slice => ExecuteMarketOnCloseOrders()) },
                { OrderType.OptionExercise, new Func<Slice, List<OrderTicket>>(slice => ExecuteOptionExerciseOrder()) },
                { OrderType.LimitIfTouched, new Func<Slice, List<OrderTicket>>(slice => ExecuteLimitIfTouchedOrders()) },
                { OrderType.ComboMarket, new Func<Slice, List<OrderTicket>>(slice => ExecuteComboMarketOrder(slice)) },
                { OrderType.ComboLimit, new Func<Slice, List<OrderTicket>>(slice => ExecuteComboLimitOrder(slice)) },
                { OrderType.ComboLegLimit, new Func<Slice, List<OrderTicket>>(slice => ExecuteComboLegLimitOrder(slice)) },
                { OrderType.TrailingStop, new Func<Slice, List<OrderTicket>>(slice => ExecuteTrailingStopOrders()) },
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
                _orderTypes = _orderTypeMethods.Keys.ToList();
                _pointsFoundPerSymbol = _brokerageAlgorithmSettings.SecurityTypes.ToDictionary(x => x, x => 0);
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

            foreach(var symbol in slice.Keys)
            {
                if (_brokerageAlgorithmSettings.SecurityTypes.Contains(symbol))
                {
                    _pointsFoundPerSymbol[symbol]++;
                }
            }

            var testCase = _orderTypes[_testCaseIndex];
            var result = _orderTypeMethods[testCase](slice);

            if (result != null && result.Any(x => x.Status == OrderStatus.Invalid) && (_brokerageAlgorithmSettings.SymbolToTestPerOrderType.ContainsKey(testCase)))
            {
                throw new RegressionTestException($"Brokerage was supposed to accept orders of type {testCase} but one order was invalid: {result}");
            }

            _testCaseIndex++;
            if (_testCaseIndex == _orderTypes.Count)
            {
                AssertHistory();
                Quit();
            }
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            if (orderEvent.Status == OrderStatus.Filled)
            {
                _filledOrders++;
            }
        }

        public override void OnEndOfAlgorithm()
        {
            foreach(var symbol in _pointsFoundPerSymbol.Where(x => x.Value == 0).Select(x => x.Key))
            {
                throw new RegressionTestException($"No data was found for {symbol} symbol");
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
                        Debug($"{type.Name} history request for {symbol.SecurityType} symbol {symbol} at {resolution} resolution ");
                        if (type == typeof(QuoteBar))
                        {
                            history = History<QuoteBar>(symbol, 50, resolution).ToList();
                        }
                        else if (type == typeof(TradeBar))
                        {
                            history = History<TradeBar>(symbol, 50, resolution).ToList();
                        }

                        if (history.Count() != 50)
                        {
                            throw new Exception($"50 {type.Name}'s were expected for {symbol.SecurityType} symbol {symbol} at {resolution} resolution, but just obtained {history.Count()}");
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
            if (_brokerageAlgorithmSettings.CanonicalFutureSymbol == null)
            {
                return true;
            }

            if (_brokerageAlgorithmSettings.FutureContract == null)
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
            if (_brokerageAlgorithmSettings.CanonicalOptionSymbol == null)
            {
                return true;
            }

            if (_brokerageAlgorithmSettings.OptionContract == null)
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
            if (_brokerageAlgorithmSettings.CanonicalFutureOptionSymbol == null)
            {
                return true;
            }

            if (_brokerageAlgorithmSettings.FutureOptionContract == null)
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
            if (_brokerageAlgorithmSettings.CanonicalIndexOptionSymbol == null)
            {
                return true;
            }

            if (_brokerageAlgorithmSettings.IndexOptionContract == null)
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

        protected virtual List<OrderTicket> ExecuteMarketOrders()
        {
            if (Portfolio.Invested)
            {
                Debug($"{Time}: Liquidating so we start from scratch");
                Liquidate();
                return null;
            }
            if (Transactions.GetOpenOrders().Count > 0)
            {
                Debug($"{Time}: Cancelling open orders so we start from scratch");
                Transactions.CancelOpenOrders();
                return null;
            }

            Debug($"{Time}: Sending market orders");
            var result = new List<OrderTicket>();
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.Market])
            {
                result.Add(MarketOrder(symbol, GetOrderQuantity(symbol)));
            }

            return result;
        }

        protected virtual List<OrderTicket> ExecuteLimitOrders()
        {
            Debug($"{Time}: Sending limit orders");
            var result = new List<OrderTicket>();
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.Limit])
            {
                // bellow market price so triggers asap
                result.Add(LimitOrder(symbol, -GetOrderQuantity(symbol), GetOrderPrice(symbol, aboveTheMarket: false)));
            }

            return result;
        }

        protected virtual List<OrderTicket> ExecuteStopMarketOrders()
        {
            if (Portfolio.Invested)
            {
                // should be filled
                Debug($"{Time}: Liquidating so we start from scratch");
                Liquidate();
                return null;
            }

            Debug($"{Time}: Sending StopMarketOrder orders");
            var result = new List<OrderTicket>();
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.StopMarket])
            {
                // Buy Stop order is always placed above the current market price
                result.Add(StopMarketOrder(symbol, GetOrderQuantity(symbol), GetOrderPrice(symbol, aboveTheMarket: true)));
            }

            return result;
        }

        protected virtual List<OrderTicket> ExecuteStopLimitOrders()
        {
            if (Transactions.GetOpenOrders().Count > 0)
            {
                if (_openOrdersTimeout++ > _brokerageAlgorithmSettings.OpenOrdersTimeout)
                {
                    Debug($"{Time}: Tiemout waiting for orders to fill, cancelling");
                    Transactions.CancelOpenOrders();
                    return null;
                }
                else
                {
                    Debug($"{Time}: Has open orders, waiting...");
                    return null;
                }
            }

            Debug($"{Time}: Sending StopLimitOrder orders");
            var result = new List<OrderTicket>();
            foreach (var symbol in _brokerageAlgorithmSettings.SymbolToTestPerOrderType[OrderType.StopLimit])
            {
                var aboveTheMarket = GetOrderPrice(symbol, aboveTheMarket: false);
                result.Add(StopLimitOrder(symbol, -GetOrderQuantity(symbol), aboveTheMarket, aboveTheMarket));
            }

            return result;
        }

        protected virtual List<OrderTicket> ExecuteMarketOnOpenOrders()
        {
            var result = new List<OrderTicket>();
            if (Time.Date != _last.Date) // each morning submit a market on open order
            {
                Debug($"{Time}: Sending MarketOnOpen orders");
                
                if (!_brokerageAlgorithmSettings.SymbolToTestPerOrderType.TryGetValue(OrderType.MarketOnOpen, out var symbols))
                {
                    symbols = _brokerageAlgorithmSettings.SecurityTypes; ;
                }
                foreach (var symbol in symbols)
                {
                    if (!Securities[symbol].Exchange.Hours.IsMarketAlwaysOpen)
                    {
                        result.Add(MarketOnOpenOrder(symbol, GetOrderQuantity(symbol)));
                    }
                }

                _submittedMarketOnCloseToday = false;
                _last = Time;
            }

            return result;
        }

        protected virtual List<OrderTicket> ExecuteMarketOnCloseOrders()
        {
            var result = new List<OrderTicket>();
            if (!_submittedMarketOnCloseToday) // once the exchange opens submit a market on close order
            {
                _submittedMarketOnCloseToday = true;
                _last = Time;
                Debug($"{Time}: Sending MarketOnClose orders");
                if (!_brokerageAlgorithmSettings.SymbolToTestPerOrderType.TryGetValue(OrderType.MarketOnClose, out var symbols))
                {
                    symbols = _brokerageAlgorithmSettings.SecurityTypes; ;
                }

                foreach (var symbol in symbols)
                {
                    if (Securities[symbol].Exchange.ExchangeOpen && !Securities[symbol].Exchange.Hours.IsMarketAlwaysOpen)
                    {
                        result.Add(MarketOnCloseOrder(symbol, GetOrderQuantity(symbol)));
                    }
                }
            }

            return result;
        }

        protected virtual List<OrderTicket> ExecuteOptionExerciseOrder()
        {
            if (_brokerageAlgorithmSettings.OptionContract == null)
            {
                return null;
            }

            MarketOrder(_brokerageAlgorithmSettings.OptionContract, 1);

            // Exercise option
            Debug($"{Time}: Exercising option contract");
            var result = new List<OrderTicket>();
            result.Add(ExerciseOption(_brokerageAlgorithmSettings.OptionContract, (int)GetOrderQuantity(_brokerageAlgorithmSettings.OptionContract)));
            return result;
        }

        protected virtual List<OrderTicket> ExecuteLimitIfTouchedOrders()
        {
            if (!_brokerageAlgorithmSettings.SymbolToTestPerOrderType.TryGetValue(OrderType.LimitIfTouched, out var symbols))
            {
                symbols = _brokerageAlgorithmSettings.SecurityTypes; ;
            }

            Debug($"{Time}: Sending LimitIfTouched orders");
            var result = new List<OrderTicket>();
            foreach (var symbol in symbols)
            {
                var aboveTheMarket = GetOrderPrice(symbol, aboveTheMarket: false);
                result.Add(LimitIfTouchedOrder(symbol, GetOrderQuantity(symbol), aboveTheMarket, aboveTheMarket));
            }

            return result;
        }

        protected virtual List<OrderTicket> ExecuteComboMarketOrder(Slice slice)
        {
            if (_brokerageAlgorithmSettings.OptionContract == null)
            {
                return null;
            }

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
                    return null;
                }

                Debug($"{Time}: Sending combo market orders");
                var orderLegs = new List<Leg>()
                    {
                        Leg.Create(callContracts[0].Symbol, (int)GetOrderQuantity(callContracts[0].Symbol), GetOrderPrice(callContracts[0].Symbol, aboveTheMarket: false)),
                        Leg.Create(callContracts[1].Symbol, -(int)GetOrderQuantity(callContracts[1].Symbol), GetOrderPrice(callContracts[1].Symbol, aboveTheMarket: false)),
                    };
                return ComboMarketOrder(orderLegs, 2);
            }

            return null;
        }

        protected virtual List<OrderTicket> ExecuteComboLimitOrder(Slice slice)
        {
            if (_brokerageAlgorithmSettings.OptionContract == null)
            {
                return null;
            }

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
                    return null;
                }

                Debug($"{Time}: Sending combo limit orders");
                var orderLegs = new List<Leg>()
                    {
                        Leg.Create(callContracts[0].Symbol, (int)GetOrderQuantity(callContracts[0].Symbol)),
                        Leg.Create(callContracts[1].Symbol, -(int)GetOrderQuantity(callContracts[1].Symbol)),
                    };
                return ComboLimitOrder(orderLegs, 2, GetOrderPrice(callContracts[0].Symbol, false));
            }

            return null;
        }

        protected virtual List<OrderTicket> ExecuteComboLegLimitOrder(Slice slice)
        {
            if (_brokerageAlgorithmSettings.OptionContract == null)
            {
                return null;
            }

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
                    return null;
                }

                Debug($"{Time}: Sending combo leg limit orders");
                var orderLegs = new List<Leg>()
                    {
                        Leg.Create(callContracts[0].Symbol, (int)GetOrderQuantity(callContracts[0].Symbol), GetOrderPrice(callContracts[0].Symbol, aboveTheMarket: false)),
                        Leg.Create(callContracts[1].Symbol, -(int)GetOrderQuantity(callContracts[1].Symbol), GetOrderPrice(callContracts[1].Symbol, aboveTheMarket: false)),
                    };
                return ComboLegLimitOrder(orderLegs, 2);
            }

            return null;
        }

        protected virtual List<OrderTicket> ExecuteTrailingStopOrders()
        {
            if (!_brokerageAlgorithmSettings.SymbolToTestPerOrderType.TryGetValue(OrderType.TrailingStop, out var symbols))
            {
                symbols = _brokerageAlgorithmSettings.SecurityTypes; ;
            }

            Debug($"{Time}: Sending TrailingStop orders");
            var result = new List<OrderTicket>();
            foreach (var symbol in symbols)
            {
                result.Add(TrailingStopOrder(symbol, (int)GetOrderQuantity(symbol), 0.1m, true));
            }

            return result;
        }

        protected virtual decimal GetOrderQuantity(Symbol symbol)
        {
            if (symbol.SecurityType == SecurityType.Crypto || symbol.SecurityType == SecurityType.CryptoFuture)
            {
                return 0.1m;
            }
            else
            {
                return 1;
            }
        }
    }
}
