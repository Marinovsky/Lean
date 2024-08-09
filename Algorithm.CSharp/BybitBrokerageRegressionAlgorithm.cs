using QuantConnect.Brokerages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Algorithm.CSharp
{
    public class BybitBrokerageRegressionAlgorithm : TemplateFeatureRegressionAlgorithm
    {
        public override BrokerageName Brokerage { get; set; } = BrokerageName.Bybit;
        protected override string BrokerageSettingsURL { get; set; } = "https://raw.githubusercontent.com/QuantConnect/Lean.Brokerages.ByBit/master/bybit.json";
    }
}
