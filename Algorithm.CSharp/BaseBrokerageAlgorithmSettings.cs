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
using QuantConnect.Orders;
using QuantConnect.Securities;
using System.Collections.Generic;
using System;
using QuantConnect.Data;

namespace QuantConnect.Algorithm.CSharp
{
    public abstract class BaseBrokerageAlgorithmSettings
    {
        public Symbol OptionContract;
        public Symbol FutureContract;
        public Symbol FutureOptionContract;
        public Symbol IndexOptionContract;
        public abstract BrokerageName BrokerageName { get; }
        public abstract Resolution Resolution { get; }
        public virtual int OpenOrdersTimeout => 5;
        public virtual Symbol EquitySymbol => Symbol.Create("AAPL", SecurityType.Equity, Market.USA);
        public virtual Symbol ForexSymbol => Symbol.Create("EURUSD", SecurityType.Forex, Market.Oanda);
        public virtual Symbol CryptoSymbol => Symbol.Create("BTCUSDT", SecurityType.Crypto, Market.Bybit);
        public virtual Symbol CfdSymbol => Symbol.Create("XAUUSD", SecurityType.Cfd, Market.Oanda);
        public virtual Symbol CryptoFutureSymbol => Symbol.Create("BTCUSDT", SecurityType.CryptoFuture, Market.Bybit);
        public virtual Symbol CanonicalOptionSymbol => Symbol.Create("AAPL", SecurityType.Option, Market.USA);
        public virtual Func<OptionFilterUniverse, OptionFilterUniverse> OptionFilter => null;
        public virtual Symbol CanonicalIndexOptionSymbol => Symbol.Create("SPX", SecurityType.IndexOption, Market.USA);
        public virtual Func<OptionFilterUniverse, OptionFilterUniverse> IndexOptionFilter => null;
        public virtual Symbol CanonicalFutureSymbol => Symbol.Create("ES", SecurityType.Future, Market.CME);
        public virtual Func<FutureFilterUniverse, FutureFilterUniverse> FutureFilter => null;
        public virtual Symbol CanonicalFutureOptionSymbol => Symbol.CreateOption(CanonicalFutureSymbol, Market.CME, OptionStyle.American, OptionRight.Put, 1000, new DateTime(2024, 1, 1));
        public virtual Func<OptionFilterUniverse, OptionFilterUniverse> FutureOptionFilter => null;
        public abstract Dictionary<OrderType, List<Symbol>> SymbolToTestPerOrderType { get; protected set; }
        public abstract List<Symbol> SecurityTypesToAdd { get; protected set; }
        public abstract List<Symbol> SecurityTypes { get; protected set; }
        public abstract Dictionary<SecurityType, List<Resolution>> ResolutionsPerSecurity { get; protected set; }
        public abstract Dictionary<SecurityType, List<Type>> DataTypesPerSecurity { get; protected set; }
        public abstract void InitializeSymbols();
    }
}
