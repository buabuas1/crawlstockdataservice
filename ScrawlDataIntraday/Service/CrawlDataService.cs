using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium;
using Polly;
using ScrawlDataIntraday.Common;
using ScrawlDataIntraday.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MassTransit;

namespace ScrawlDataIntraday.Service
{
    public class CrawlDataService
    {
        private readonly ILogger<Worker> _logger;
        private readonly StockIntradayService _stockIntradayService;
        private readonly IBus _bus;
        private readonly IConfiguration _configuration;

        public CrawlDataService(ILogger<Worker> logger, StockIntradayService stockIntradayService, IBus bus,
            IConfiguration configuration)
        {
            _logger = logger;
            _stockIntradayService = stockIntradayService;
            _bus = bus;
            _configuration = configuration;
        }
        public async Task Run(string stockCode)
        {
            
            var retryPolicy = Policy
                        .Handle<Exception>()
                        .WaitAndRetryAsync(
                            retryCount: 3,
                            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(5),
                            onRetryAsync: async (exception, timespan, retryAttempt, context) =>
                            {
                                Console.WriteLine($"Retry attempt {retryAttempt} after {timespan.TotalSeconds} seconds due to:      {exception.Message}");
                                _logger.LogError("Error Stock: " + context["stock"]);
                            });
            var rs = await retryPolicy.ExecuteAsync(async (ctx) =>
            {
                ctx["stock"] = stockCode;
                return await GetStockIntraday(stockCode);
            }, new Context());

            await SaveTradesToMongo(rs);

            var ind = ConstStock.DefaultStocks.IndexOf(stockCode);
            _logger.LogInformation(string.Format("Done {0}, Processed: {1}%", stockCode, (Math.Round((double)ind / ConstStock.DefaultStocks.Count * 100))));
            if (ind == ConstStock.DefaultStocks.Count - 1)
            {
                _logger.LogInformation("Done all");
            }
        }

        private async Task SaveTradesToMongo(List<Trade>? trades)
        {
            if (trades?.Count > 0)
            {
                var stockIntraday = new StockIntraday(DateTime.Now, trades[0].TradingTime.Date, trades[0].Code, trades);
                await _stockIntradayService.UpdateAsync(stockIntraday, true);
                _logger.LogInformation("Saved Mongo: " + stockIntraday.StockCode);
            }
        }

        private async Task<List<Trade>> GetStockIntraday(string stockCode)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            ChromeDriver driver = null;

            ChromeOptions options = new ChromeOptions();
            options.AddArgument("--headless"); // Run Chrome in headless mode
                                               //options.AddArgument("--disable-gpu"); // Disable GPU hardware acceleration
                                               //options.AddArgument("--window-size=1920,1080"); // Set window size if needed
            options.AddArgument("--no-sandbox"); // Bypass OS security model (use with caution)

            // Initialize the ChromeDriver with options
            driver = new ChromeDriver(options);

            //var driver = new ChromeDriver();
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);

            driver.Navigate().GoToUrl("https://stockbiz.vn/ma-chung-khoan/" + stockCode);

            IWebElement iframe = driver.FindElement(By.CssSelector("#__next > div.mx-auto.max-w-7xl.mt-4.px-4.lg\\:px-8 > div > main > div > div.grid.grid-cols-3.gap-4.w-full.max-xl\\:grid-cols-2.max-md\\:grid-cols-1.mt-4 > div:nth-child(1) > div:nth-child(2) > div.flex.flex-col.flex-1 > div.flex.flex-row.w-full.gap-2.pb-3 > button.inline-flex.items-center.justify-center.text-sm.font-medium.transition-colors.focus-visible\\:outline-none.focus-visible\\:ring-2.focus-visible\\:ring-ring.focus-visible\\:ring-offset-2.disabled\\:opacity-50.disabled\\:pointer-events-none.ring-offset-background.bg-\\[\\#A855F7\\].text-primary-foreground.dark\\:text-white.hover\\:bg-\\[\\#A855F7\\]\\/90.h-9.px-3.rounded-md"));

            IList<IWebElement> elements = driver.FindElements(By.TagName("button"));

            foreach (IWebElement e in elements)
            {
                if (e.Text == "Sổ lệnh")
                {
                    iframe = e;
                    break;
                }
            }

            var scrollOrigin = new WheelInputDevice.ScrollOrigin
            {
                Element = iframe,
                XOffset = 0,
                YOffset = -50
            };
            new Actions(driver)
                .ScrollFromOrigin(scrollOrigin, 0, 500)
                .Perform();


            new Actions(driver)
                .Click(iframe)
                .Perform();

            var listTrades = new List<Trade>();
            int tradeCount = listTrades.Count;
            for (int i = 0; i < 10000; i++)
            {
                CrawlTrades(driver, listTrades, stockCode);
                if (tradeCount == listTrades.Count)
                {
                    break;
                }
                tradeCount = listTrades.Count;
                if (tradeCount == 0)
                {
                    _logger.LogError("Error trades: " + stockCode);
                }
            }
            _logger.LogInformation(stockCode + " Total trades: " + listTrades.Count);
            stopwatch.Stop();
            _logger.LogInformation(string.Format("Stock: {0}, Time elapsed: {1}", stockCode, stopwatch.Elapsed.TotalMinutes));
            driver.Close();
            return listTrades;
        }

        private static void CrawlTrades(ChromeDriver driver, List<Trade> listTrades, string stockCode)
        {
            var divSoLenh = driver.FindElement(By.CssSelector("#__next > div.mx-auto.max-w-7xl.mt-4.px-4.lg\\:px-8 > div > main > div > div.grid.grid-cols-3.gap-4.w-full.max-xl\\:grid-cols-2.max-md\\:grid-cols-1.mt-4 > div:nth-child(1) > div:nth-child(2) > div.flex.flex-col.flex-1 > div.flex.flex-1.w-full > div > div.w-full.h-\\[250px\\] > div > div > div"));

            var childs = divSoLenh.FindElements(By.TagName("div"));

            foreach (IWebElement e in childs)
            {

                var index = e.GetAttribute("data-index");

                if (index != null && int.Parse(index) >= listTrades.Count)
                {
                    //Console.WriteLine("Index " + index);
                    var text = e.Text;
                    List<string> props = text.Split(Environment.NewLine).ToList();

                    var tradingTime = TimeOnly.Parse(props[0]);
                    DateTime dateTime = DateTime.Today.Add(tradingTime.ToTimeSpan());
                    var price = decimal.Parse(props[1]);
                    var change = decimal.Parse(props[2]);
                    var volume = int.Parse(props[3], NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

                    var side = props.Count > 4 ? (props[4] == "B" ? "S" : "B") : "AT";
                    var package = 1;
                    listTrades.Add(new Trade(index, dateTime, volume, price, side, stockCode, package));
                }
            }

            var scrollOriginSL = new WheelInputDevice.ScrollOrigin
            {
                Element = divSoLenh,
                XOffset = 0,
                YOffset = -50
            };
            new Actions(driver)
                .ScrollFromOrigin(scrollOriginSL, 0, 200)
                .Perform();
        }
    }
}
