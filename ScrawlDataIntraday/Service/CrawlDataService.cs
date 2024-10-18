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
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using static System.Net.Mime.MediaTypeNames;
using System.Xml.Linq;

namespace ScrawlDataIntraday.Service
{
    public class CrawlDataService
    {
        private readonly ILogger<Worker> _logger;
        private readonly StockIntradayService _stockIntradayService;
        private readonly IBus _bus;
        private readonly IConfiguration _configuration;
        private readonly int _retryTimes;
        public List<string> _errorStocks = new List<string>();
        public bool _isRunning = true;
        
        public CrawlDataService(ILogger<Worker> logger, StockIntradayService stockIntradayService, IBus bus,
            IConfiguration configuration)
        {
            _logger = logger;
            _stockIntradayService = stockIntradayService;
            _bus = bus;
            _configuration = configuration;
            _retryTimes = configuration.GetSection("RetryTime").Get<int>();
            _isRunning = true;
        }
        public async Task Run(string stockCode)
        {
            var rs = new List<Trade>();
             try
            {
                var retryPolicy = Policy
                        .Handle<Exception>()
                        .WaitAndRetryAsync(
                            retryCount: _retryTimes,
                            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(5),
                            onRetryAsync: async (exception, timespan, retryAttempt, context) =>
                            {
                                _logger.LogError($"Retry attempt {retryAttempt} after {timespan.TotalSeconds} seconds due to:      {exception.Message}");
                                _logger.LogError(exception, "Exception when retry: ");
                                _logger.LogError("Error Stock: " + context["stock"]);
                                if (retryAttempt == _retryTimes)
                                {
                                    _errorStocks.Add(stockCode);
                                }
                            });
                rs = await retryPolicy.ExecuteAsync(async (ctx) =>
                {
                    ctx["stock"] = stockCode;
                    return await GetStockIntraday(stockCode);
                }, new Context());
            } catch (Exception ex)
            {
                _logger.LogError(ex,"Get stock error");
            }

            await SaveTradesToMongo(rs);

            var allStock = _configuration.GetSection("Stocks").Get<List<string>>() ?? new List<string>();
            var ind = allStock?.IndexOf(stockCode);
           
            _logger.LogInformation(string.Format("Done {0}, Processed: {1}%", stockCode, (Math.Round((double)ind / allStock.Count * 100))));
            _logger.LogInformation("Error Stock: " + string.Join(",", _errorStocks));
            if (ind == allStock.Count - 1)
            {
                _logger.LogInformation("Done all");
                //_logger.LogInformation("Error Stock: " + string.Join(",", _errorStocks));
                _isRunning = false;
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
            if (_configuration.GetSection("SourceCrawSite").Value == "Fireant")
            {
                return await GetStockIntradayUseFireant(stockCode);
            }
            return await GetStockIntradayUseStockBiz(stockCode);
        }

        private async Task<List<Trade>> GetStockIntradayUseFireant(string stockCode)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            ChromeDriver driver = null;

            try
            {
                ChromeOptions options = new ChromeOptions();
                options.AddArgument("--headless"); // Run Chrome in headless mode
                options.AddArgument("--disable-gpu"); // Disable GPU hardware acceleration
                options.AddArgument("--window-size=1920,1080"); // Set window size if needed
                options.AddArgument("--window-position=-2400,-2400"); // Set window size if needed
                options.AddArgument("--no-sandbox"); // Bypass OS security model (use with caution)

                // Initialize the ChromeDriver with options
                driver = new ChromeDriver(options);

                //var driver = new ChromeDriver();
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);

                driver.Navigate().GoToUrl("https://fireant.vn/dashboard/content/symbols/" + stockCode);

                IWebElement buttonDesau = driver.FindElement(By.CssSelector("body > div:nth-child(6) > div > div.bp5-dialog-container.bp5-overlay-content.bp5-overlay-appear-done.bp5-overlay-enter-done > div > div.bp5-dialog-footer > div > button:nth-child(1) > span"));

                WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
                wait.Until(ExpectedConditions.ElementToBeClickable(buttonDesau));
                new Actions(driver).Click(buttonDesau).Perform();

                var soLenhPath = By.XPath("//span[text()='Sổ lệnh']");
                wait.Until(ExpectedConditions.ElementToBeClickable(soLenhPath));

                var spanElement = driver.FindElement(soLenhPath);
                new Actions(driver)
                    .Click(spanElement)
                    .Perform();

                Thread.Sleep(3000);
                var listTrades = new List<Trade>();
                int tradeCount = listTrades.Count;
                for (int i = 0; i < 10000; i++)
                {
                    try
                    {
                        CrawlTradesFireant(driver, listTrades, stockCode);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Exception " + stockCode);
                        driver.Close();
                        driver.Quit();
                        throw e;
                    }

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
                driver.Quit();
                return listTrades;
            } catch (Exception e)
            {
                _logger.LogError(e, "Exception " + stockCode);
                driver.Close();
                driver.Quit();
                throw e;
            }
        }

