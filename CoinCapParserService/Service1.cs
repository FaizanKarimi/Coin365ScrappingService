using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;

namespace CoinCapParserService
{
    public partial class Service1 : ServiceBase
    {
        #region Timers
        private Timer _timerSignalRServer;
        private Timer _timerPhysicalCurrencies;
        private Timer _timerCurrenciesCategory;
        private Timer _timerMarketCap;
        private Timer _timerCurrenciesInCache;
        private Timer _timerOneWeekData;
        private Timer _timerOneMonthData;
        private Timer _timerThreeMonthData;
        private Timer _timerOneYearData;
        private Timer _timerAllData;
        private Timer _timerAllExchanges;
        private Timer _timerSaveChartsData;
        #endregion

        #region Timer Callbacks
        private TimerCallback _timerCallbackSignalRServer;
        private TimerCallback _timerCallbackPhysicalCurrencies;
        private TimerCallback _timerCallbackCurrenciesCategory;
        private TimerCallback _timerCallbackMarketCap;
        private TimerCallback _timerCallbackCurrenciesInCache;
        private TimerCallback _timerCallbackOneWeekData;
        private TimerCallback _timerCallbackOneMonthData;
        private TimerCallback _timerCallbackThreeMonthData;
        private TimerCallback _timerCallbackOneYearData;
        private TimerCallback _timerCallbackAllData;
        private TimerCallback _timerCallbackAllExchanges;
        private TimerCallback _timerCallbackSaveChartsData;
        #endregion

        #region Private Memebers
        private JobExecuter _jobExecuter;
        private CoinCapParser _coinCapParser;
        private const int _zero = 0;
        private const int _fivteenSeconds = 15000;
        private const int _thirtySeconds = 30000;
        private const int _nintySeconds = 90000;
        private const int _oneMinute = 60000;
        private const int _twoMinutes = 120000;
        private const int _twoAndHalfMinutes = 150000;
        private const int _threeMinutes = 180000;
        private const int _threeAndHalfMinutes = 210000;
        private const int _thiryMinutes = 1800000;
        private const int _oneHour = 3600000;
        private const int _twoHours = 7200000;
        private const int _threeHours = 10800000;
        private const int _oneDay = 86400000;
        #endregion

        public Service1()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
#if DEBUG
            Debugger.Launch();
#endif
            ApplicationCache.IsServerStarted = false;

            _jobExecuter = new JobExecuter();
            _coinCapParser = new CoinCapParser();

            _timerCallbackSignalRServer = new TimerCallback(_jobExecuter.StartSignalRServer);
            _timerSignalRServer = new Timer(_timerCallbackSignalRServer, _coinCapParser, _zero, _oneMinute);

            _timerCallbackPhysicalCurrencies = new TimerCallback(_jobExecuter.GetAndSavePhysicalCurrencies);
            _timerPhysicalCurrencies = new Timer(_timerCallbackPhysicalCurrencies, _coinCapParser, _zero, _threeMinutes);

            _timerCallbackCurrenciesCategory = new TimerCallback(_jobExecuter.SaveCurrenciesCategories);
            _timerCurrenciesCategory = new Timer(_timerCallbackCurrenciesCategory, _coinCapParser, _zero, _oneDay);

            _timerCallbackMarketCap = new TimerCallback(_jobExecuter.SaveMarketCapData);
            _timerMarketCap = new Timer(_timerCallbackMarketCap, _coinCapParser, _zero, _fivteenSeconds);

            _timerCallbackCurrenciesInCache = new TimerCallback(_jobExecuter.SaveAllCurrenciesInCache);
            _timerCurrenciesInCache = new Timer(_timerCallbackCurrenciesInCache, _coinCapParser, _fivteenSeconds, _thirtySeconds);

            _timerCallbackSaveChartsData = new TimerCallback(_jobExecuter.SaveChartsData);
            _timerSaveChartsData = new Timer(_timerCallbackSaveChartsData, _coinCapParser, _thirtySeconds, _oneHour);

            _timerCallbackOneWeekData = new TimerCallback(_jobExecuter.ParseOneWeekData);
            _timerOneWeekData = new Timer(_timerCallbackOneWeekData, _coinCapParser, _thirtySeconds, _oneDay);

            _timerCallbackOneMonthData = new TimerCallback(_jobExecuter.ParseOneMonthData);
            _timerOneMonthData = new Timer(_timerCallbackOneMonthData, _coinCapParser, _oneMinute, _oneDay);

            _timerCallbackThreeMonthData = new TimerCallback(_jobExecuter.ParseThreeMonthData);
            _timerThreeMonthData = new Timer(_timerCallbackThreeMonthData, _coinCapParser, _nintySeconds, _oneDay);

            _timerCallbackOneYearData = new TimerCallback(_jobExecuter.ParseOneYearData);
            _timerOneYearData = new Timer(_timerCallbackOneYearData, _coinCapParser, _twoAndHalfMinutes, _oneDay);

            _timerCallbackAllData = new TimerCallback(_jobExecuter.ParseAllData);
            _timerAllData = new Timer(_timerCallbackAllData, _coinCapParser, _threeMinutes, _oneDay);

            _timerCallbackAllExchanges = new TimerCallback(_jobExecuter.GetAndSaveExchangesData);
            _timerAllExchanges = new Timer(_timerCallbackAllExchanges, _coinCapParser, _threeAndHalfMinutes, _thiryMinutes);
        }

        protected override void OnStop()
        {
            _timerSignalRServer.Dispose();
            _timerPhysicalCurrencies.Dispose();
            _timerCurrenciesCategory.Dispose();
            _timerMarketCap.Dispose();
            _timerCurrenciesInCache.Dispose();
            _timerOneWeekData.Dispose();
            _timerOneMonthData.Dispose();
            _timerThreeMonthData.Dispose();
            _timerOneYearData.Dispose();
            _timerAllData.Dispose();
            _timerAllExchanges.Dispose();
        }
    }
}