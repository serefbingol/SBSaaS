using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SBSaaS.API.Auth;
using SBSaaS.API.Middleware;
using SBSaaS.Application.Interfaces;
using SBSaaS.Infrastructure;
using System.Globalization;
using System.Threading.RateLimiting;
using SBSaaS.API.Localization;
using Microsoft.AspNetCore.Localization.RequestCultureProviders;

var builder = WebApplication.CreateBuilder(args);

// 1. Add services to the container.
builder.Services.AddInfrastructure(builder.Configuration);

// Tenant Context – basit header temelli örnek (X-Tenant-Id)
builder.Services.AddScoped<ITenantContext, HeaderTenantContext>();

// JwtOptions
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<ITokenService, TokenService>();

// Localization
var locCfg = builder.Configuration.GetSection("Localization");
var defaultCulture = locCfg["DefaultCulture"] ?? "tr-TR";
var supported = locCfg.GetSection("SupportedCultures").Get<string[]>() ?? new[] { "tr-TR", "en-US", "de-DE" };

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddControllers()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

builder.Services.AddScoped<IRequestCultureProvider, TenantRequestCultureProvider>();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = supported.Select(c => new CultureInfo(c)).ToList();
    options.DefaultRequestCulture = new RequestCulture(defaultCulture);
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;

    // Çözüm sırası: Query → Cookie → Header → Tenant
    options.RequestCultureProviders = new IRequestCultureProvider[]
    {
        new QueryStringRequestCultureProvider(),
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider(),
        // Tenant en sonda fallback gibi davranır
        new ServiceRequestCultureProvider { CultureProvider = typeof(TenantRequestCultureProvider) }
    };
});


builder.Services.AddSingleton<IFormatService, FormatService>();
builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(o =>
{
    var cfg = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = cfg.Issuer,
        ValidAudience = cfg.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(cfg.SigningKey))
    };
})
.AddGoogle(opt =>
{
    opt.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
    opt.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
})
.AddMicrosoftAccount(opt =>
{
    opt.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"]!;
    opt.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"]!;
});

// Authorization – policy örnekleri
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantScoped", p => p.RequireClaim("tenant"));
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin","Owner"));
});

// Rate limiting (IP başına basit sabit pencere)
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("api", options =>
    {
        options.Window = TimeSpan.FromSeconds(1);
        options.PermitLimit = 20; // saniyede 20 istek
        options.QueueLimit = 0;
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 2. Build the application.
var app = builder.Build();

// 3. Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Localization middleware
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);

// Custom middleware
app.UseMiddleware<TenantMiddleware>();

// Auth middleware
app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

// Map endpoints
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithMetadata(new AllowAnonymousTenantAttribute());

// 4. Run the application.
app.Run();

// Make the implicit Program class public so it can be accessed by the test project.
public partial class Program { }