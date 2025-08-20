using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Servisleri container'a ekleyin.

// C1 dokümanına göre yerelleştirme servisleri
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

// Adım 2'de WebApp projesine taşınan formatlama servisi
builder.Services.AddScoped<SBSaaS.WebApp.Services.IFormatService, SBSaaS.WebApp.Services.FormatService>();

// C1 dokümanına göre MVC ve yerelleştirme desteği.
// Projeniz Razor Pages kullanıyorsa .AddRazorPages() olarak değiştirebilirsiniz.
builder.Services
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

// Desteklenen kültürleri yapılandır
var supported = new[] { "tr-TR", "en-US", "de-DE" };
var cultures = supported.Select(c => new CultureInfo(c)).ToList();

builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture("tr-TR");
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
    o.RequestCultureProviders = new IRequestCultureProvider[]
    {
        new QueryStringRequestCultureProvider(),
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    };
});

var app = builder.Build();

// HTTP request pipeline'ını yapılandırın.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// C1 dokümanına göre RequestLocalization middleware'i
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// C1 dokümanına göre varsayılan controller rotası
app.MapDefaultControllerRoute();

app.Run();
