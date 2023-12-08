using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Threading;
using HtmlAgilityPack;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Mail;
using MongoDB.Bson.Serialization.Attributes;

namespace CoinCapParserService
{
    public class CoinCapParser
    {
        #region Private Members
        private string _filePath = @"c:\coin365Exception.txt";
        private string _intervalFilePath = @"c:\coin365IntervalException.txt";
        private string _webExceptionFilePath = @"c:\coin365WebException.txt";
        private string _timerCurrencyCount = @"c:\coin365TimerCount.txt";
        private static IHubContext hubContext = null;
        private static ExchangeViewModel exchangeViewModel = new ExchangeViewModel();
        private static List<GenericModelforCurrencies> ListForAverageResultsAllExchanges = new List<GenericModelforCurrencies>();
        private const int _threadSleepTime = 20000;
        #endregion

        #region Public Methods
        /// <summary>
        /// Start the signalR server.
        /// </summary>
        public void StartSignalRServer()
        {
            try
            {
                int IsServerEnv = Convert.ToInt32(ConfigurationManager.AppSettings["IsServer"]);
                string url = string.Empty;
                if (IsServerEnv == 1)
                {
                    url = "http://92.222.141.82:8089"; //Live                    
                    //url = "http://51.254.194.252:8089"; //Staging
                }
                else
                {
                    url = "http://192.168.1.167:5000";
                }

                WebApp.Start<Startup>(url);
                ApplicationCache.IsServerStarted = true;
                hubContext = GlobalHost.ConnectionManager.GetHubContext<MyHub>();
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "StartSignalRServer");
            }
        }

