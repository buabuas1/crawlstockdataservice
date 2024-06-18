using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System;
using Newtonsoft.Json;

namespace ScrawlDataIntraday.Models
{
    public class StockIntraday
    {
        [BsonElement("_id")]
        [JsonProperty("_id")]
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime TradingTime { get; set; }
        public string StockCode { get; set; }
        public List<Trade> Trades { get; set; } = new List<Trade>();

        public StockIntraday(DateTime createdDate, DateTime tradingTime, string stockCode, List<Trade> trades)
        {
            CreatedDate = createdDate;
            TradingTime = tradingTime;
            StockCode = stockCode;
            Trades = trades;
            Id = ObjectId.GenerateNewId().ToString();
        }
    }
}
