using ScrawlDataIntraday;
using ScrawlDataIntraday.Models;
using Serilog;
using MassTransit;
using Microsoft.Extensions.Configuration;
using ScrawlDataIntraday.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<MongoDatabaseSettings>(
    builder.Configuration.GetSection("StockStoreDatabase"));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<StockConsumer>();

    x.UsingInMemory((context, cfg) =>
    {
        cfg.ConfigureEndpoints(context);
        cfg.ConcurrentMessageLimit = int.Parse(builder.Configuration.GetSection("ComsumerSize").Value ?? "1");
    });
});

builder.Services.AddSingleton<StockIntradayService>();
builder.Services.AddSingleton<CrawlDataService>();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

builder.Services.AddHostedService<Worker>();


var host = builder.Build();
host.Run();
