using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using ScrawlDataIntraday.Models;

public class StockIntradayService
{
    private readonly IMongoCollection<StockIntraday> _booksCollection;

    public StockIntradayService(
        IOptions<ScrawlDataIntraday.Models.MongoDatabaseSettings> bookStoreDatabaseSettings)
    {
        var mongoClient = new MongoClient(
            bookStoreDatabaseSettings.Value.ConnectionString);

        var mongoDatabase = mongoClient.GetDatabase(
            bookStoreDatabaseSettings.Value.DatabaseName);
        
        bool isMongoLive = mongoDatabase.RunCommandAsync((Command<BsonDocument>)"{ping:1}").Wait(1000);

        if (isMongoLive)
        {
            // connected
        }
        else
        {
            throw new Exception("MongoDB is not connected");
        }
        _booksCollection = mongoDatabase.GetCollection<StockIntraday>(
            bookStoreDatabaseSettings.Value.BooksCollectionName);
    }

    public async Task<List<StockIntraday>> GetAsync() =>
        await _booksCollection.Find(_ => true).ToListAsync();

    public async Task<StockIntraday?> GetAsync(string id) =>
        await _booksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task CreateAsync(StockIntraday newBook, bool upsert = false) =>
        await _booksCollection.InsertOneAsync(newBook);

    public async Task UpdateAsync(StockIntraday updatedBook, bool upsert = false) {
        var filter = Builders<StockIntraday>.Filter.Eq(x => x.StockCode, updatedBook.StockCode) & Builders<StockIntraday>.Filter.Eq(x => x.TradingTime, updatedBook.TradingTime);

        var existingBook = await _booksCollection.Find(filter).FirstOrDefaultAsync();
        if (existingBook != null)
        {
            await RemoveAsync(existingBook.Id);
        }
        await CreateAsync(updatedBook);
    }

    public async Task RemoveAsync(string id) =>
        await _booksCollection.DeleteOneAsync(x => x.Id == id);
}