﻿using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Xml.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium;
using ScrawlDataIntraday.Common;
using ScrawlDataIntraday.Models;
using System.Diagnostics;
using Amazon.Auth.AccessControlPolicy;
using Polly;
using Policy = Polly.Policy;
using MassTransit;
using Quartz;
namespace ScrawlDataIntraday
{
    public class Worker : BackgroundService, IJob
    {
        private readonly ILogger<Worker> _logger;
        private readonly StockIntradayService _stockIntradayService;
        private readonly IBus _bus;
        private readonly IConfiguration _configuration;
        public Worker(ILogger<Worker> logger, StockIntradayService stockIntradayService, IBus bus, IConfiguration configuration)
        {
            _logger = logger;
            _stockIntradayService = stockIntradayService;
            _bus = bus;
            _configuration = configuration;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            await RunCrawlData();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }

                //await RunCrawlData();
            }
        }

        private async Task RunCrawlData()
        {
            var hour = DateTime.Now.Hour;
            if (hour < 14)
            {
                foreach (var stock in ConstStock.popularStocks)
                {
                    await _bus.Publish(new StockMessage(stock));
                }
            }
            else
            {
                List<string> stocks = new List<string>();
                stocks = _configuration.GetSection("Stocks").Get<List<string>>();
                foreach (var stock in stocks)
                {
                    await _bus.Publish(new StockMessage(stock));
                }
            }

        }
    }
}
