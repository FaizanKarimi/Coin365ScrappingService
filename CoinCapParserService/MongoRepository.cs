using System.Threading.Tasks;
using MongoDB.Driver;
using System.Collections.Generic;
using System;
using System.Configuration;
using System.Linq;
using System.Linq.Expressions;
using System.Data;

namespace CoinCapParserService
{
    public class MongoRepository : IMongoRepository
    {
        #region Private Members        
        private IMongoDatabase _mongoDatabase;
        private MongoClient _mongoClient;
        #endregion

        #region Contructor
        public MongoRepository()
        {
            //#if !DEBUG
            //            var credentials = getMongoCredentials();
            //            var settings = new MongoClientSettings
            //            {
            //                Credential = credentials
            //            };
            //            _mongoClient = new MongoClient(settings);
            //            _mongoDatabase = _mongoClient.GetDatabase("coin365");
            //#else
            string mongoUrl = "mongodb://127.0.0.1:27017";
            _mongoClient = new MongoClient(mongoUrl);
            _mongoDatabase = _mongoClient.GetDatabase("coin365");
            //#endif
        }
        #endregion

        #region Public Methods
        public async Task<ReplaceOneResult> UpdateAsync<T>(T entity, string collectionName) where T : IIdentified
        {
            IMongoCollection<T> mongoCollection = _mongoDatabase.GetCollection<T>(collectionName);
            return await mongoCollection.ReplaceOneAsync(x => x.Id == entity.Id, entity, new UpdateOptions { IsUpsert = false });
        }

        public async Task<DeleteResult> DeleteAsync<T>(T entity, string collectionName) where T : IIdentified
        {
            IMongoCollection<T> mongoCollection = _mongoDatabase.GetCollection<T>(collectionName);
            return await mongoCollection.DeleteOneAsync(x => x.Id == entity.Id);
        }

        public void Insert<T>(T entity, string collectionName) where T : IIdentified
        {
            IMongoCollection<T> mongoCollection = _mongoDatabase.GetCollection<T>(collectionName);
            mongoCollection.InsertOneAsync(entity, new InsertOneOptions { BypassDocumentValidation = false }).Wait();
        }

        public void InsertSingleEntity<T>(T entity, string collectionName) where T : class
        {
            IMongoCollection<T> mongoCollection = _mongoDatabase.GetCollection<T>(collectionName);
            mongoCollection.InsertOneAsync(entity, new InsertOneOptions { BypassDocumentValidation = false }).Wait();
        }

        public void InsertMany<T>(List<T> entities, string collectionName) where T : class
        {
            IMongoCollection<T> mongoCollection = _mongoDatabase.GetCollection<T>(collectionName);
            mongoCollection.InsertManyAsync(entities, new InsertManyOptions { BypassDocumentValidation = false }).Wait();
        }

        public T TestCollection<T>(string collectionName) where T : class
        {
            IMongoCollection<T> mongoCollection = _mongoDatabase.GetCollection<T>(collectionName);
            return mongoCollection.AsQueryable().FirstOrDefault();
        }

        public T Get<T>(Expression<Func<T, bool>> predicate) where T : class, new()
        {
            T obj = new T();
            string name = obj.GetType().Name;
            IMongoCollection<T> mongoCollection = _mongoDatabase.GetCollection<T>(name);
            return mongoCollection.AsQueryable().FirstOrDefault(predicate);
        }

        public List<T> GetList<T>(IMongoCollection<T> mongoCollection, Expression<Func<T, bool>> predicate) where T : IIdentified
        {
            return mongoCollection.AsQueryable().Where<T>(predicate).ToList();
        }

        public IMongoCollection<T> GetCollection<T>() where T : class, new()
        {
            T entity = new T();
            Type type = entity.GetType();
            string name = type.Name;
            return _mongoDatabase.GetCollection<T>(name);
        }
        public IMongoCollection<T> GetSingleCollection<T>(string collectionName) where T : class, new()
        {
            IMongoCollection<T> collection = _mongoDatabase.GetCollection<T>(collectionName);
            return collection;
        }

        public void DropDatabase(string databaseName)
        {
            _mongoClient.DropDatabase(databaseName);
        }

        public void DropCollection(string collectionName)
        {
            _mongoDatabase.DropCollection(collectionName);
        }
        #endregion

        public MongoCredential getMongoCredentials()
        {
            try
            {
                int IsServerEnv = Convert.ToInt32(ConfigurationManager.AppSettings["IsServer"]);
                MongoCredential credential = MongoCredential.CreateMongoCRCredential("admin", "user1", "123456");
                if (IsServerEnv == 1)
                {
                    credential = MongoCredential.CreateMongoCRCredential("admin", "coinadmin", "Sixlogics123");
                }
                return credential;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}