        private static void CrawlTradesFireant(ChromeDriver driver, List<Trade> listTrades, string stockCode)
        {
            var divSolenhPath = By.XPath("/html/body/div[2]/div/div[3]/div/div[2]/div/div[2]/div[2]/div/div[2]/div/div[2]/div/div[3]");
            
            
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            wait.Until(ExpectedConditions.ElementToBeClickable(divSolenhPath));

            var divSoLenh = driver.FindElement(divSolenhPath);
            var childs = divSoLenh.FindElements(By.CssSelector(".list-row"));
            var heightSize = childs.FirstOrDefault()?.Size.Height ?? 33;

            for (int i = 0; i < childs.Count; i++)
            {
                var e = childs[i];
                //Console.WriteLine($"Index {i.ToString()} {e.Text} ");
                var top = 0;
                int.TryParse(e.GetCssValue("top").Split("px")[0], out top);
                var index = (int)Math.Round((decimal)(top / heightSize), 0);

                if (index != null && (index) >= listTrades.Count)
                {
                    var text = ((IJavaScriptExecutor)driver).ExecuteScript("return arguments[0].innerText;", e);
                    //Console.WriteLine($"Index {i.ToString()} {text} ");
                    
                    var props = text.ToString().Split(Environment.NewLine).ToList();
                    if (string.IsNullOrEmpty(props[0]) && string.IsNullOrEmpty(props[1])) { 
                        continue; 
                    }
                    var tradingTime = TimeOnly.Parse(props[0]);
                    DateTime dateTime = DateTime.Today.Add(tradingTime.ToTimeSpan());
                    var price = decimal.Parse(props[1]);
                    var change = decimal.Parse(props[2]);
                    var volume = int.Parse(props[3], NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

                    var side = props.Count > 4 ? (props[4] == "B" ? "S" : "B") : "AT";
                    var package = 1;
                    listTrades.Add(new Trade(index.ToString(), dateTime, volume, price, side, stockCode, package, change));
                    
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

        private async Task<List<Trade>> GetStockIntradayUseStockBiz(string stockCode)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            ChromeDriver driver = null;

            ChromeOptions options = new ChromeOptions();
            options.AddArgument("--headless"); // Run Chrome in headless mode
            options.AddArgument("--disable-gpu"); // Disable GPU hardware acceleration
            options.AddArgument("--window-size=1920,1080"); // Set window size if needed
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
                try
                {
                    CrawlTrades(driver, listTrades, stockCode);
                    if (listTrades.Count > 0 && listTrades.Where(t => t.Side == "AT")?.Count() > 50)
                    {
                        throw new Exception("Craw false side");
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Exception " + stockCode);
                    driver.Close();
                    driver.Quit();
                    throw e;
                }

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
            driver.Quit();
            return listTrades;
        }

        private static void CrawlTrades(ChromeDriver driver, List<Trade> listTrades, string stockCode)
        {

            var divSoLenh = driver.FindElement(By.CssSelector("#__next > div.mx-auto.max-w-7xl.mt-4.px-4.lg\\:px-8 > div > main > div > div.grid.grid-cols-3.gap-4.w-full.max-xl\\:grid-cols-2.max-md\\:grid-cols-1.mt-4 > div:nth-child(1) > div:nth-child(2) > div.flex.flex-col.flex-1 > div.flex.flex-1.w-full > div > div.w-full.h-\\[250px\\] > div > div > div"));
            
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector("#__next > div.mx-auto.max-w-7xl.mt-4.px-4.lg\\:px-8 > div > main > div > div.grid.grid-cols-3.gap-4.w-full.max-xl\\:grid-cols-2.max-md\\:grid-cols-1.mt-4 > div:nth-child(1) > div:nth-child(2) > div.flex.flex-col.flex-1 > div.flex.flex-1.w-full > div > div.w-full.h-\\[250px\\] > div > div > div")));
            
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
                    listTrades.Add(new Trade(index, dateTime, volume, price, side, stockCode, package, change));
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
