namespace ScrawlDataIntraday.Models
{
    public class Trade
    {
        public string Guid { get; set; }
        public DateTime TradingTime { get; set; }
        public int Volume { get; set; }
        public decimal Price { get; set; }
        public string Side { get; set; }
        public string Code { get; set; }
        public int Package { get; set; }

        public Trade(string guid, DateTime tradingTime, int volume, decimal price, string side, string code, int package)
        {
            Guid = guid;
            TradingTime = tradingTime;
            Volume = volume;
            Price = price;
            Side = side;
            Code = code;
            Package = package;
        }
    }
}