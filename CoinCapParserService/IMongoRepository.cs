using System.Threading.Tasks;
using MongoDB.Driver;
using System.Collections.Generic;
using System;
using System.Linq.Expressions;

namespace CoinCapParserService
{
    public interface IMongoRepository
    {
        /// <summary>
        /// Delete record from the table.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="entity"></param>
        /// <returns></returns>
        Task<DeleteResult> DeleteAsync<T>(T entity, string collectionName) where T : IIdentified;

        /// <summary>
        /// Get record from the table.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        T Get<T>(Expression<Func<T, bool>> predicate) where T : class, new();

        /// <summary>
        /// Get collection from the database.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="mongoDatabase"></param>
        /// <returns></returns>
        IMongoCollection<T> GetCollection<T>() where T : class, new();

        /// <summary>
        /// Get list of records from the table.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="mongoCollection"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        List<T> GetList<T>(IMongoCollection<T> mongoCollection, Expression<Func<T, bool>> predicate) where T : IIdentified;

        /// <summary>
        /// Insert record into the table.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="entity"></param>
        void Insert<T>(T entity, string collectionName) where T : IIdentified;

        /// <summary>
        /// Insert records into the table.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entities"></param>
        /// <param name="collectionName"></param>
        void InsertMany<T>(List<T> entities, string collectionName) where T : class;

        /// <summary>
        /// Update record into the table.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="entity"></param>
        Task<ReplaceOneResult> UpdateAsync<T>(T entity, string collectionName) where T : IIdentified;

        /// <summary>
        /// Drop database.
        /// </summary>
        /// <param name="databaseName"></param>
        void DropDatabase(string databaseName);

        /// <summary>
        /// Droping the collection.
        /// </summary>
        /// <param name="collectionName"></param>
        void DropCollection(string collectionName);
    }
}