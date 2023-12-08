using MongoDB.Bson;

namespace CoinCapParserService
{
    public interface IIdentified
    {
        ObjectId Id { get; }
    }
}