using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SBSaaS.Infrastructure.Persistence;
using System.Linq;

namespace SBSaaS.API.IntegrationTests.Helpers;

/// <summary>
/// Entegrasyon testleri için servisleri geçersiz kılmak (override) amacıyla kullanılan
/// özel WebApplicationFactory sınıfı. Bu fabrika, üretim veritabanını
/// bellek-içi (in-memory) bir veritabanıyla değiştirir.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 'builder', test edilen sistemin (SUT - System Under Test) IWebHostBuilder'ıdır.
        // Bunu servisleri, uygulama yapılandırmasını vb. düzenlemek için kullanabiliriz.
        builder.ConfigureServices(services =>
        {
            // 1. DbContext için orijinal servis tanımlayıcısını bul.
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<SbsDbContext>));

            // 2. Bulunursa, servis koleksiyonundan kaldır.
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            // 3. Bellek-içi veritabanı kullanan yeni bir DbContext kaydı ekle.
            // Her fabrika örneği için yeni bir veritabanı oluşturulur, bu da test izolasyonunu sağlar.
            services.AddDbContext<SbsDbContext>(options =>
            {
                options.UseInMemoryDatabase($"InMemoryDbForTesting-{System.Guid.NewGuid()}");
            });

            // Diğer sahte servisleri de buraya ekleyebilirsiniz. Örneğin:
            // services.AddSingleton<IEmailService>(new Mock<IEmailService>().Object);
        });
    }
}