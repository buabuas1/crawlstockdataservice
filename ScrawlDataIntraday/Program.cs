using ScrawlDataIntraday;
using ScrawlDataIntraday.Models;
using Serilog;
using MassTransit;
using Microsoft.Extensions.Configuration;
using ScrawlDataIntraday.Service;
using Quartz;

namespace ScrawlDataIntraday
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var builder = Host.CreateDefaultBuilder(args);

                builder.ConfigureServices((hostContext, services) =>
                {
                    services.Configure<MongoDatabaseSettings>(
                    hostContext.Configuration.GetSection("StockStoreDatabase"));

                    services.AddMassTransit(x =>
                    {
                        x.AddConsumer<StockConsumer>();

                        x.UsingInMemory((context, cfg) =>
                        {
                            cfg.ConfigureEndpoints(context);
                            cfg.ConcurrentMessageLimit = int.Parse(hostContext.Configuration.GetSection("ComsumerSize").Value ?? "1");
                        });
                    });

                    services.AddSingleton<StockIntradayService>();
                    services.AddSingleton<CrawlDataService>();

                    Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(hostContext.Configuration)
                    .CreateLogger();

                    string time = hostContext.Configuration.GetSection("CronTime").Value;
                    Console.WriteLine("CronTime: " + time);
                    services.AddQuartz(q =>
                    {
                        // Create a job
                        var jobKey = new JobKey("DailyJob");
                        q.AddJob<Worker>(opts => opts.WithIdentity(jobKey));

                        // Create a trigger to fire at 15:00 every day
                        q.AddTrigger(opts => opts
                            .ForJob(jobKey) // Link to the DailyJob
                            .WithIdentity("DailyJobTrigger") // Give the trigger a unique name
                            .WithCronSchedule(time ?? "0 0 15 * * ?") // Cron expression for 15:00 every day
                        );
                    });

                    // Add Quartz.NET hosted service
                    services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

                    services.AddHostedService<Worker>();

                });
                
                builder.ConfigureLogging((hostingContext, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddSerilog();
                });
                var host = builder.Build();
                host.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Service corrupted");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
    


