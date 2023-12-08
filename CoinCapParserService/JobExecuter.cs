namespace CoinCapParserService
{
    public class JobExecuter
    {
        public JobExecuter() { }

        public void StartSignalRServer(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            if (!ApplicationCache.IsServerStarted)
            {
                coinCapParser.StartSignalRServer();
            }
        }

        public void GetAndSavePhysicalCurrencies(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.GetAndSavePhysicalCurrencies();
        }

        public void SaveCurrenciesCategories(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.SaveCurrenciesCategories();
        }

        public void SaveMarketCapData(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.SaveMarketCapData();
        }

        public void SaveAllCurrenciesInCache(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.SaveAllCurrenciesInCache();
        }

        public void SaveChartsData(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.SaveChartsData();
        }

        public void ParseOneWeekData(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.ParseOneWeekData();
        }

        public void ParseOneMonthData(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.ParseOneMonthData();
        }

        public void ParseThreeMonthData(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.ParseThreeMonthData();
        }

        public void ParseSixMonthData(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.ParseSixMonthData();
        }

        public void ParseOneYearData(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.ParseOneYearData();
        }

        public void ParseAllData(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.ParseAllData();
        }

        public void GetAndSaveExchangesData(object state)
        {
            CoinCapParser coinCapParser = state as CoinCapParser;
            coinCapParser.GetAndSaveExchangesData();
        }
    }
}