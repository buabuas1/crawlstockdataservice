using MassTransit;
using MassTransit.Testing;
using ScrawlDataIntraday.Models;

namespace ScrawlDataIntraday.Service
{
    public class StockConsumer : IConsumer<StockMessage>
    {
        private readonly CrawlDataService _crawlDataService;

        public StockConsumer(CrawlDataService crawlDataService)
        {
            _crawlDataService = crawlDataService;
        }

        public async Task Consume(ConsumeContext<StockMessage> context)
        {
            Console.WriteLine("Consume: " + context.Message.StockCode);
            await _crawlDataService.Run(context.Message.StockCode);
        }
    }
}
