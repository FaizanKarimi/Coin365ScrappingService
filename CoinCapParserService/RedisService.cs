using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Net;

namespace CoinCapParserService
{
    /// <summary>
    /// RedisService class.
    /// </summary>
    /// <seealso cref="IRedisService" />
    public class RedisService : IRedisService
    {
        /// <summary>
        /// The two day expiry time
        /// </summary>
        private const int _twoDayExpiryTime = 172800000;

        /// <summary>
        /// The connection multiplexer
        /// </summary>
        private IConnectionMultiplexer _connectionMultiplexer;

        /// <summary>
        /// The database
        /// </summary>
        private IDatabase _database;

        /// <summary>
        /// The end point
        /// </summary>
        private EndPoint _endPoint;

        /// <summary>
        /// The server
        /// </summary>
        private IServer _server;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisService"/> class.
        /// </summary>
        public RedisService()
        {
            this._connectionMultiplexer = ConnectionMultiplexer.Connect(CommHelpers.GetRedisUrl());
            this._database = this._connectionMultiplexer.GetDatabase();
            this._endPoint = this._connectionMultiplexer.GetEndPoints()[0];
            this._server = this._connectionMultiplexer.GetServer(this._endPoint);
        }

        /// <summary>
        /// Creates the transaction.
        /// </summary>
        /// <returns>
        /// transaction object
        /// </returns>
        public ITransaction CreateTransaction()
        {
            return this._database.CreateTransaction();
        }

        /// <summary>
        /// Set the cache entity without the expiry time.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool Set<T>(string key, T value)
        {
            bool set = false;
            string data = JsonConvert.SerializeObject(value);
            set = this._database.StringSet(key, data, TimeSpan.FromMilliseconds(_twoDayExpiryTime));
            return set;
        }

        /// <summary>
        /// Set the cache entity with the expiry time.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expireTime"></param>
        /// <returns></returns>
        public bool Set<T>(string key, T value, double expireTime)
        {
            bool set = false;
            string data = JsonConvert.SerializeObject(value);
            if (!string.IsNullOrEmpty(data))
            {
                set = this._database.StringSet(key, data, TimeSpan.FromMilliseconds(_twoDayExpiryTime));
            }
            return set;
        }

        /// <summary>
        /// Get the cached entity.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public T Get<T>(string key)
        {
            T entity = default(T);
            string data = this._database.StringGet(key);
            if (!string.IsNullOrEmpty(data))
            {
                entity = JsonConvert.DeserializeObject<T>(data);
            }
            return entity;
        }

        /// <summary>
        /// Remove the cached enity.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
`        public bool Remove(string key)
        {
            bool removed = false;
            removed = this._database.KeyDelete(key);
            return removed;
        }

        /// <summary>
        /// Clear the cache.
        /// </summary>
        public void ClearCache()
        {
            int databaseNumber = this._database.Database;
            this._server.FlushDatabase(databaseNumber);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            this._connectionMultiplexer.Dispose();
        }
    }
}