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
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Securities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.Algorithm.CSharp
{
    public class DefaultBrokerageAlgorithmSettings : BaseBrokerageAlgorithmSettings
    {
        public override BrokerageName BrokerageName => BrokerageName.Default;
        public override Resolution Resolution => Resolution.Minute;
        public override Func<FutureFilterUniverse, FutureFilterUniverse> FutureFilter => u => u.Expiration(0, 182);
        public override Func<OptionFilterUniverse, OptionFilterUniverse> FutureOptionFilter => u => u.Strikes(-2, +2).Expiration(0, 60);
        public override Func<OptionFilterUniverse, OptionFilterUniverse> OptionFilter => u => u.Strikes(-2, +2).Expiration(0, 60);
        public override Dictionary<OrderType, List<Symbol>> SymbolToTestPerOrderType { get; protected set; }
        public override List<Symbol> SecurityTypes { get; protected set; }
        public override List<Symbol> SecurityTypesToAdd { get; protected set; }
        public override Dictionary<SecurityType, List<Resolution>> ResolutionsPerSecurity { get ; protected set; }
        public override Dictionary<SecurityType, List<Type>> DataTypesPerSecurity { get; protected set; }

        public DefaultBrokerageAlgorithmSettings()
        {
            SecurityTypesToAdd = new List<Symbol>()
            {
                EquitySymbol,
                CanonicalOptionSymbol,
                ForexSymbol,
                CanonicalFutureSymbol,
                CanonicalFutureOptionSymbol,
                CfdSymbol,
                CryptoSymbol,
                CanonicalIndexOptionSymbol,
                CryptoFutureSymbol
            };
        }

        public override void InitializeSymbols()
        {
            SecurityTypes = new List<Symbol>
            {
                EquitySymbol,
                OptionContract,
                ForexSymbol,
                FutureContract,
                FutureOptionContract,
                CfdSymbol,
                CryptoSymbol,
                IndexOptionContract,
            };

            SymbolToTestPerOrderType = new Dictionary<OrderType, List<Symbol>>() {
                { OrderType.Market, SecurityTypes },
                { OrderType.Limit, SecurityTypes },
                { OrderType.StopMarket, SecurityTypes },
                { OrderType.StopLimit, SecurityTypes },
                { OrderType.MarketOnOpen, SecurityTypes.Where(x => x.SecurityType != SecurityType.Future && x.SecurityType != SecurityType.CryptoFuture).ToList() },
                { OrderType.MarketOnClose, SecurityTypes.Where(x => x.SecurityType != SecurityType.Future && x.SecurityType != SecurityType.CryptoFuture).ToList() },
                { OrderType.OptionExercise, new List<Symbol>() { OptionContract, IndexOptionContract } },
                { OrderType.LimitIfTouched, SecurityTypes },
                { OrderType.ComboMarket, new List<Symbol>() { OptionContract } },
                { OrderType.ComboLimit, new List<Symbol>() { OptionContract } },
                { OrderType.ComboLegLimit, new List<Symbol>() { OptionContract } },
                { OrderType.TrailingStop, SecurityTypes }
            };

            var allResolutions = new List<Resolution>()
            {
                Resolution.Daily,
                Resolution.Hour,
                Resolution.Minute,
                Resolution.Second,
            };

            ResolutionsPerSecurity = new Dictionary<SecurityType, List<Resolution>>()
            {
                { SecurityType.Equity, allResolutions  },
                { SecurityType.Option, allResolutions  },
                { SecurityType.Forex, allResolutions },
                { SecurityType.Future, allResolutions },
                { SecurityType.Cfd, allResolutions },
                { SecurityType.Crypto, allResolutions },
                { SecurityType.FutureOption, allResolutions },
                { SecurityType.IndexOption, allResolutions },
                { SecurityType.CryptoFuture, allResolutions },
            };

            var dataTypes = new List<Type>()
            {
                typeof(QuoteBar),
                typeof(TradeBar),
            };

            DataTypesPerSecurity = new Dictionary<SecurityType, List<Type>>()
            {
                { SecurityType.Equity, new List<Type>() { typeof(TradeBar) }  },
                { SecurityType.Option, dataTypes  },
                { SecurityType.Forex, new List<Type>() { typeof(QuoteBar) } },
                { SecurityType.Future, dataTypes },
                { SecurityType.Cfd, new List<Type>() { typeof(QuoteBar) } },
                { SecurityType.Crypto, dataTypes },
                { SecurityType.FutureOption, dataTypes },
                { SecurityType.IndexOption, dataTypes },
                { SecurityType.CryptoFuture, dataTypes }
            };
        }
    }
}
