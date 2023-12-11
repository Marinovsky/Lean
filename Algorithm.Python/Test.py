from AlgorithmImports import *

class Test(QCAlgorithm):
    def Initialize(self):
        self.SetStartDate(2023, 5, 28);
        self.SetEndDate(2023, 6, 28);

        self.AddEquity("SPY", Resolution.Daily)
        self.SetStatisticsService(Custom2StatisticsService())

    def OnEndOfAlgorithm(self):
        statistics = self.Statistics
        self.Log(f"Statistics: {statistics}")

class Custom2StatisticsService:
    def StatisticsResults(self):
        return StatisticsResults()
    def SetSummaryStatistics(self, name, value):
        print(f"Name: {name} Value: {value}")
