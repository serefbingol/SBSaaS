using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;
using SBSaaS.API.Middleware;
using SBSaaS.Infrastructure;
using SBSaaS.Application.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// 1. Add services to the container.
builder.Services.AddInfrastructure(builder.Configuration);

// Tenant Context – basit header temelli örnek (X-Tenant-Id)
builder.Services.AddScoped<ITenantContext, HeaderTenantContext>();

// Localization – varsayılan tr-TR
var supportedCultures = new[] { "tr-TR", "en-US", "de-DE" };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
    options.DefaultRequestCulture = new RequestCulture("tr-TR");
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
});

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources"); // Resource dosyalarının yolu

builder.Services.AddAuthentication()
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
app.UseRequestLocalization();

// Custom middleware
app.UseMiddleware<TenantMiddleware>();

// Auth middleware
app.UseAuthentication();
app.UseAuthorization();

// Map endpoints
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithMetadata(new AllowAnonymousTenantAttribute());

// 4. Run the application.
app.Run();

// Make the implicit Program class public so it can be accessed by the test project.
public partial class Program { }
