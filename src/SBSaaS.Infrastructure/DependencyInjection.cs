using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Entities;
using SBSaaS.Infrastructure.Audit;
using SBSaaS.Infrastructure.Identity;
using SBSaaS.Infrastructure.Persistence;
using SBSaaS.Infrastructure.Storage;

namespace SBSaaS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Audit Interceptor ve TenantContext gibi servislerin HttpContext'e erişmesi gerekebilir.
        services.AddHttpContextAccessor();

        // A2 - Audit Logging
        services.AddScoped<AuditSaveChangesInterceptor>();

        // A1 - Veri Katmanı
        services.AddDbContext<SbsDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(config.GetConnectionString("Postgres"));
            // Üretimde hassas verilerin loglanmasını engellemek önemlidir.
            opt.EnableSensitiveDataLogging(false);
            // Audit interceptor'ını DbContext'e ekle
            opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        // A3 - Identity
        services.AddIdentity<ApplicationUser, IdentityRole>(o => { o.User.RequireUniqueEmail = true; })
            .AddEntityFrameworkStores<SbsDbContext>()
            .AddDefaultTokenProviders();

        // B1 & A5 - Dosya Depolama (MinIO)
        services.AddSingleton(_ => new MinioClient()
            .WithEndpoint(config["Minio:Endpoint"]!)
            .WithCredentials(config["Minio:AccessKey"]!, config["Minio:SecretKey"]!)
            .WithSSL(bool.TryParse(config["Minio:UseSSL"], out var ssl) && ssl)
            .Build());
        services.AddScoped<IFileStorage, MinioFileStorage>();
        // A5 - Presigned URL ve Politika servisleri
        services.AddScoped<IObjectSigner, MinioObjectSigner>();
        services.AddScoped<IUploadPolicy, UploadPolicy>();

        return services;
    }
}