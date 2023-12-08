using System;
using StackExchange.Redis;

namespace CoinCapParserService
{
    /// <summary>
    /// IRedisService class
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    public interface IRedisService : IDisposable
    {
        /// <summary>
        /// Clear the cache.
        /// </summary>
        void ClearCache();

        /// <summary>
        /// Get the cached entity.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        T Get<T>(string key);

        /// <summary>
        /// Remove the cached enity.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        bool Remove(string key);

        /// <summary>
        /// Set the cache entity without the expiry time.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        bool Set<T>(string key, T value);

        /// <summary>
        /// Set the cache entity with the expiry time.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expireTime"></param>
        /// <returns></returns>
        bool Set<T>(string key, T value, double expireTime);

        /// <summary>
        /// Creates the transaction.
        /// </summary>
        /// <returns>transaction object</returns>
        ITransaction CreateTransaction();
    }
}