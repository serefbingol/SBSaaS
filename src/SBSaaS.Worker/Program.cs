using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SBSaaS.Infrastructure;
using System;
using System.Threading.Tasks;
using SBSaaS.Application.Interfaces;
using SBSaaS.Worker.Services;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Quartz;
using SBSaaS.Worker.Jobs;


namespace SBSaaS.Worker
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables()
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .CreateLogger();

            try
            {
                Log.Information("Starting SBSaaS.Worker service.");

                var host = Host.CreateDefaultBuilder(args)
                    .UseSerilog()
                    .ConfigureServices((hostContext, services) =>
                    {
                        // Worker'a özel, her mesaj işleme kapsamında (scope) yeniden oluşturulacak
                        // ve içeriği dinamik olarak doldurulacak context servisleri.
                        services.AddScoped<ICurrentUser, WorkerUserContext>();
                        services.AddScoped<ITenantContext, WorkerTenantContext>();

                        // Altyapı servislerini (DbContext, MinIO, ClamAV, vb.) kaydet.
                        services.AddInfrastructure(hostContext.Configuration);

                        // Faz 4: Periyodik görevler için Quartz.NET'i yapılandır
                        services.AddQuartz(q =>
                        {
                            // 1. DailyAggregationJob: Ham olayları günlük olarak toplar.
                            // Her gece 01:00'de çalışacak şekilde zamanla.
                            var dailyJobKey = new JobKey(nameof(DailyAggregationJob));
                            q.AddJob<DailyAggregationJob>(opts => opts.WithIdentity(dailyJobKey));
                            q.AddTrigger(opts => opts
                                .ForJob(dailyJobKey)
                                .WithIdentity($"{nameof(DailyAggregationJob)}-trigger")
                                .WithCronSchedule("0 0 1 * * ?")); // Cron: Her gün saat 01:00:00

                            // 2. PeriodAggregationJob: Günlük özetleri fatura dönemine göre toplar.
                            // Her gece 02:00'de, yani günlük toplama bittikten sonra çalışacak şekilde zamanla.
                            var periodJobKey = new JobKey(nameof(PeriodAggregationJob));
                            q.AddJob<PeriodAggregationJob>(opts => opts.WithIdentity(periodJobKey));
                            q.AddTrigger(opts => opts
                                .ForJob(periodJobKey)
                                .WithIdentity($"{nameof(PeriodAggregationJob)}-trigger")
                                .WithCronSchedule("0 0 2 * * ?")); // Cron: Her gün saat 02:00:00

                            // 3. PeriodClosingJob: Biten dönemleri kapatır ve aşımları hesaplar.
                            // Her gece 03:00'te, yani dönemsel toplama da bittikten sonra çalışacak şekilde zamanla.
                            var closingJobKey = new JobKey(nameof(PeriodClosingJob));
                            q.AddJob<PeriodClosingJob>(opts => opts.WithIdentity(closingJobKey));
                            q.AddTrigger(opts => opts
                                .ForJob(closingJobKey)
                                .WithIdentity($"{nameof(PeriodClosingJob)}-trigger")
                                .WithCronSchedule("0 0 3 * * ?")); // Cron: Her gün saat 03:00:00
                        });

                        // Quartz.NET'in bir IHostedService olarak çalışmasını sağla
                        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

                        // Dağıtık izleme (Distributed Tracing) için OpenTelemetry yapılandırması.
                        // Bu, bir isteğin API'den Worker'a olan yolculuğunu takip etmeyi sağlar.
                        services.AddOpenTelemetry()
                            .WithTracing(builder => builder
                                .AddSource("SBSaaS.Worker") // Worker'a özel aktiviteler için kaynak adı.
                                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("SBSaaS.Worker"))
                                .AddHttpClientInstrumentation() // MinIO ve ClamAV gibi servislere yapılan HTTP çağrılarını izler.                                
                                .AddEntityFrameworkCoreInstrumentation(opt => opt.SetDbStatementForText = true) // EF Core komutlarını izler.
                                // .AddRabbitMQInstrumentation() // RabbitMQ'dan mesaj alma işlemlerini izler. (Not available in OpenTelemetry .NET)
                                .AddOtlpExporter(options =>
                                {
                                    // İzleme verilerini toplayıcıya (collector) gönderir.
                                    options.Endpoint = new Uri(hostContext.Configuration["OpenTelemetry:OtlpExporter:Endpoint"] ?? "http://localhost:4317");
                                })
                            );

                        // RabbitMQ kuyruğunu dinleyen ana worker servisini kaydet.
                        services.AddHostedService<FileScanConsumerWorker>();
                    })
                    .Build();

                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "SBSaaS.Worker service terminated unexpectedly.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}