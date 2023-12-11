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
 *
*/

using Python.Runtime;

namespace QuantConnect.Statistics
{
    public class IStatisticsServicePythonWrapper : IStatisticsService
    {
        private readonly dynamic _model;
        public IStatisticsServicePythonWrapper(PyObject model)
        {
            _model = model;
        }

        public void SetSummaryStatistic(string name, string value)
        {
            using (Py.GIL())
            {
                _model.SetSummaryStatistic(name, value);
            }
        }

        public StatisticsResults StatisticsResults()
        {
            using (Py.GIL())
            {
                return (_model.StatisticsResults() as PyObject).GetAndDispose<StatisticsResults>();
            }
        }
    }
}
