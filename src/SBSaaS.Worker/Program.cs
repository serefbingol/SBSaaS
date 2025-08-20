using SBSaaS.Worker;
using Minio;
using SBSaaS.Application.Interfaces;
using SBSaaS.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHostedService<Worker>();

// MinIO Client Registration
builder.Services.AddSingleton(sp => new MinioClient()
    .WithEndpoint(builder.Configuration["Minio:Endpoint"]!)
    .WithCredentials(builder.Configuration["Minio:AccessKey"]!, builder.Configuration["Minio:SecretKey"]!)
    .WithSSL(bool.TryParse(builder.Configuration["Minio:UseSSL"], out var ssl) && ssl)
    .Build());

// File Storage and Object Signer
builder.Services.AddScoped<IFileStorage, MinioFileStorage>();
builder.Services.AddScoped<IObjectSigner, MinioObjectSigner>();

// Antivirus Scanner
builder.Services.AddScoped<IAntivirusScanner, ClamAVScanner>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapControllers();

app.Run();
