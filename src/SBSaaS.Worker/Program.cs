using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SBSaaS.Infrastructure;
using System;
using System.Threading.Tasks;
using SBSaaS.Application.Interfaces;
using SBSaaS.Worker.Services;
using OpenTelemetry.Resources; // Added
using OpenTelemetry.Trace; // Added
using OpenTelemetry.Instrumentation.Runtime; // Added for runtime metrics
using OpenTelemetry.Instrumentation.AspNetCore; // Added for ASP.NET Core instrumentation
// Removed: using OpenTelemetry.Instrumentation.EntityFrameworkCore; 
using OpenTelemetry.Instrumentation.Http; // Added for HTTP client instrumentation
using OpenTelemetry.Exporter.OpenTelemetryProtocol; // Added for OTLP exporter
// Removed: using OpenTelemetry.Instrumentation.Npgsql; 
// Removed: using OpenTelemetry.Instrumentation.RabbitMQ; 

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
                        // Register Worker-specific TenantContext first
                        services.AddScoped<ITenantContext, WorkerTenantContext>();

                        // Add infrastructure services (DbContext, Repositories, MinIO, RabbitMQ Publisher etc.)
                        services.AddInfrastructure(hostContext.Configuration);

                        // Add OpenTelemetry Tracing
                        services.AddOpenTelemetry()
                            .WithTracing(builder => builder
                                .AddSource("SBSaaS.Worker") // Name your service
                                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("SBSaaS.Worker"))
                                .AddHttpClientInstrumentation() // Trace outgoing HTTP calls
                                .AddAspNetCoreInstrumentation() // Trace incoming HTTP calls (if any, for web workers)
                                //.AddEntityFrameworkCoreInstrumentation() // Removed
                                //.AddNpgsql() // Removed
                                //.AddRabbitMQInstrumentation() // Removed
                                .AddOtlpExporter(options =>
                                {
                                    options.Endpoint = new Uri(hostContext.Configuration["OpenTelemetry:OtlpExporter:Endpoint"] ?? "http://localhost:4317");
                                })
                            );

                        // Register the main worker service that listens to the RabbitMQ queue
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