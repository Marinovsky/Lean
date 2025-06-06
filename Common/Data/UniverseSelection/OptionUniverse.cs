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

using System;
using System.IO;
using System.Runtime.CompilerServices;
using QuantConnect.Data.Market;
using QuantConnect.Python;

namespace QuantConnect.Data.UniverseSelection
{
    /// <summary>
    /// Represents a universe of options data
    /// </summary>
    public class OptionUniverse : BaseChainUniverseData
    {
        private const int StartingGreeksCsvIndex = 7;

        /// <summary>
        /// Open interest value of the option
        /// </summary>
        public override decimal OpenInterest
        {
            get
            {
                ThrowIfNotAnOption(nameof(OpenInterest));
                return base.OpenInterest;
            }
        }

        /// <summary>
        /// Implied volatility value of the option
        /// </summary>
        public decimal ImpliedVolatility
        {
            get
            {
                ThrowIfNotAnOption(nameof(ImpliedVolatility));
                return CsvLine.GetDecimalFromCsv(6);
            }
        }

        /// <summary>
        /// Greeks values of the option
        /// </summary>
        public Greeks Greeks
        {
            get
            {
                ThrowIfNotAnOption(nameof(Greeks));
                return new PreCalculatedGreeks(CsvLine);
            }
        }

        /// <summary>
        /// Creates a new instance of the <see cref="OptionUniverse"/> class
        /// </summary>
        public OptionUniverse()
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="OptionUniverse"/> class
        /// </summary>
        public OptionUniverse(DateTime date, Symbol symbol, string csv)
            : base(date, symbol, csv)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="OptionUniverse"/> class as a copy of the given instance
        /// </summary>
        public OptionUniverse(OptionUniverse other)
            : base(other)
        {
        }

        /// <summary>
        /// Reader converts each line of the data source into BaseData objects. Each data type creates its own factory method, and returns a new instance of the object
        /// each time it is called.
        /// </summary>
        /// <param name="config">Subscription data config setup object</param>
        /// <param name="stream">Stream reader of the source document</param>
        /// <param name="date">Date of the requested data</param>
        /// <param name="isLiveMode">true if we're in live mode, false for backtesting mode</param>
        /// <returns>Instance of the T:BaseData object generated by this line of the CSV</returns>
        [StubsIgnore]
        public override BaseData Reader(SubscriptionDataConfig config, StreamReader stream, DateTime date, bool isLiveMode)
        {
            if (TryRead(config, stream, date, out var symbol, out var remainingLine))
            {
                return new OptionUniverse(date, symbol, remainingLine);
            }

            return null;
        }

        /// <summary>
        /// Adds a new data point to this collection.
        /// If the data point is for the underlying, it will be stored in the <see cref="BaseDataCollection.Underlying"/> property.
        /// </summary>
        /// <param name="newDataPoint">The new data point to add</param>
        public override void Add(BaseData newDataPoint)
        {
            if (newDataPoint is BaseChainUniverseData optionUniverseDataPoint)
            {
                if (optionUniverseDataPoint.Symbol.HasUnderlying)
                {
                    optionUniverseDataPoint.Underlying = Underlying;
                    base.Add(optionUniverseDataPoint);
                }
                else
                {
                    Underlying = optionUniverseDataPoint;
                    foreach (BaseChainUniverseData data in Data)
                    {
                        data.Underlying = optionUniverseDataPoint;
                    }
                }
            }
        }

        /// <summary>
        /// Creates a copy of the instance
        /// </summary>
        /// <returns>Clone of the instance</returns>
        public override BaseData Clone()
        {
            return new OptionUniverse(this);
        }

        /// <summary>
        /// Gets the CSV string representation of this universe entry
        /// </summary>
        public static string ToCsv(Symbol symbol, decimal open, decimal high, decimal low, decimal close, decimal volume, decimal? openInterest,
            decimal? impliedVolatility, Greeks greeks)
        {
            if (symbol.SecurityType == SecurityType.FutureOption || symbol.SecurityType == SecurityType.Future)
            {
                return $"{symbol.ID},{symbol.Value},{open},{high},{low},{close},{volume},{openInterest}";
            }

            return $"{symbol.ID},{symbol.Value},{open},{high},{low},{close},{volume},"
                + $"{openInterest},{impliedVolatility},{greeks?.Delta},{greeks?.Gamma},{greeks?.Vega},{greeks?.Theta},{greeks?.Rho}";
        }

        /// <summary>
        /// Implicit conversion into <see cref="Symbol"/>
        /// </summary>
        /// <param name="data">The option universe data to be converted</param>
#pragma warning disable CA2225 // Operator overloads have named alternates
        public static implicit operator Symbol(OptionUniverse data)
#pragma warning restore CA2225 // Operator overloads have named alternates
        {
            return data.Symbol;
        }

        /// <summary>
        /// Gets the CSV header string for this universe entry
        /// </summary>
        public static string CsvHeader(SecurityType securityType)
        {
            // FOPs don't have greeks
            if (securityType == SecurityType.FutureOption || securityType == SecurityType.Future)
            {
                return "symbol_id,symbol_value,open,high,low,close,volume,open_interest";
            }

            return "symbol_id,symbol_value,open,high,low,close,volume,open_interest,implied_volatility,delta,gamma,vega,theta,rho";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfNotAnOption(string propertyName)
        {
            if (!Symbol.SecurityType.IsOption())
            {
                throw new InvalidOperationException($"{propertyName} is only available for options.");
            }
        }

        /// <summary>
        /// Pre-calculated greeks lazily parsed from csv line.
        /// It parses the greeks values from the csv line only when they are requested to avoid holding decimals in memory.
        /// </summary>
        private class PreCalculatedGreeks : Greeks
        {
            private readonly string _csvLine;

            public override decimal Delta => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex);

            public override decimal Gamma => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex + 1);

            public override decimal Vega => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex + 2);

            public override decimal Theta => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex + 3);

            public override decimal Rho => _csvLine.GetDecimalFromCsv(StartingGreeksCsvIndex + 4);

            [PandasIgnore]
            public override decimal Lambda => decimal.Zero;

            /// <summary>
            /// Initializes a new default instance of the <see cref="PreCalculatedGreeks"/> class
            /// </summary>
            public PreCalculatedGreeks(string csvLine)
            {
                _csvLine = csvLine;
            }

            /// <summary>
            /// Gets a string representation of the greeks values
            /// </summary>
            public override string ToString()
            {
                return $"D: {Delta}, G: {Gamma}, V: {Vega}, T: {Theta}, R: {Rho}";
            }
        }
    }
}
