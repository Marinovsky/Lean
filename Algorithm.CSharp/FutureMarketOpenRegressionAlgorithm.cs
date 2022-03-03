using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Interfaces;

namespace QuantConnect.Algorithm.CSharp
{
    public class FutureMarketOpenRegressionAlgorithm : QCAlgorithm, IRegressionAlgorithmDefinition
    {
        private Symbol _es;
        private static List<DateTime> _afterMarketOpen = new List<DateTime>() {
            new DateTime(2022, 02, 01, 16, 30, 0),
            new DateTime(2022, 02, 02, 16, 30, 0),
            new DateTime(2022, 02, 03, 16, 30, 0),
            new DateTime(2022, 02, 04, 16, 30, 0),
            new DateTime(2022, 02, 06, 18, 0, 0),
            new DateTime(2022, 02, 07, 16, 30, 0)
        };
        private static List<DateTime> _beforeMarketClose = new List<DateTime>()
        {
            new DateTime(2022, 02, 01, 16, 15, 0),
            new DateTime(2022, 02, 02, 16, 15, 0),
            new DateTime(2022, 02, 03, 16, 15, 0),
            new DateTime(2022, 02, 04, 16, 15, 0),
            new DateTime(2022, 02, 07, 16, 15, 0)
        };
        private Queue<DateTime> _afterMarketOpenQueue = new Queue<DateTime>(_afterMarketOpen);
        private Queue<DateTime> _beforeMarketCloseQueue = new Queue<DateTime>(_beforeMarketClose);

        public override void Initialize()
        {
            SetStartDate(2022, 02, 01);
            SetEndDate(2022, 02, 07);
            _es = AddFuture("ES").Symbol;

            Schedule.On(DateRules.EveryDay(_es),
                TimeRules.AfterMarketOpen(_es),
                EveryDayAfterMarketOpen);

            Schedule.On(DateRules.EveryDay(_es),
                TimeRules.BeforeMarketClose(_es),
                EveryDayBeforeMarketClose);
        }

        public void EveryDayBeforeMarketClose()
        {
            var expectedMarketClose = _beforeMarketCloseQueue.Dequeue();
            if (Time != expectedMarketClose)
            {
                throw new Exception($"Expected market close date was {expectedMarketClose} but received {Time}");
            }
        }

        public void EveryDayAfterMarketOpen()
        {
            var expectedMarketOpen = _afterMarketOpenQueue.Dequeue();
            if (Time != expectedMarketOpen)
            {
                throw new Exception($"Expected market open date was {expectedMarketOpen} but received {Time}");
            }
        }

        public override void OnEndOfAlgorithm()
        {
            if (!_afterMarketOpenQueue.Any() || !_beforeMarketCloseQueue.Any())
            {
                throw new Exception($"_afterMarketOpenQueue and _beforeMarketCloseQueue should be empty");
            }
        }
        public bool CanRunLocally { get; } = true;

        /// <summary>
        /// This is used by the regression test system to indicate which languages this algorithm is written in.
        /// </summary>
        public Language[] Languages { get; } = { Language.CSharp};

        /// <summary>
        /// This is used by the regression test system to indicate what the expected statistics are from running the algorithm
        /// </summary>
        public Dictionary<string, string> ExpectedStatistics => new Dictionary<string, string>
        {
            {"Total Trades", "0"},
            {"Average Win", "0%"},
            {"Average Loss", "0%"},
            {"Compounding Annual Return", "0%"},
            {"Drawdown", "0%"},
            {"Expectancy", "0"},
            {"Net Profit", "0%"},
            {"Sharpe Ratio", "0"},
            {"Probabilistic Sharpe Ratio", "0%"},
            {"Loss Rate", "0%"},
            {"Win Rate", "0%"},
            {"Profit-Loss Ratio", "0"},
            {"Alpha", "0"},
            {"Beta", "0"},
            {"Annual Standard Deviation", "0"},
            {"Annual Variance", "0"},
            {"Information Ratio", "0"},
            {"Tracking Error", "0"},
            {"Treynor Ratio", "0"},
            {"Total Fees", "$0.00"},
            {"Estimated Strategy Capacity", "$0"},
            {"Lowest Capacity Asset", ""},
            {"Fitness Score", "0"},
            {"Kelly Criterion Estimate", "0"},
            {"Kelly Criterion Probability Value", "0"},
            {"Sortino Ratio", "79228162514264337593543950335"},
            {"Return Over Maximum Drawdown", "79228162514264337593543950335"},
            {"Portfolio Turnover", "0"},
            {"Total Insights Generated", "0"},
            {"Total Insights Closed", "0"},
            {"Total Insights Analysis Completed", "0"},
            {"Long Insight Count", "0"},
            {"Short Insight Count", "0"},
            {"Long/Short Ratio", "100%"},
            {"Estimated Monthly Alpha Value", "$0"},
            {"Total Accumulated Estimated Alpha Value", "$0"},
            {"Mean Population Estimated Insight Value", "$0"},
            {"Mean Population Direction", "0%"},
            {"Mean Population Magnitude", "0%"},
            {"Rolling Averaged Population Direction", "0%"},
            {"Rolling Averaged Population Magnitude", "0%"},
            {"OrderListHash", "d41d8cd98f00b204e9800998ecf8427e"}
        };
    }
}
