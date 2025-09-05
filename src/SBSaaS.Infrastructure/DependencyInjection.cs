using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Entities;
using SBSaaS.Infrastructure.Audit;
using SBSaaS.Infrastructure.Antivirus;
using SBSaaS.Infrastructure.Messaging;
using SBSaaS.Infrastructure.Persistence;
using SBSaaS.Infrastructure.Storage;
using Microsoft.Extensions.Localization; // Eklendi
using Microsoft.Extensions.Caching.Memory; // Eklendi
using SBSaaS.Infrastructure.Localization;
using SBSaaS.Infrastructure.Services;
using SBSaaS.Domain.Entities.Auth; // Eklendi

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
        services.AddDbContextFactory<SbsDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(config.GetConnectionString("Postgres"));
            // Üretimde hassas verilerin loglanmasını engellemek önemlidir.
            opt.EnableSensitiveDataLogging(false);
            // Audit interceptor'ını DbContext'e ekle
            opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        // A3 - Identity
        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.Password.RequiredLength = 8;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            o.User.RequireUniqueEmail = true;
            o.SignIn.RequireConfirmedEmail = false; // üretimde true önerilir
        })
            .AddEntityFrameworkStores<SbsDbContext>()
            .AddDefaultTokenProviders();

        // B1 & A5 - Dosya Depolama (MinIO) ve Politika Yönetimi
        // appsettings.json'daki "Minio" bölümünü MinioOptions sınıfına bağla.
        // Bu, IOptions<MinioOptions>'ı enjekte edilebilir hale getirir.
        services.Configure<MinioOptions>(config.GetSection(MinioOptions.SectionName));

        // MinioClient'ı singleton olarak kaydet.
        // Bu, uygulama boyunca aynı istemcinin yeniden kullanılmasını sağlar ve
        // yapılandırmayı IOptions üzerinden güvenli bir şekilde alır.
        services.AddSingleton<IMinioClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
            return new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithSSL(options.UseSSL)
                .Build();
        });
        // services.AddScoped<IFileStorage, MinioFileStorage>(); // Bu satır, doğrudan dosya yükleme akışı için gereklidir.
        services.AddScoped<IObjectSigner, MinioObjectSigner>();
        services.AddScoped<IUploadPolicy, UploadPolicy>();

        // Mesajlaşma Sistemi (RabbitMQ)
        services.Configure<RabbitMqOptions>(config.GetSection("RabbitMQ"));
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

        // Antivirüs Tarama Servisi (ClamAV)
        services.Configure<ClamAVOptions>(config.GetSection("ClamAV"));
        services.AddScoped<IAntivirusScanner, ClamAVScanner>();

        // Lokalizasyon Servisleri (Veritabanı Tabanlı)
        services.AddMemoryCache(); // IMemoryCache ekleniyor
        services.AddSingleton<IStringLocalizerFactory, DbStringLocalizerFactory>(); // DbStringLocalizerFactory ekleniyor

        // Data Protection anahtarlarını veritabanında saklamak için yapılandırma.
        // Bu, anahtarların kalıcı olmasını ve birden fazla instance arasında paylaşılmasını sağlar.
        services.AddDataProtection()
            .PersistKeysToDbContext<SbsDbContext>();

        // Faz 4 - Kullanım Ölçümleme (Metering) Servisi
        services.AddScoped<IMeteringService, MeteringService>();

        return services;
    }
}
