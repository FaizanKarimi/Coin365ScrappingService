using System;
using System.Configuration;

namespace CoinCapParserService
{
    public static class CommHelpers
    {
        public static string GetRedisUrl()
        {
            string url = string.Empty;
            url = ConfigurationManager.AppSettings["RedisURL"].ToString();
            return url;
        }

        public static int GetCurrenciesLimit()
        {
            int limit = 0;
            limit = Convert.ToInt32(ConfigurationManager.AppSettings["CurrenciesLimit"]);
            return limit;
        }
    }
}