using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Minio;
using SBSaaS.Domain.Entities;
using SBSaaS.Infrastructure.Persistence;
using SBSaaS.Infrastructure.Storage;
using SBSaaS.Application.Interfaces;
using SBSaaS.Infrastructure.Audit;
using Microsoft.Extensions.Configuration;

namespace SBSaaS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Tenant ve Audit mekanizmaları için HttpContext'e erişim sağlar.
        services.AddHttpContextAccessor();

        // TenantId'yi HTTP Header'dan okuyan servisi kaydet.
        services.AddScoped<ITenantContext, HeaderTenantContext>();

        // Audit log'ları oluşturan interceptor'ı kaydet.
        services.AddScoped<AuditSaveChangesInterceptor>();

        // DbContext'i, interceptor'ı kullanacak şekilde fabrika metoduyla kaydet.
        // Bu, önceki yinelenen kaydı düzeltir.
        services.AddDbContext<SbsDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(config.GetConnectionString("Postgres"));
            opt.EnableSensitiveDataLogging(false); // Üretimde false olmalı
            opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.User.RequireUniqueEmail = false; // Çok kiracılı yapıda e-posta tenant bazında unique olmalı, global değil.
        })
        .AddEntityFrameworkStores<SbsDbContext>()
        .AddDefaultTokenProviders();
       
        // Minio.MinioClient'ı DI konteynerine ekleyin.
        // Singleton yaşam süresi ile tüm uygulama boyunca tek bir MinioClient örneği kullanılacaktır.
        services.AddSingleton<IMinioClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var endpoint = config.GetValue<string>("Minio:Endpoint");
            var accessKey = config.GetValue<string>("Minio:AccessKey");
            var secretKey = config.GetValue<string>("Minio:SecretKey");
            var useSsl = config.GetValue<bool?>("Minio:UseSSL") ?? true; // Varsayılan olarak SSL kullan

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            {
                throw new InvalidOperationException("Minio yapılandırma bilgileri (endpoint, access key, secret key) 'Minio' bölümü altında appsettings.json dosyasında bulunamadı.");
            }

            var minioClient = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey);

            if (useSsl) minioClient.WithSSL();
            
            return minioClient.Build();
        });
        services.AddScoped<IFileStorage, MinioFileStorage>();

        return services;
    }
}