        /// <summary>
        /// Get and save physical currencies.
        /// </summary>
        public void GetAndSavePhysicalCurrencies()
        {
            try
            {
                string url = "http://openexchangerates.org/api/latest.json?app_id=58f7fbffa5874620a64dcb000826a3d5";
                string json = _GetResponseFromAPI(url);
                if (!string.IsNullOrEmpty(json))
                {
                    PhysicalCurrencyModel Model = JsonConvert.DeserializeObject<PhysicalCurrencyModel>(json);
                    List<PhysicalCurrencyModelMongo> lstp = new List<PhysicalCurrencyModelMongo>();
                    foreach (var item in Model.rates.GetType().GetProperties())
                    {
                        PhysicalCurrencyModelMongo objM = new PhysicalCurrencyModelMongo();
                        objM.currency = item.Name;
                        objM.rate = Convert.ToDouble(item.GetValue(Model.rates, null));
                        lstp.Add(objM);
                    }
                    this._SavePhysicalCurrenciesRates(lstp);
                }
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "GetAndSavePhysicalCurrencies");
            }
        }

        /// <summary>
        /// Save the currencies categories.
        /// </summary>
        public void SaveCurrenciesCategories()
        {
            try
            {
                string url = "https://www.investitin.com/altcoin-list";
                HtmlDocument document = new HtmlDocument();
                string html = _GetResponseFromAPI(url);
                document.LoadHtml(html);
                HtmlNode table = document.DocumentNode.SelectSingleNode("//*[@id='tablepress-27']");

                List<CurrenciesCategories> currenciesCategoriesList = new List<CurrenciesCategories>();
                for (int i = 1; i < 264; i++)
                {
                    try
                    {
                        string symbol = table.SelectSingleNode("tbody/tr[" + i + "]/td[2]").InnerHtml;
                        string category = table.SelectSingleNode("tbody/tr[" + i + "]/td[3]").InnerHtml;
                        CurrenciesCategories categories = new CurrenciesCategories()
                        {
                            symbol = symbol,
                            category = category
                        };
                        currenciesCategoriesList.Add(categories);
                    }
                    catch (Exception ex)
                    {
                        _LogErrorMessage(ex.Message, "SaveCurrenciesCategories/loop");
                    }
                }

                using (IRedisService redisService = new RedisService())
                {
                    redisService.Set<List<CurrenciesCategories>>(RedisKeys.CurrenciesCategories, currenciesCategoriesList);
                }
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "SaveCurrenciesCategories");
            }
        }

        /// <summary>
        /// Save coincap marketcap, total volumne and btc dominance.
        /// </summary>
        public void SaveMarketCapData()
        {
            try
            {
                string url = "https://api.coincap.io/v2/assets?limit=" + Constants.CurrenciesLimit;
                string json = _GetResponseFromAPI(url);
                if (!string.IsNullOrEmpty(json))
                {
                    CoinCapMarketCap coinCapMarketCap = JsonConvert.DeserializeObject<CoinCapMarketCap>(json);
                    double marketCapValue = coinCapMarketCap.data.Sum(x => Convert.ToDouble(x.marketCapUsd));
                    double volumne = coinCapMarketCap.data.Sum(x => Convert.ToDouble(x.volumeUsd24Hr));
                    double btcMarketCap = Convert.ToDouble(coinCapMarketCap.data.FirstOrDefault(x => x.id.Equals("bitcoin")).marketCapUsd);
                    double btcDominance = (btcMarketCap / marketCapValue) * 100;
                    int count = coinCapMarketCap.data.Count;
                    MarketCap marketCap = new MarketCap();
                    marketCap.market_capt = marketCapValue;
                    marketCap.volume_total = volumne;
                    marketCap.btc_dominance = btcDominance;

                    this._SaveMarketCapToRedisCache(marketCap);
                }
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "SaveMarketCapData");
            }
        }

        /// <summary>
        /// Save all the currencies in redis cache and broadcast to connected clients.
        /// </summary>
        public void SaveAllCurrenciesInCache()
        {
            int currenciesLimit = CommHelpers.GetCurrenciesLimit();
            List<CurrenciesCategories> currenciesCategories = this._GetCurrenciesCategoriesFromRedis();
            List<CurrenciesPercentages> currenciesChangeInOneWeek = this._GetCurrenciesChangeInPercentageFromRedis(RedisKeys.CurrenciesChangeOneWeek);
            List<CurrenciesPercentages> currenciesChangeInOneMonth = this._GetCurrenciesChangeInPercentageFromRedis(RedisKeys.CurrenciesChangeOneMonth);
            List<CurrenciesPercentages> currenciesChangeInThreeMonths = this._GetCurrenciesChangeInPercentageFromRedis(RedisKeys.CurrenciesChangeThreeMonths);
            List<CurrenciesPercentages> currenciesChangeInOneYear = this._GetCurrenciesChangeInPercentageFromRedis(RedisKeys.CurrenciesChangeOneYear);
            List<CurrenciesPercentages> currenciesChangeAll = this._GetCurrenciesChangeInPercentageFromRedis(RedisKeys.CurrenciesChangeAll);

            Assets assets;
            string url = "https://api.coincap.io/v2/assets?limit=" + currenciesLimit;
            string json = _GetResponseFromAPI(url);
            if (!string.IsNullOrEmpty(json))
            {
                assets = JsonConvert.DeserializeObject<Assets>(json);

                //Get the currency links.
                List<CryptoCurrencyLinks> currencyLinks = this._GetCryptoCurrencyLinks();
                string ServerURL = Convert.ToString(ConfigurationManager.AppSettings["ServerURL"]);

                List<AverageDataCurrencies> averageDataCurrenciesList = new List<AverageDataCurrencies>();
                string btcPrice = assets.data.FirstOrDefault(x => x.symbol.Equals("BTC")).priceUsd;

                int index = 1;
                foreach (AssetsData item in assets.data)
                {
                    AverageDataCurrencies averageDataCurrencies = new AverageDataCurrencies()
                    {
                        rank = Convert.ToInt32(item.rank),
                        price = Convert.ToDouble(item.priceUsd),
                        average_volume = Convert.ToDouble(item.vwap24Hr),
                        base_currency = item.symbol,
                        basecurrency_fullname = item.id,
                        volume = Convert.ToDouble(item.volumeUsd24Hr),
                        prcnt_change = Convert.ToDouble(item.changePercent24Hr),
                        circulating_supply = Convert.ToDouble(item.supply),
                        market_cap = Convert.ToDouble(item.marketCapUsd),
                        created_date = DateTime.UtcNow,
                        prcnt_change7d = this._GetCurrencyChangeInPercentage(item.symbol, currenciesChangeInOneWeek),
                        prcnt_change1m = this._GetCurrencyChangeInPercentage(item.symbol, currenciesChangeInOneMonth),
                        prcnt_change3m = this._GetCurrencyChangeInPercentage(item.symbol, currenciesChangeInThreeMonths),
                        prcnt_change1y = this._GetCurrencyChangeInPercentage(item.symbol, currenciesChangeInOneYear),
                        prcnt_changeytd = this._GetCurrencyChangeInPercentage(item.symbol, currenciesChangeAll),
                        prcnt_changeall = this._GetCurrencyChangeInPercentage(item.symbol, currenciesChangeAll),
                        rank_change7d = this._GetCurrencyChangeInRank(item.symbol, currenciesChangeInOneWeek) == 0 ? 0 : this._GetCurrencyChangeInRank(item.symbol, currenciesChangeInOneWeek) - Convert.ToInt32(item.rank),
                        rank_change1m = this._GetCurrencyChangeInRank(item.symbol, currenciesChangeInOneMonth) == 0 ? 0 : this._GetCurrencyChangeInRank(item.symbol, currenciesChangeInOneMonth) - Convert.ToInt32(item.rank),
                        rank_change3m = this._GetCurrencyChangeInRank(item.symbol, currenciesChangeInThreeMonths) == 0 ? 0 : this._GetCurrencyChangeInRank(item.symbol, currenciesChangeInThreeMonths) - Convert.ToInt32(item.rank),
                        rank_change1y = this._GetCurrencyChangeInRank(item.symbol, currenciesChangeInOneYear) == 0 ? 0 : this._GetCurrencyChangeInRank(item.symbol, currenciesChangeInOneYear) - Convert.ToInt32(item.rank),
                        rank_changeytd = this._GetCurrencyChangeInRank(item.symbol, currenciesChangeAll) == 0 ? 0 : this._GetCurrencyChangeInRank(item.symbol, currenciesChangeAll) - Convert.ToInt32(item.rank),
                        rank_changeall = this._GetCurrencyChangeInRank(item.symbol, currenciesChangeAll) == 0 ? 0 : this._GetCurrencyChangeInRank(item.symbol, currenciesChangeAll) - Convert.ToInt32(item.rank),
                        currency_logo = !string.IsNullOrEmpty(item.symbol) ? ServerURL + "/" + item.symbol.ToUpper() + ".png" : ServerURL + "/" + item.id + ".png",
                        reddit_link = currencyLinks.FirstOrDefault(x => x.currency_name.Equals(item.symbol))?.reggit_link,
                        facebook_link = currencyLinks.FirstOrDefault(x => x.currency_name.Equals(item.symbol))?.facebook_link,
                        github_link = currencyLinks.FirstOrDefault(x => x.currency_name.Equals(item.symbol))?.git_link,
                        website_link = currencyLinks.FirstOrDefault(x => x.currency_name.Equals(item.symbol))?.web_link,
                        twitter_link = currencyLinks.FirstOrDefault(x => x.currency_name.Equals(item.symbol))?.twitter_link,
                        btc_price = this._GetBTCPrice(item.priceUsd, btcPrice),
                        category = currenciesCategories != null ? currenciesCategories.FirstOrDefault(x => x.symbol.Equals(item.symbol))?.category : string.Empty,
                        coin_description = currencyLinks.FirstOrDefault(x => x.currency_name.Equals(item.symbol))?.description,
                        position_p = index
                    };
                    index++;
                    averageDataCurrenciesList.Add(averageDataCurrencies);
                }

                this._SaveCurrenciesToRedisCache(averageDataCurrenciesList);
            }
        }

        /// <summary>
        /// Save data from redis cache after every 30 minutes for charts.
        /// </summary>
        public void SaveChartsData()
        {
            try
            {
                List<AverageDataCurrencies> cachedCurrencies;
                using (IRedisService redisService = new RedisService())
                {
                    cachedCurrencies = redisService.Get<List<AverageDataCurrencies>>(RedisKeys.Currencies);
                }

                if (cachedCurrencies != null && cachedCurrencies.Count > 0)
                {
                    List<ChartsData> chartsDataList = new List<ChartsData>();
                    foreach (var item in cachedCurrencies)
                    {
                        ChartsData chartsData = new ChartsData()
                        {
                            btc_price = item.btc_price,
                            datetime = item.created_date.ToString(Constants.DateFormat),
                            market_cap = item.market_cap,
                            price = item.price,
                            volume = item.volume,
                            base_currency = item.base_currency,
                            created_date = DateTime.UtcNow
                        };
                        chartsDataList.Add(chartsData);
                    }

                    if (chartsDataList.Count > 0)
                    {
                        MongoRepository mongoRepository = new MongoRepository();
                        mongoRepository.InsertMany<ChartsData>(chartsDataList, "ChartsData");
                    }

                    this._SaveAverageDatatoMongoDB(cachedCurrencies);
                }
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "SaveChartsData");
            }
        }

        /// <summary>
        /// Change for one week.
        /// </summary>
        public void ParseOneWeekData()
        {
            var stopWatch = Stopwatch.StartNew();
            try
            {
                List<AverageDataCurrencies> cachedCurrencies;
                using (IRedisService redisService = new RedisService())
                {
                    cachedCurrencies = redisService.Get<List<AverageDataCurrencies>>(RedisKeys.Currencies);
                }

                if (cachedCurrencies != null && cachedCurrencies.Count > 0)
                {
                    List<CurrenciesPercentages> currenciesPercentages = new List<CurrenciesPercentages>();
                    foreach (AverageDataCurrencies item in cachedCurrencies)
                    {
                        CurrenciesIntervalData intervalData;
                        string url = "https://api.coincap.io/v2/assets/" + item.basecurrency_fullname + "/history?interval=" + Constants.OneWeek;
                        Thread.Sleep(_threadSleepTime);
                        string json = _GetResponseFromAPI(url);
                        if (!string.IsNullOrEmpty(json))
                        {
                            intervalData = JsonConvert.DeserializeObject<CurrenciesIntervalData>(json);
                            try
                            {
                                if (intervalData.data.Count > 0)
                                {
                                    double latestPrice = Convert.ToDouble(intervalData.data.LastOrDefault().priceUsd);
                                    double previousPrice = Convert.ToDouble(intervalData.data.FirstOrDefault().priceUsd);
                                    double latestMarketCap = latestPrice * Convert.ToDouble(intervalData.data.LastOrDefault().circulatingSupply);
                                    double previousMarketCap = previousPrice * Convert.ToDouble(intervalData.data.FirstOrDefault().circulatingSupply);

                                    double percantageChange = ((latestPrice - previousPrice) / previousPrice) * 100;
                                    double marketCapChange = previousMarketCap;
                                    CurrenciesPercentages currenciesPercentage = new CurrenciesPercentages()
                                    {
                                        Currency = item.base_currency,
                                        PercentageChange = percantageChange,
                                        MarketCapChange = marketCapChange
                                    };
                                    currenciesPercentages.Add(currenciesPercentage);
                                }
                            }
                            catch (Exception ex)
                            {
                                _LogIntervalDataException(item.rank.ToString() + "" + ex.Message + " Url is: " + url, "ParseOneWeekData/For");
                            }
                        }
                    }
                    currenciesPercentages = currenciesPercentages.OrderByDescending(x => x.MarketCapChange).ToList();
                    using (IRedisService redisService = new RedisService())
                    {
                        redisService.Set<List<CurrenciesPercentages>>(RedisKeys.CurrenciesChangeOneWeek, currenciesPercentages);
                    }
                    _LogTimerCount(currenciesPercentages.Count, "ParseOneWeekData");
                }
            }
            catch (Exception ex)
            {
                _LogIntervalDataException(ex.Message, "ParseOneWeekData");
            }
            stopWatch.Stop();
            _LogTimerCount(stopWatch.ElapsedMilliseconds, "ParseOneWeekData");
        }

        /// <summary>
        /// Change for one month.
        /// </summary>
        public void ParseOneMonthData()
        {
            var stopWatch = Stopwatch.StartNew();
            try
            {
                List<AverageDataCurrencies> cachedCurrencies;
                using (IRedisService redisService = new RedisService())
                {
                    cachedCurrencies = redisService.Get<List<AverageDataCurrencies>>(RedisKeys.Currencies);
                }

                if (cachedCurrencies != null && cachedCurrencies.Count > 0)
                {
                    List<CurrenciesPercentages> currenciesPercentages = new List<CurrenciesPercentages>();
                    foreach (AverageDataCurrencies item in cachedCurrencies)
                    {
                        string url = "https://api.coincap.io/v2/assets/" + item.basecurrency_fullname + "/history?interval=" + Constants.OneMonth;
                        CurrenciesIntervalData intervalData;
                        Thread.Sleep(_threadSleepTime);
                        string json = _GetResponseFromAPI(url);
                        if (!string.IsNullOrEmpty(json))
                        {
                            intervalData = JsonConvert.DeserializeObject<CurrenciesIntervalData>(json);
                            try
                            {
                                if (intervalData.data.Count > 0)
                                {
                                    double latestPrice = Convert.ToDouble(intervalData.data.LastOrDefault().priceUsd);
                                    double previousPrice = Convert.ToDouble(intervalData.data.FirstOrDefault().priceUsd);
                                    double latestMarketCap = latestPrice * Convert.ToDouble(intervalData.data.LastOrDefault().circulatingSupply);
                                    double previousMarketCap = previousPrice * Convert.ToDouble(intervalData.data.FirstOrDefault().circulatingSupply);

                                    double percantageChange = ((latestPrice - previousPrice) / previousPrice) * 100;
                                    double marketCapChange = previousMarketCap;
                                    CurrenciesPercentages currenciesPercentage = new CurrenciesPercentages()
                                    {
                                        Currency = item.base_currency,
                                        PercentageChange = percantageChange,
                                        MarketCapChange = marketCapChange
                                    };
                                    currenciesPercentages.Add(currenciesPercentage);
                                }
                            }
                            catch (Exception ex)
                            {
                                _LogIntervalDataException(item.rank.ToString() + "" + ex.Message + " Url is: " + url, "ParseOneWeekData/For");
                            }
                        }
                    }
                    currenciesPercentages = currenciesPercentages.OrderByDescending(x => x.MarketCapChange).ToList();
                    using (IRedisService redisService = new RedisService())
                    {
                        redisService.Set<List<CurrenciesPercentages>>(RedisKeys.CurrenciesChangeOneMonth, currenciesPercentages);
                    }
                    _LogTimerCount(currenciesPercentages.Count, "ParseOneMonthData");
                }
            }
            catch (Exception ex)
            {
                _LogIntervalDataException(ex.Message, "ParseOneMonthData");
            }
            stopWatch.Stop();
            _LogTimerCount(stopWatch.ElapsedMilliseconds, "ParseOneMonthData");
        }

        /// <summary>
        /// Change for three month.
        /// </summary>
        public void ParseThreeMonthData()
        {
            var stopWatch = Stopwatch.StartNew();
            try
            {
                List<AverageDataCurrencies> cachedCurrencies;
                using (IRedisService redisService = new RedisService())
                {
                    cachedCurrencies = redisService.Get<List<AverageDataCurrencies>>(RedisKeys.Currencies);
                }

                if (cachedCurrencies != null && cachedCurrencies.Count > 0)
                {
                    List<CurrenciesPercentages> currenciesPercentages = new List<CurrenciesPercentages>();
                    foreach (AverageDataCurrencies item in cachedCurrencies)
                    {
                        string url = "https://api.coincap.io/v2/assets/" + item.basecurrency_fullname + "/history?interval=" + Constants.ThreeMonth;
                        CurrenciesIntervalData intervalData;
                        Thread.Sleep(_threadSleepTime);
                        string json = _GetResponseFromAPI(url);
                        if (!string.IsNullOrEmpty(json))
                        {
                            intervalData = JsonConvert.DeserializeObject<CurrenciesIntervalData>(json);
                            intervalData.data = intervalData.data.Where(x => x.date >= DateTime.UtcNow.AddMonths(-3)).ToList();
                            try
                            {
                                if (intervalData.data.Count > 0)
                                {
                                    double latestPrice = Convert.ToDouble(intervalData.data.LastOrDefault().priceUsd);
                                    double previousPrice = Convert.ToDouble(intervalData.data.FirstOrDefault().priceUsd);
                                    double latestMarketCap = latestPrice * Convert.ToDouble(intervalData.data.LastOrDefault().circulatingSupply);
                                    double previousMarketCap = previousPrice * Convert.ToDouble(intervalData.data.FirstOrDefault().circulatingSupply);

                                    double percantageChange = ((latestPrice - previousPrice) / previousPrice) * 100;
                                    double marketCapChange = previousMarketCap;
                                    CurrenciesPercentages currenciesPercentage = new CurrenciesPercentages()
                                    {
                                        Currency = item.base_currency,
                                        PercentageChange = percantageChange,
                                        MarketCapChange = marketCapChange
                                    };
                                    currenciesPercentages.Add(currenciesPercentage);
                                }
                            }
                            catch (Exception ex)
                            {
                                _LogIntervalDataException(item.rank.ToString() + "" + ex.Message + " Url is: " + url, "ParseOneWeekData/For");
                            }
                        }
                    }
                    currenciesPercentages = currenciesPercentages.OrderByDescending(x => x.MarketCapChange).ToList();
                    using (IRedisService redisService = new RedisService())
                    {
                        redisService.Set<List<CurrenciesPercentages>>(RedisKeys.CurrenciesChangeThreeMonths, currenciesPercentages);
                    }
                    _LogTimerCount(currenciesPercentages.Count, "ParseThreeMonthData");
                }
            }
            catch (Exception ex)
            {
                _LogIntervalDataException(ex.Message, "ParseThreeMonthData");
            }
            stopWatch.Stop();
            _LogTimerCount(stopWatch.ElapsedMilliseconds, "ParseThreeMonthData");
        }

        /// <summary>
        /// Change for six month.
        /// </summary>
        public void ParseSixMonthData()
        {
            try
            {
                List<AverageDataCurrencies> cachedCurrencies;
                using (IRedisService redisService = new RedisService())
                {
                    cachedCurrencies = redisService.Get<List<AverageDataCurrencies>>(RedisKeys.Currencies);
                }

                if (cachedCurrencies != null && cachedCurrencies.Count > 0)
                {
                    List<CurrenciesPercentages> currenciesPercentages = new List<CurrenciesPercentages>();
                    foreach (AverageDataCurrencies item in cachedCurrencies)
                    {
                        string url = "https://api.coincap.io/v2/assets/" + item.basecurrency_fullname + "/history?interval=" + Constants.SixMonth;
                        CurrenciesIntervalData intervalData;
                        Thread.Sleep(_threadSleepTime);
                        string json = _GetResponseFromAPI(url);
                        if (!string.IsNullOrEmpty(json))
                        {
                            intervalData = JsonConvert.DeserializeObject<CurrenciesIntervalData>(json);
                            intervalData.data = intervalData.data.Where(x => !string.IsNullOrEmpty(x.circulatingSupply)).ToList();
                            try
                            {
                                if (intervalData.data.Count > 0)
                                {
                                    double latestPrice = Convert.ToDouble(intervalData.data.LastOrDefault().priceUsd);
                                    double previousPrice = Convert.ToDouble(intervalData.data.FirstOrDefault().priceUsd);
                                    double latestMarketCap = latestPrice * Convert.ToDouble(intervalData.data.LastOrDefault().circulatingSupply);
                                    double previousMarketCap = previousPrice * Convert.ToDouble(intervalData.data.FirstOrDefault().circulatingSupply);

                                    double percantageChange = ((latestPrice - previousPrice) / previousPrice) * 100;
                                    double marketCapChange = previousMarketCap;
                                    CurrenciesPercentages currenciesPercentage = new CurrenciesPercentages()
                                    {
                                        Currency = item.base_currency,
                                        PercentageChange = percantageChange,
                                        MarketCapChange = marketCapChange
                                    };
                                    currenciesPercentages.Add(currenciesPercentage);
                                }
                            }
                            catch (Exception ex)
                            {
                                _LogIntervalDataException(item.rank.ToString() + "" + ex.Message + " Url is: " + url, "ParseOneWeekData/For");
                            }
                        }
                    }
                    currenciesPercentages = currenciesPercentages.OrderByDescending(x => x.MarketCapChange).ToList();
                    using (IRedisService redisService = new RedisService())
                    {
                        redisService.Set<List<CurrenciesPercentages>>(RedisKeys.CurrenciesChangeSixMonths, currenciesPercentages);
                    }
                    _LogTimerCount(currenciesPercentages.Count, "ParseSixMonthData");
                }
            }
            catch (Exception ex)
            {
                _LogIntervalDataException(ex.Message, "ParseSixMonthData");
            }
        }

        /// <summary>
        /// Change for one year.
        /// </summary>
        public void ParseOneYearData()
        {
            var stopWatch = Stopwatch.StartNew();
            try
            {
                List<AverageDataCurrencies> cachedCurrencies;
                using (IRedisService redisService = new RedisService())
                {
                    cachedCurrencies = redisService.Get<List<AverageDataCurrencies>>(RedisKeys.Currencies);
                }

                if (cachedCurrencies != null && cachedCurrencies.Count > 0)
                {
                    List<CurrenciesPercentages> currenciesPercentages = new List<CurrenciesPercentages>();
                    foreach (AverageDataCurrencies item in cachedCurrencies)
                    {
                        string url = "https://api.coincap.io/v2/assets/" + item.basecurrency_fullname + "/history?interval=" + Constants.OneYear;
                        CurrenciesIntervalData intervalData;
                        Thread.Sleep(_threadSleepTime);
                        string json = _GetResponseFromAPI(url);
                        if (!string.IsNullOrEmpty(json))
                        {
                            if (!string.IsNullOrEmpty(json))
                            {
                                intervalData = JsonConvert.DeserializeObject<CurrenciesIntervalData>(json);
                                intervalData.data = intervalData.data.Where(x => !string.IsNullOrEmpty(x.circulatingSupply)).ToList();
                                try
                                {
                                    if (intervalData.data.Count > 0)
                                    {
                                        double latestPrice = Convert.ToDouble(intervalData.data.LastOrDefault().priceUsd);
                                        double previousPrice = Convert.ToDouble(intervalData.data.FirstOrDefault().priceUsd);
                                        double latestMarketCap = latestPrice * Convert.ToDouble(intervalData.data.LastOrDefault().circulatingSupply);
                                        double previousMarketCap = previousPrice * Convert.ToDouble(intervalData.data.FirstOrDefault().circulatingSupply);

                                        double percantageChange = ((latestPrice - previousPrice) / previousPrice) * 100;
                                        double marketCapChange = previousMarketCap;
                                        CurrenciesPercentages currenciesPercentage = new CurrenciesPercentages()
                                        {
                                            Currency = item.base_currency,
                                            PercentageChange = percantageChange,
                                            MarketCapChange = marketCapChange
                                        };
                                        currenciesPercentages.Add(currenciesPercentage);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _LogIntervalDataException(item.rank.ToString() + "" + ex.Message + " Url is: " + url, "ParseOneYearData/For");
                                }
                            }
                        }
                    }

                    currenciesPercentages = currenciesPercentages.OrderByDescending(x => x.MarketCapChange).ToList();
                    using (IRedisService redisService = new RedisService())
                    {
                        redisService.Set<List<CurrenciesPercentages>>(RedisKeys.CurrenciesChangeOneYear, currenciesPercentages);
                    }
                    _LogTimerCount(currenciesPercentages.Count, "ParseOneYearData");
                }
            }
            catch (Exception ex)
            {
                _LogIntervalDataException(ex.Message, "ParseOneYearData");
            }
            stopWatch.Stop();
            _LogTimerCount(stopWatch.ElapsedMilliseconds, "ParseOneYearData");
        }

        /// <summary>
        /// Change for all.
        /// </summary>
        public void ParseAllData()
        {
            var stopWatch = Stopwatch.StartNew();
            try
            {
                List<AverageDataCurrencies> cachedCurrencies;
                using (IRedisService redisService = new RedisService())
                {
                    cachedCurrencies = redisService.Get<List<AverageDataCurrencies>>(RedisKeys.Currencies);
                }

                if (cachedCurrencies != null && cachedCurrencies.Count > 0)
                {
                    List<CurrenciesPercentages> currenciesPercentages = new List<CurrenciesPercentages>();
                    foreach (AverageDataCurrencies item in cachedCurrencies)
                    {
                        string url = "https://api.coincap.io/v2/assets/" + item.basecurrency_fullname + "/history?interval=" + Constants.YTD;
                        CurrenciesIntervalData intervalData;
                        Thread.Sleep(_threadSleepTime);
                        string json = _GetResponseFromAPI(url);
                        if (!string.IsNullOrEmpty(json))
                        {
                            intervalData = JsonConvert.DeserializeObject<CurrenciesIntervalData>(json);
                            intervalData.data = intervalData.data.Where(x => !string.IsNullOrEmpty(x.circulatingSupply) && x.date >= DateTime.UtcNow.AddMonths(-DateTime.UtcNow.Month)).ToList();
                            try
                            {
                                if (intervalData.data.Count > 0)
                                {
                                    double latestPrice = Convert.ToDouble(intervalData.data.LastOrDefault().priceUsd);
                                    double previousPrice = Convert.ToDouble(intervalData.data.FirstOrDefault().priceUsd);
                                    double latestMarketCap = latestPrice * Convert.ToDouble(intervalData.data.LastOrDefault().circulatingSupply);
                                    double previousMarketCap = previousPrice * Convert.ToDouble(intervalData.data.FirstOrDefault().circulatingSupply);

                                    double percantageChange = ((latestPrice - previousPrice) / previousPrice) * 100;
                                    double marketCapChange = previousMarketCap;
                                    CurrenciesPercentages currenciesPercentage = new CurrenciesPercentages()
                                    {
                                        Currency = item.base_currency,
                                        PercentageChange = percantageChange,
                                        MarketCapChange = marketCapChange
                                    };
                                    currenciesPercentages.Add(currenciesPercentage);
                                }
                            }
                            catch (Exception ex)
                            {
                                _LogIntervalDataException(item.rank.ToString() + "" + ex.Message + " Url is: " + url, "ParseAllData/For");
                            }
                        }
                    }
                    currenciesPercentages = currenciesPercentages.OrderByDescending(x => x.MarketCapChange).ToList();
                    using (IRedisService redisService = new RedisService())
                    {
                        redisService.Set<List<CurrenciesPercentages>>(RedisKeys.CurrenciesChangeAll, currenciesPercentages);
                    }
                    _LogTimerCount(currenciesPercentages.Count, "ParseAllData");
                }
            }
            catch (Exception ex)
            {
                _LogIntervalDataException(ex.Message, "ParseAllData");
            }
            stopWatch.Stop();
            _LogTimerCount(stopWatch.ElapsedMilliseconds, "ParseAllData");
        }

        /// <summary>
        /// Get all the exchanges data and stored into database.
        /// </summary>
        public void GetAndSaveExchangesData()
        {
            string urlExchanges = "https://api.coincap.io/v2/exchanges";
            string responseExchanges = _GetResponseFromAPI(urlExchanges);
            if (!string.IsNullOrEmpty(responseExchanges))
            {
                List<Exchanges> allexchanges = new List<Exchanges>();
                Exchanges exchanges = JsonConvert.DeserializeObject<Exchanges>(responseExchanges);
                List<ExchangesDatum> exchangesData = new List<ExchangesDatum>();
#if !DEBUG
                exchangesData = exchanges.data.Skip(0).Take(20).ToList();
#else
                exchangesData = exchanges.data;
#endif
                exchangeViewModel = this._GetExchangeSettingsByExchangeName();
                int IsEnable = Convert.ToInt32(ConfigurationManager.AppSettings["IsDumpExchangeSettingAndInfoSQLEnabled"]);
                if (IsEnable == 1)
                {
                    foreach (ExchangesDatum item in exchangesData)
                    {
                        this._SaveExchanges(item.name, item.exchangeUrl);
                        this._DumpAllExchangeDataAllExchanges(item.exchangeId, item.tradingPairs, item.exchangeUrl);
                    }
                }
                else
                {
                    foreach (ExchangesDatum item in exchangesData)
                    {
                        this._DumpAllExchangeDataAllExchanges(item.exchangeId, item.tradingPairs, item.exchangeUrl);
                    }
                }

                #region Insert into Exchanges
                MongoRepository mongoRepository = new MongoRepository();
                mongoRepository.DropCollection("ExchangeSettingsModel");
                foreach (var item in exchangeViewModel.exchangeSettings)
                {
                    ExchangesInfoModel exchangesInfoModel = new ExchangesInfoModel()
                    {
                        exchange_name = item.ExchangeName,
                        exchange_id = item.ID,
                        exchange_url = item.PairURL
                    };
                    mongoRepository.Insert<ExchangesInfoModel>(exchangesInfoModel, "ExchangeSettingsModel");
                }
                #endregion
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Get the exchange data and dump into mongo database.
        /// </summary>
        /// <param name="exchangeId"></param>
        /// <param name="tradingPairs"></param>
        /// <param name="exchangeUrl"></param>
        public void _DumpAllExchangeDataAllExchanges(string exchangeId, string tradingPairs, string exchangeUrl)
        {
            List<GenericModelforCurrencies> exchangesListAllCurrencies = new List<GenericModelforCurrencies>();
            string url = "https://api.coincap.io/v2/markets?exchangeId=" + exchangeId.Trim() + "&limit=2000";
            string response = _GetResponseFromAPI(url);
            Binance exchange = JsonConvert.DeserializeObject<Binance>(response);
            if (!string.IsNullOrEmpty(response))
            {
                foreach (BinnanceData item in exchange.data)
                {
                    try
                    {
                        int exchangeSettingID =
                            exchangeViewModel.exchangeSettings.FirstOrDefault(x => x.ExchangeName.Equals(exchangeId, StringComparison.OrdinalIgnoreCase)) == null
                            ? _SaveAndGetExchangeId(exchangeId, exchangeUrl)
                            : exchangeViewModel.exchangeSettings.FirstOrDefault(x => x.ExchangeName.Equals(exchangeId, StringComparison.OrdinalIgnoreCase)).ID;
                        int exchangeInfoID =
                            exchangeViewModel.exchangesInfos.FirstOrDefault(x => x.ExchangeName.Equals(exchangeId, StringComparison.OrdinalIgnoreCase)) == null
                            ? _SaveAndGetExchangeInfoId(exchangeSettingID, exchangeId)
                            : exchangeViewModel.exchangesInfos.FirstOrDefault(x => x.ExchangeName.Equals(exchangeId, StringComparison.OrdinalIgnoreCase)).ID;
                        GenericModelforCurrencies markets = new GenericModelforCurrencies
                        {
                            exchangeid = exchangeSettingID,
                            exchangeinfoid = exchangeInfoID,
                            basecurrency_fullname = string.IsNullOrEmpty(item.baseId) ? string.Empty : _FirstCharToUpper(item.baseId),
                            rank = string.IsNullOrEmpty(item.rank) ? 0 : Convert.ToInt32(item.rank),
                            symbol = item.baseSymbol,
                            converted_symbol = item.quoteSymbol,
                            created_date = DateTime.UtcNow,
                            timestamp = DateTime.Now.TimeOfDay,
                            logo_url = string.Empty,
                            price = string.IsNullOrEmpty(item.priceUsd) ? 0 : Convert.ToDouble(item.priceUsd),
                            currency_pair = (string.IsNullOrEmpty(tradingPairs)) || (tradingPairs == "0") ? string.Empty : string.Concat(item.baseSymbol, item.quoteSymbol)
                        };
                        markets.timestamp = DateTime.UtcNow.TimeOfDay;
                        markets.volume = string.IsNullOrEmpty(item.volumeUsd24Hr) ? 0 : Convert.ToDouble(item.volumeUsd24Hr);
                        if (exchangesListAllCurrencies.FirstOrDefault(x => x.symbol.Equals(item.baseSymbol)) == null)
                        {
                            exchangesListAllCurrencies.Add(markets);
                        }
                    }
                    catch (Exception ex)
                    {
                        _LogErrorMessage(ex.Message, "_DumpAllExchangeDataAllExchanges");
                    }
                }
            }

            if (exchangesListAllCurrencies.Count > 0)
            {
                this._SaveExchangesDatatoMongoDB(exchangesListAllCurrencies);
            }
        }

        /// <summary>
        /// Save and get exchange info by Id.
        /// </summary>
        /// <param name="exchangeSettingID"></param>
        /// <param name="exchangeId"></param>
        /// <returns></returns>
        private int _SaveAndGetExchangeInfoId(int exchangeSettingID, string exchangeId)
        {
            string ConString = ConfigurationManager.ConnectionStrings["ApplicationServices"].ConnectionString;
            ExchangesInfo exchangesInfo = new ExchangesInfo();
            using (SqlConnection connection = new SqlConnection(ConString))
            {
                using (SqlCommand command = new SqlCommand("SetAndGetExchangeInfoId", connection))
                {
                    command.Parameters.AddWithValue("@ExchangeID", exchangeSettingID);
                    command.Parameters.AddWithValue("@ExchangeName", exchangeId);
                    command.CommandType = CommandType.StoredProcedure;
                    connection.Open();
                    SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        exchangesInfo.ID = Convert.ToInt32(reader["ExchangeInfoId"]);
                    }

                    connection.Close();
                    reader.Close();
                }
            }
            exchangeViewModel = _GetExchangeSettingsByExchangeName();
            return exchangesInfo.ID;
        }

        /// <summary>
        /// Save and get the exchange by Id.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="exchangeUrl"></param>
        /// <returns></returns>
        private int _SaveAndGetExchangeId(string name, string exchangeUrl)
        {
            string ConString = ConfigurationManager.ConnectionStrings["ApplicationServices"].ConnectionString;
            ExchangeSettings exchangeSettings = new ExchangeSettings();
            using (SqlConnection connection = new SqlConnection(ConString))
            {
                using (SqlCommand command = new SqlCommand("SetAndGetExchangeId", connection))
                {
                    command.Parameters.AddWithValue("@ExchangeName", name);
                    command.Parameters.AddWithValue("@PairURL", exchangeUrl);
                    command.CommandType = CommandType.StoredProcedure;
                    connection.Open();
                    SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        exchangeSettings.ID = Convert.ToInt32(reader["ExchangeId"]);
                    }

                    connection.Close();
                    reader.Close();
                }

            }
            exchangeViewModel = _GetExchangeSettingsByExchangeName();
            return exchangeSettings.ID;
        }

        /// <summary>
        /// Save exchanges data in mongo database.
        /// </summary>
        /// <param name="lstData"></param>
        private void _SaveExchangesDatatoMongoDB(List<GenericModelforCurrencies> lstData)
        {
            try
            {
                MongoRepository mongoRepository = new MongoRepository();
                mongoRepository.InsertMany<GenericModelforCurrencies>(lstData, "Exchanges");
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "_SaveExchangesDatatoMongoDB");
            }
        }

        /// <summary>
        /// Save exchanges.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="exchangeUrl"></param>
        private void _SaveExchanges(string name, string exchangeUrl)
        {
            string ConString = ConfigurationManager.ConnectionStrings["ApplicationServices"].ConnectionString;
            using (SqlConnection connection = new SqlConnection(ConString))
            {
                using (SqlCommand command = new SqlCommand("SetExchangeSettings", connection))
                {
                    command.Parameters.AddWithValue("@ExchangeName", name);
                    command.Parameters.AddWithValue("@PairURL", exchangeUrl);
                    command.CommandType = CommandType.StoredProcedure;
                    connection.Open();
                    command.ExecuteNonQuery();
                    connection.Close();
                }
            }
        }

        /// <summary>
        /// Save all the currencies into database.
        /// </summary>
        /// <param name="lstData"></param>
        private void _SaveAverageDatatoMongoDB(List<AverageDataCurrencies> lstData)
        {
            try
            {
                MongoRepository mongoRepository = new MongoRepository();
                mongoRepository.InsertMany<AverageDataCurrencies>(lstData, "AverageDataCurrencies");
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "_SaveAverageDatatoMongoDB");
            }
        }

        /// <summary>
        /// Getting the exchange settings.
        /// </summary>
        /// <returns></returns>
        private ExchangeViewModel _GetExchangeSettingsByExchangeName()
        {
            string ConString = ConfigurationManager.ConnectionStrings["ApplicationServices"].ConnectionString;
            ExchangeViewModel record = new ExchangeViewModel();
            using (SqlConnection connection = new SqlConnection(ConString))
            {
                using (SqlCommand command = new SqlCommand("GetAllExchangeSettingsAndExchangesInfo", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    connection.Open();
                    SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        ExchangeSettings exchangeSettings = new ExchangeSettings
                        {
                            ID = Convert.ToInt32(reader["ID"]),
                            ExchangeName = reader["ExchangeName"].ToString(),
                            PairURL = reader["PairURL"].ToString(),
                            TickerURL = reader["TickerURL"].ToString(),
                            IsSynced = Convert.ToBoolean(reader["IsSynced"]),
                            IsSingleAPI = Convert.ToBoolean(reader["IsSingleAPI"]),
                            IsStatic = Convert.ToBoolean(reader["IsStatic"])
                        };
                        record.exchangeSettings.Add(exchangeSettings);
                    }
                    reader.NextResult();
                    while (reader.Read())
                    {
                        ExchangesInfo exchangesInfo = new ExchangesInfo
                        {
                            ID = Convert.ToInt32(reader["ID"]),
                            ExchangeID = Convert.ToInt32(reader["ExchangeID"]),
                            ExchangeName = Convert.ToString(reader["ExchangeName"]),
                            IsActive = Convert.ToBoolean(reader["IsActive"]),
                            IsDeleted = Convert.ToBoolean(reader["IsDeleted"]),
                            API_Pair = reader["API_Pair"].ToString(),
                            CreatedDate = Convert.ToDateTime(reader["CreatedDate"]) == null ? DateTime.UtcNow : Convert.ToDateTime(reader["CreatedDate"])
                        };
                        record.exchangesInfos.Add(exchangesInfo);
                    }
                    connection.Close();
                    reader.Close();
                }
            }
            return record;
        }

        /// <summary>
        /// Convert to first letter to upper case.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private string _FirstCharToUpper(string value)
        {
            string convertedString = string.Empty;
            try
            {
                convertedString = value.First().ToString().ToUpper() + value.Substring(1);
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "_FirstCharToUpper");
            }
            return convertedString;
        }

        /// <summary>
        /// Get the currencies categories from redis cache.
        /// </summary>
        /// <returns></returns>
        private List<CurrenciesCategories> _GetCurrenciesCategoriesFromRedis()
        {
            List<CurrenciesCategories> currenciesCategories = null;
            using (IRedisService redisService = new RedisService())
            {
                currenciesCategories = redisService.Get<List<CurrenciesCategories>>(RedisKeys.CurrenciesCategories);
            }
            return currenciesCategories;
        }

        /// <summary>
        /// Get the change in percentage from redis based on period.
        /// </summary>
        /// <param name="redisKey"></param>
        /// <returns></returns>
        private List<CurrenciesPercentages> _GetCurrenciesChangeInPercentageFromRedis(string redisKey)
        {
            List<CurrenciesPercentages> currenciesPercentages;
            using (IRedisService redisService = new RedisService())
            {
                currenciesPercentages = redisService.Get<List<CurrenciesPercentages>>(redisKey);
            }
            return currenciesPercentages;
        }

        /// <summary>
        /// Get the change in rank of the currency.
        /// </summary>
        private int _GetCurrencyChangeInRank(string symbol, List<CurrenciesPercentages> currenciesPercentages)
        {
            int rank = 0;
            if (currenciesPercentages != null && currenciesPercentages.Count > 0)
            {
                rank = (currenciesPercentages.FindIndex(x => x.Currency.ToUpper().Equals(symbol.ToUpper())) + 1);
            }
            return rank;
        }

        /// <summary>
        /// Get the change in percentage of the currency.
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="currenciesPercentages"></param>
        /// <returns></returns>
        private double _GetCurrencyChangeInPercentage(string symbol, List<CurrenciesPercentages> currenciesPercentages)
        {
            double changeInPercentage = 0;
            if (currenciesPercentages != null && currenciesPercentages.Count > 0)
            {
                if (currenciesPercentages.Exists(x => x.Currency.Equals(symbol)))
                {
                    changeInPercentage = currenciesPercentages.FirstOrDefault(x => x.Currency.Equals(symbol)).PercentageChange;
                }
            }
            return changeInPercentage;
        }

        /// <summary>
        /// Save all the currencies in redis cache.
        /// </summary>
        /// <param name="averageDataCurrencies"></param>
        private void _SaveCurrenciesToRedisCache(List<AverageDataCurrencies> averageDataCurrencies)
        {
            try
            {
                List<AverageDataCurrencies> cachedCurrencies;
                using (IRedisService redisService = new RedisService())
                {
                    cachedCurrencies = redisService.Get<List<AverageDataCurrencies>>(RedisKeys.Currencies);
                }

                if (cachedCurrencies != null && cachedCurrencies.Count > 0)
                {
                    foreach (AverageDataCurrencies item in averageDataCurrencies)
                    {
                        AverageDataCurrencies cachedValue = cachedCurrencies.FirstOrDefault(x => string.Equals(x.base_currency, item.base_currency, StringComparison.OrdinalIgnoreCase));
                        if (cachedValue != null)
                        {
                            item.rank = item.position_p - cachedValue.position_p;
                            if (item.price == cachedValue.price)
                            {
                                item.isupdated = false;
                            }
                            else
                            {
                                item.isupdated = true;
                            }
                        }
                    }
                }

                //order by marketcap and changing position
                averageDataCurrencies = averageDataCurrencies.OrderByDescending(x => x.market_cap).ToList();
                int pst = 1;
                foreach (var item in averageDataCurrencies)
                {
                    item.position = pst;
                    pst++;
                }

                using (IRedisService redisService = new RedisService())
                {
                    redisService.Set<List<AverageDataCurrencies>>(RedisKeys.Currencies, averageDataCurrencies);
                }

                //Now broadcasting all the updated data to connected clients.
                averageDataCurrencies = averageDataCurrencies.Where(x => x.isupdated).ToList();
                if (ApplicationCache.IsServerStarted)
                {
                    this._BroadcastCurrenciesDataToConnectedClients(averageDataCurrencies);
                }
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "_SaveCurrenciesToRedisCache");
            }
        }

        /// <summary>
        /// Save market cap to redis cache.
        /// </summary>
        /// <param name="marketCap"></param>
        private void _SaveMarketCapToRedisCache(MarketCap marketCap)
        {
            try
            {
                using (IRedisService redisService = new RedisService())
                {
                    MarketCap cachedMarketCap = redisService.Get<MarketCap>(RedisKeys.MarketCap);
                    if (cachedMarketCap != null)
                    {
                        if (!marketCap.Equals(cachedMarketCap))
                        {
                            redisService.Set<MarketCap>(RedisKeys.MarketCap, marketCap);
                        }
                    }
                    else
                    {
                        redisService.Set<MarketCap>(RedisKeys.MarketCap, marketCap);
                    }
                }

                if (ApplicationCache.IsServerStarted)
                {
                    this._BroadcastMarketCapDataToConnectedClients(marketCap);
                }
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.Message, "_SaveMarketCapToRedisCache");
            }
        }

        /// <summary>
        /// Get all the currencies links.
        /// </summary>
        /// <returns></returns>
        private List<CryptoCurrencyLinks> _GetCryptoCurrencyLinks()
        {
            MongoRepository _mongoRepository = new MongoRepository();
            IMongoCollection<CryptoCurrencyLinks> Collection = _mongoRepository.GetCollection<CryptoCurrencyLinks>();
            List<CryptoCurrencyLinks> documents = Collection.Find(x => true).ToList();
            return documents;
        }

        /// <summary>
        /// Save physical currencies.
        /// </summary>
        /// <param name="ObjData"></param>
        private void _SavePhysicalCurrenciesRates(List<PhysicalCurrencyModelMongo> ObjData)
        {
            try
            {
                MongoRepository mongoRepository = new MongoRepository();
                mongoRepository.InsertMany<PhysicalCurrencyModelMongo>(ObjData, "PhysicalCurrencyModel");
            }
            catch (Exception ex)
            {
                _LogErrorMessage(ex.InnerException.Message, "_SavePhysicalCurrenciesRates");
            }
        }

        /// <summary>
        /// Broadcast all the currencies to the connected clients.
        /// </summary>
        /// <param name="lstCurrencies"></param>
        private void _BroadcastCurrenciesDataToConnectedClients(List<AverageDataCurrencies> lstCurrencies)
        {
            try
            {
                lstCurrencies = lstCurrencies.Where(x => x.isupdated == true).ToList();
                hubContext.Clients.All.SendCurrencyObject(lstCurrencies);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Broadcast the market cap latest data.
        /// </summary>
        /// <param name="marketCap"></param>
        private void _BroadcastMarketCapDataToConnectedClients(MarketCap marketCap)
        {
            try
            {
                GlobalData globalData = new GlobalData()
                {
                    btc_dominance = marketCap.btc_dominance,
                    market_capt = marketCap.market_capt,
                    volume_total = marketCap.volume_total,
                    btc_dominanceval = (marketCap.market_capt * marketCap.btc_dominance / 100)
                };
                hubContext.Clients.All.SendMarketCapObject(globalData);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Convert the current currency price into btc price.
        /// </summary>
        /// <param name="currencyName"></param>
        /// <param name="currenciesModel"></param>
        /// <returns></returns>
        private double _GetBTCPrice(string currentCurrencyPrice, string btcPrice)
        {
            double price = 0;
            price = Convert.ToDouble(currentCurrencyPrice) / Convert.ToDouble(btcPrice);
            return price;
        }

        /// <summary>
        /// Get the api response.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private string _GetResponseFromAPI(string url)
        {
            string json = string.Empty;
            try
            {
                using (CustomWebClient webClient = new CustomWebClient())
                {
                    json = webClient.DownloadString(url);
                }
            }
            catch (WebException ex)
            {
                _LogWebErrorMessage("Url is: " + url);
                _LogWebErrorMessage(ex.Message);
                _GetResponseFromAPI(url);
            }
            catch (Exception ex)
            {
                _LogErrorMessage("Url is: " + url, "_GetResponseFromAPI");
                _LogErrorMessage(ex.Message, "_GetResponseFromAPI");
                _GetResponseFromAPI(url);
            }
            return json;
        }

        /// <summary>
        /// Log error message to file.
        /// </summary>
        /// <param name="message"></param>
        private void _LogErrorMessage(string message, string level)
        {
            using (StreamWriter _streamWriter = File.AppendText(_filePath))
            {
                _streamWriter.WriteLine("Exception occured: " + DateTime.Now);
                _streamWriter.WriteLine("Level : " + level);
                _streamWriter.WriteLine("Exception : " + message);
                _streamWriter.WriteLine("=======================================================================");
            }
            _SendEmail(level, message);
        }

        /// <summary>
        /// Log web status error message to file.
        /// </summary>
        /// <param name="message"></param>
        private void _LogWebErrorMessage(string message)
        {
            using (StreamWriter _streamWriter = File.AppendText(_webExceptionFilePath))
            {
                _streamWriter.WriteLine("Exception occured: " + DateTime.Now);
                _streamWriter.WriteLine("Exception : " + message);
                _streamWriter.WriteLine("=======================================================================");
            }
        }

        /// <summary>
        /// Log the exception message of interval threads.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="level"></param>
        private void _LogIntervalDataException(string message, string level)
        {
            using (StreamWriter _streamWriter = File.AppendText(_intervalFilePath))
            {
                _streamWriter.WriteLine("Exception occured: " + DateTime.Now);
                _streamWriter.WriteLine("Exception : " + message);
                _streamWriter.WriteLine("Level : " + level);
                _streamWriter.WriteLine("=======================================================================");
            }
        }

        /// <summary>
        /// Log the timer count which are parsed for rank and change in currency percentage.
        /// </summary>
        /// <param name="count"></param>
        /// <param name="level"></param>
        private void _LogTimerCount(long count, string level)
        {
            using (StreamWriter _streamWriter = File.AppendText(_timerCurrencyCount))
            {
                _streamWriter.WriteLine("Count : " + count);
                _streamWriter.WriteLine("Level : " + level);
                _streamWriter.WriteLine("=======================================================================");
            }
        }

        /// <summary>
        /// Send email to developer if some unexpected error occured.
        /// </summary>
        /// <param name="errorMessage"></param>
        private void _SendEmail(string level, string errorMessage)
        {
            SmtpClient client = new SmtpClient();
            try
            {
                client.Host = ConfigurationManager.AppSettings["Email"];
                client.Port = Convert.ToInt32(ConfigurationManager.AppSettings["EmailPort"]);
                MailMessage mailMessage = new MailMessage(ConfigurationManager.AppSettings["EmailFrom"], ConfigurationManager.AppSettings["EmailTo"]);
                mailMessage.Subject = string.Concat("Coin365 Crawling Issue");
                mailMessage.IsBodyHtml = true;
                mailMessage.Body = string.Concat("Exception occured at : ", level, "<br/> Error message is: ", errorMessage);
                client.Credentials = new NetworkCredential(ConfigurationManager.AppSettings["EmailFrom"], ConfigurationManager.AppSettings["EmailPassword"]);
                client.Send(mailMessage);
            }
            catch (Exception)
            {
            }
            finally
            {
                client.Dispose();
            }
        }
        #endregion
    }

    #region Custom WebClient
    public class CustomWebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            HttpWebRequest request = base.GetWebRequest(address) as HttpWebRequest;
            request.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
#if !DEBUG
            request.Proxy = new WebProxy("127.0.0.1:24001");
#endif
            request.Headers.Clear();
            request.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0");
            request.Headers.Add(HttpRequestHeader.Accept, "application/json");
            request.Headers.Add(HttpRequestHeader.ContentType, "application/json");
            ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback
            (
                delegate { return true; }
            );
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | SecurityProtocolType.Ssl3;
            return request;
        }
    }
    #endregion

    #region Models
    public class CurrenciesPercentages
    {
        public string Currency { get; set; }
        public double PercentageChange { get; set; }
        public double MarketCapChange { get; set; }
    }

    public class CurrenciesIntervalData
    {
        public List<IntervalData> data { get; set; }
        public long timestamp { get; set; }
    }

    public class IntervalData
    {
        public string priceUsd { get; set; }
        public string circulatingSupply { get; set; }
        public object time { get; set; }
        public DateTime date { get; set; }
    }

    public class Assets
    {
        public List<AssetsData> data { get; set; }
        public long timestamp { get; set; }
    }

    public class AssetsData
    {
        public string id { get; set; }
        public string rank { get; set; }
        public string symbol { get; set; }
        public string name { get; set; }
        public string supply { get; set; }
        public string maxSupply { get; set; }
        public string marketCapUsd { get; set; }
        public string volumeUsd24Hr { get; set; }
        public string priceUsd { get; set; }
        public string changePercent24Hr { get; set; }
        public string vwap24Hr { get; set; }
    }

    public class CryptoCurrencyLinks
    {
        public ObjectId id { get; set; }
        public int index { get; set; }
        public string currency_name { get; set; }
        public string base_currency { get; set; }
        public string web_link { get; set; }
        public string description { get; set; }
        public string facebook_link { get; set; }
        public string twitter_link { get; set; }
        public string git_link { get; set; }
        public string news { get; set; }
        public string reggit_link { get; set; }
    }

    public class ExchangeSettings
    {
        public int ID { get; set; }
        public string ExchangeName { get; set; }
        public string PairURL { get; set; }
        public string TickerURL { get; set; }
        public bool IsSynced { get; set; }
        public bool IsSingleAPI { get; set; }
        public bool IsStatic { get; set; }
    }

    public class ExchangeViewModel
    {
        public ExchangeViewModel()
        {
            exchangeSettings = new List<ExchangeSettings>();
            exchangesInfos = new List<ExchangesInfo>();
        }
        public List<ExchangeSettings> exchangeSettings { get; set; }
        public List<ExchangesInfo> exchangesInfos { get; set; }
    }

    public class ExchangesInfo
    {
        public int ID { get; set; }
        public int ExchangeID { get; set; }
        public string ExchangeName { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public string API_Pair { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class CurrencyModelForCache
    {
        public ObjectId id { get; set; }
        public string base_currency { get; set; }
        public string basecurrency_fullname { get; set; }
        public double price { get; set; }
        public double volume { get; set; }
        public double volume_sum { get; set; }
        public double prcnttrading_vol { get; set; }
        public double price_share { get; set; }
        public double average_price { get; set; }
        public double prcnt_change { get; set; }
        public bool isupdated { get; set; }
        public int position { get; set; }
        public int rank { get; set; }
        public double market_cap { get; set; }
        public double circulating_supply { get; set; }
        public double average_percentagechange { get; set; }
        public double average_volume { get; set; }
        public DateTime created_date { get; set; }
        public string formatted_date { get; set; }
        public int buy_votes { get; set; }
        public int sell_votes { get; set; }
        public double prct_ath { get; set; }
        public double btc_price { get; set; }
        public string coin_description { get; set; }
        public double prcnt_change7d { get; set; }
        public double prcnt_change1m { get; set; }
        public double prcnt_change3m { get; set; }
        public double prcnt_change1y { get; set; }
        public double prcnt_changeytd { get; set; }
        public double prcnt_changeall { get; set; }
        public double max_supply { get; set; }
        public int rank_change7d { get; set; }
        public int rank_change1m { get; set; }
        public int rank_change3m { get; set; }
        public int rank_change1y { get; set; }
        public int rank_changeytd { get; set; }
        public int rank_changeall { get; set; }
        public int position_p { get; set; }
        public string currency_logo { get; set; }
        public string linkedIn_link { get; set; }
        public string twitter_link { get; set; }
        public string facebook_link { get; set; }
        public string instagram_link { get; set; }
        public string reddit_link { get; set; }
        public string github_link { get; set; }
        public string website_link { get; set; }
    }

    public class ExchangesDatum
    {
        public string exchangeId { get; set; }
        public string name { get; set; }
        public string rank { get; set; }
        public string percentTotalVolume { get; set; }
        public string volumeUsd { get; set; }
        public string tradingPairs { get; set; }
        public bool? socket { get; set; }
        public string exchangeUrl { get; set; }
        public object updated { get; set; }
    }

    public class Exchanges
    {
        public List<ExchangesDatum> data { get; set; }
        public long timestamp { get; set; }
    }

    public class GenericModelforCurrencies
    {
        public ObjectId id { get; set; }
        public string symbol { get; set; }
        public string basecurrency_fullname { get; set; }
        public string converted_symbol { get; set; }
        public string logo_url { get; set; }
        public string currency_pair { get; set; }
        public double price { get; set; }
        public double high { get; set; }
        public double low { get; set; }
        public double open { get; set; }
        public double close { get; set; }
        public double bid { get; set; }
        public double ask { get; set; }
        public double change { get; set; }
        public double unit_price { get; set; }
        public int position { get; set; }
        public double volume { get; set; }
        public int rank { get; set; }
        public double percentage_change { get; set; }
        public DateTime created_date { get; set; }
        public TimeSpan timestamp { get; set; }
        public int exchangeid { get; set; }
        public int exchangeinfoid { get; set; }
        public double prcnttrading_volume { get; set; }
        public double priceshare { get; set; }
        public double prcnt_change { get; set; }
    }

    public class AverageDataCurrencies
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string id { get; set; }
        public string base_currency { get; set; }
        public string basecurrency_fullname { get; set; }
        public double price { get; set; }
        public double volume { get; set; }
        public double prcnttrading_vol { get; set; }
        public double prcnt_change { get; set; }
        public bool isupdated { get; set; }
        public int position { get; set; }
        public int rank { get; set; }
        public double market_cap { get; set; }
        public double circulating_supply { get; set; }
        public double average_volume { get; set; }
        public DateTime created_date { get; set; }
        public int buy_votes { get; set; }
        public int sell_votes { get; set; }
        public double prct_ath { get; set; }
        public double btc_price { get; set; }
        public string coin_description { get; set; }
        public double prcnt_change7d { get; set; }
        public double prcnt_change1m { get; set; }
        public double prcnt_change3m { get; set; }
        public double prcnt_change1y { get; set; }
        public double prcnt_changeytd { get; set; }
        public double prcnt_changeall { get; set; }
        public double? max_supply { get; set; }
        public int rank_change7d { get; set; }
        public int rank_change1m { get; set; }
        public int rank_change3m { get; set; }
        public int rank_change1y { get; set; }
        public int rank_changeytd { get; set; }
        public int rank_changeall { get; set; }
        public int position_p { get; set; }
        public string currency_logo { get; set; }
        public string linkedIn_link { get; set; }
        public string twitter_link { get; set; }
        public string facebook_link { get; set; }
        public string instagram_link { get; set; }
        public string reddit_link { get; set; }
        public string github_link { get; set; }
        public string website_link { get; set; }
        public string category { get; set; }
    }

    public class ChartsData
    {
        public ObjectId id { get; set; }
        public double price { get; set; }
        public double btc_price { get; set; }
        public string datetime { get; set; }
        public double market_cap { get; set; }
        public double volume { get; set; }
        public string base_currency { get; set; }
        public DateTime created_date { get; set; }
    }

    public class BinnanceData
    {
        public string exchangeId { get; set; }
        public string rank { get; set; }
        public string baseSymbol { get; set; }
        public string baseId { get; set; }
        public string quoteSymbol { get; set; }
        public string quoteId { get; set; }
        public string priceQuote { get; set; }
        public string priceUsd { get; set; }
        public string volumeUsd24Hr { get; set; }
        public string percentExchangeVolume { get; set; }
        public string tradesCount24Hr { get; set; }
        public object updated { get; set; }
    }

    public class Binance
    {
        public List<BinnanceData> data { get; set; }
        public long timestamp { get; set; }
    }

    public class ExchangesInfoModel : IIdentified
    {
        public ObjectId Id { get; set; }
        public string exchange_name { get; set; }
        public string exchange_url { get; set; }
        public int exchange_id { get; set; }
    }

    public class PhysicalCurrencyModel
    {
        public string disclaimer { get; set; }
        public string license { get; set; }
        public int timestamp { get; set; }
        public string @base { get; set; }
        public Rates rates { get; set; }
    }

    public class Rates
    {
        public double AED { get; set; }
        public double AFN { get; set; }
        public double ALL { get; set; }
        public double AMD { get; set; }
        public double ANG { get; set; }
        public double AOA { get; set; }
        public double ARS { get; set; }
        public double AUD { get; set; }
        public double AWG { get; set; }
        public double AZN { get; set; }
        public double BAM { get; set; }
        public int BBD { get; set; }
        public double BDT { get; set; }
        public double BGN { get; set; }
        public double BHD { get; set; }
        public double BIF { get; set; }
        public int BMD { get; set; }
        public double BND { get; set; }
        public double BOB { get; set; }
        public double BRL { get; set; }
        public int BSD { get; set; }
        public double BTC { get; set; }
        public double BTN { get; set; }
        public double BWP { get; set; }
        public double BYN { get; set; }
        public double BZD { get; set; }
        public double CAD { get; set; }
        public double CDF { get; set; }
        public double CHF { get; set; }
        public double CLF { get; set; }
        public double CLP { get; set; }
        public double CNH { get; set; }
        public double CNY { get; set; }
        public double COP { get; set; }
        public double CRC { get; set; }
        public int CUC { get; set; }
        public double CUP { get; set; }
        public double CVE { get; set; }
        public double CZK { get; set; }
        public double DJF { get; set; }
        public double DKK { get; set; }
        public double DOP { get; set; }
        public double DZD { get; set; }
        public double EGP { get; set; }
        public double ERN { get; set; }
        public double ETB { get; set; }
        public double EUR { get; set; }
        public double FJD { get; set; }
        public double FKP { get; set; }
        public double GBP { get; set; }
        public double GEL { get; set; }
        public double GGP { get; set; }
        public double GHS { get; set; }
        public double GIP { get; set; }
        public double GMD { get; set; }
        public double GNF { get; set; }
        public double GTQ { get; set; }
        public double GYD { get; set; }
        public double HKD { get; set; }
        public double HNL { get; set; }
        public double HRK { get; set; }
        public double HTG { get; set; }
        public double HUF { get; set; }
        public double IDR { get; set; }
        public double ILS { get; set; }
        public double IMP { get; set; }
        public double INR { get; set; }
        public double IQD { get; set; }
        public double IRR { get; set; }
        public double ISK { get; set; }
        public double JEP { get; set; }
        public double JMD { get; set; }
        public double JOD { get; set; }
        public double JPY { get; set; }
        public double KES { get; set; }
        public double KGS { get; set; }
        public double KHR { get; set; }
        public double KMF { get; set; }
        public int KPW { get; set; }
        public double KRW { get; set; }
        public double KWD { get; set; }
        public double KYD { get; set; }
        public double KZT { get; set; }
        public double LAK { get; set; }
        public double LBP { get; set; }
        public double LKR { get; set; }
        public double LRD { get; set; }
        public double LSL { get; set; }
        public double LYD { get; set; }
        public double MAD { get; set; }
        public double MDL { get; set; }
        public double MGA { get; set; }
        public double MKD { get; set; }
        public double MMK { get; set; }
        public double MNT { get; set; }
        public double MOP { get; set; }
        public double MRO { get; set; }
        public double MRU { get; set; }
        public double MUR { get; set; }
        public double MVR { get; set; }
        public double MWK { get; set; }
        public double MXN { get; set; }
        public double MYR { get; set; }
        public double MZN { get; set; }
        public double NAD { get; set; }
        public double NGN { get; set; }
        public double NIO { get; set; }
        public double NOK { get; set; }
        public double NPR { get; set; }
        public double NZD { get; set; }
        public double OMR { get; set; }
        public int PAB { get; set; }
        public double PEN { get; set; }
        public double PGK { get; set; }
        public double PHP { get; set; }
        public double PKR { get; set; }
        public double PLN { get; set; }
        public double PYG { get; set; }
        public double QAR { get; set; }
        public double RON { get; set; }
        public double RSD { get; set; }
        public double RUB { get; set; }
        public double RWF { get; set; }
        public double SAR { get; set; }
        public double SBD { get; set; }
        public double SCR { get; set; }
        public double SDG { get; set; }
        public double SEK { get; set; }
        public double SGD { get; set; }
        public double SHP { get; set; }
        public double SLL { get; set; }
        public double SOS { get; set; }
        public double SRD { get; set; }
        public double SSP { get; set; }
        public double STD { get; set; }
        public double STN { get; set; }
        public double SVC { get; set; }
        public double SYP { get; set; }
        public double SZL { get; set; }
        public double THB { get; set; }
        public double TJS { get; set; }
        public double TMT { get; set; }
        public double TND { get; set; }
        public double TOP { get; set; }
        public double TRY { get; set; }
        public double TTD { get; set; }
        public double TWD { get; set; }
        public double TZS { get; set; }
        public double UAH { get; set; }
        public double UGX { get; set; }
        public int USD { get; set; }
        public double UYU { get; set; }
        public double UZS { get; set; }
        public double VEF { get; set; }
        public double VND { get; set; }
        public double VUV { get; set; }
        public double WST { get; set; }
        public double XAF { get; set; }
        public double XAG { get; set; }
        public double XAU { get; set; }
        public double XCD { get; set; }
        public double XDR { get; set; }
        public double XOF { get; set; }
        public double XPD { get; set; }
        public double XPF { get; set; }
        public double XPT { get; set; }
        public double YER { get; set; }
        public double ZAR { get; set; }
        public double ZMW { get; set; }
        public double ZWL { get; set; }
    }

    public class PhysicalCurrencyModelMongo : IIdentified
    {
        public ObjectId Id { get; set; }
        public string currency { get; set; }
        public double rate { get; set; }
    }

    public class data
    {
        public int active_cryptocurrencies { get; set; }
        public int active_markets { get; set; }
        public double bitcoin_percentage_of_market_cap { get; set; }
        public QuotesCC quotes { get; set; }
        public int last_updated { get; set; }
    }

    public class USD
    {
        public double total_market_cap { get; set; }
        public double total_volume_24h { get; set; }
    }

    public class QuotesCC
    {
        public USD USD { get; set; }
    }

    public class Metadata
    {
        public int timestamp { get; set; }
        public object error { get; set; }
    }

    public class MarketCap
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }
        public double market_capt { get; set; }
        public double volume_total { get; set; }
        public double btc_dominance { get; set; }
    }

    public class CurrenciesCategories
    {
        public string symbol { get; set; }
        public string category { get; set; }
    }

    public class CoinCapMarketCap
    {
        public List<Datum> data { get; set; }
        public long timestamp { get; set; }
    }

    public class Datum
    {
        public string id { get; set; }
        public string rank { get; set; }
        public string symbol { get; set; }
        public string name { get; set; }
        public string supply { get; set; }
        public string maxSupply { get; set; }
        public string marketCapUsd { get; set; }
        public string volumeUsd24Hr { get; set; }
        public string priceUsd { get; set; }
        public string changePercent24Hr { get; set; }
        public string vwap24Hr { get; set; }
    }

    public class GlobalData
    {
        public double market_capt { get; set; }
        public double volume_total { get; set; }
        public double btc_dominance { get; set; }
        public double btc_dominanceval { get; set; }
    }
    #endregion
}