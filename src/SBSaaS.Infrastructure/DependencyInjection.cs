using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddDbContext<SbsDbContext>(opt =>
        {
            opt.UseNpgsql(config.GetConnectionString("Postgres"));
            opt.EnableSensitiveDataLogging(false);
        });

        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<SbsDbContext>()
        .AddDefaultTokenProviders();
       
        // Minio.MinioClient'ı DI konteynerine ekleyin.
        // Singleton yaşam süresi ile tüm uygulama boyunca tek bir MinioClient örneği kullanılacaktır.
        services.AddSingleton<MinioClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var endpoint = config.GetValue<string>("Minio:Endpoint");
            var accessKey = config.GetValue<string>("Minio:AccessKey");
            var secretKey = config.GetValue<string>("Minio:SecretKey");

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            {
                throw new InvalidOperationException("Minio yapılandırma bilgileri (endpoint, access key, secret key) 'Minio' bölümü altında appsettings.json dosyasında bulunamadı.");
            }

            return (MinioClient)new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .Build();
        });
        // IFileStorage'ı kaydedin. Bu, MinioClient'ın artık kullanılabilir olmasını sağlar.
        services.AddScoped<IFileStorage, MinioFileStorage>();
      
        services.AddScoped<AuditSaveChangesInterceptor>();
        // Interceptor'ı servis olarak kaydet
        services.AddScoped<AuditSaveChangesInterceptor>();

        // DbContext'i factory overload ile kur ve interceptor'ı ekle
        services.AddDbContext<SbsDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(config.GetConnectionString("Postgres"));
            opt.EnableSensitiveDataLogging(false);
            opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        return services;
    }
}