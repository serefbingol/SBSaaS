Bu belge **D2 – Logging & Monitoring** iş paketinin uçtan uca uygulamasını içerir. Hedef: **yapısal loglama** (Serilog), **izleme** (OpenTelemetry traces/metrics/logs), **correlation** (tenant/user/request), ve lokalde **Grafana + Loki + Tempo** ile görünürlük; prod için **OTLP/collector** üzerinden dış APM’e çıkış.

---

# 0) DoD – Definition of Done

- API & WebApp’te **Serilog** kurulu, JSON (yapısal) log yazıyor.
- Log enriched fields: `tenantId`, `userId`, `correlationId`, `clientIp`, `path`, `status`, `durationMs`.
- **PII maskeleme** kuralları uygulandı (email/phone vb.).
- **OpenTelemetry** ile HTTP/EFCore/ASP.NET otomatik **traces & metrics** toplanıyor.
- Lokalde **Grafana + Loki + Tempo** stack ayağa kalkıyor, log/trace korelasyonu çalışıyor.
- Prod’da **OTLP** ile collector/APM’e export ayarı yapılmış.

---

# 1) NuGet Paketleri

```bash
# API & WebApp
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Serilog.AspNetCore
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Serilog.Sinks.Console
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Serilog.Enrichers.ClientInfo
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Serilog.Enrichers.Environment
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Serilog.Enrichers.Process
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Serilog.Enrichers.Thread
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Serilog.Expressions

# OpenTelemetry
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package OpenTelemetry.Extensions.Hosting
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package OpenTelemetry.Exporter.OpenTelemetryProtocol
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package OpenTelemetry.Instrumentation.AspNetCore
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package OpenTelemetry.Instrumentation.Http
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package OpenTelemetry.Instrumentation.EntityFrameworkCore
```

> WebApp için de aynı paketler (AspNetCore/Http) yeterli.

---

# 2) appsettings – Serilog yapılandırması (API)

``

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Enrichers.Environment" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft": "Warning", "System": "Warning", "Microsoft.Hosting.Lifetime": "Information" }
    },
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId", "WithProcessId" ],
    "WriteTo": [ { "Name": "Console", "Args": { "formatter": "Serilog.Formatting.Compact.RenderedCompactJsonFormatter, Serilog.Formatting.Compact" } } ]
  }
}
```

> Prod’da sink’i OTLP/logs veya Elasticsearch/Loki’ye yönlendirebilirsiniz (collector üzerinden).

---

# 3) Program.cs – Serilog + OTel (API)

`` (özet)

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog bootstrap
Log.Logger = new LoggerConfiguration()
   .ReadFrom.Configuration(builder.Configuration)
   .CreateLogger();
builder.Host.UseSerilog();

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "SBSaaS.API", serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation(opt => { opt.RecordException = true; opt.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"); })
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(o => { o.SetDbStatementForText = true; })
        .AddSource("SBSaaS") // manuel ActivitySource için
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();

// Request logging (Serilog)
app.Use(async (ctx, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    await next();
    sw.Stop();
    var tenantId = ctx.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    var userId = ctx.User?.Identity?.IsAuthenticated == true ? ctx.User.FindFirst("sub")?.Value ?? ctx.User.Identity?.Name : null;
    Log.ForContext("tenantId", tenantId)
       .ForContext("userId", userId)
       .ForContext("path", ctx.Request.Path.Value)
       .ForContext("status", ctx.Response.StatusCode)
       .ForContext("durationMs", sw.ElapsedMilliseconds)
       .Information("HTTP {Method} {Path} responded {Status} in {Elapsed} ms", ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
});

app.MapGet("/health", () => Results.Ok(new { ok = true }));
app.Run();
```

> `Serilog.AspNetCore` içinde `UseSerilogRequestLogging()` da kullanılabilir; burada custom alan eklemek için manuel örnek gösterildi.

---

# 4) PII Maskeleme & Log Filter

**Amaç:** Audit’te olduğu gibi e-posta/telefon vb. PII alanları loglarda maskelemek.

``

```csharp
using Serilog.Core;
using Serilog.Events;

public class PiiDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        result = default!;
        if (value is string s && (s.Contains("@") || s.StartsWith("+")))
        { result = new ScalarValue("***"); return true; }
        return false;
    }
}
```

**Kayıt**

```csharp
Log.Logger = new LoggerConfiguration()
  .Destructure.With<PiiDestructuringPolicy>()
  .ReadFrom.Configuration(builder.Configuration)
  .CreateLogger();
```

> Daha hassas kontrol için regex ve alan adı bazlı maskeleme yapılabilir (örn. `Email`, `Phone` isimli property’ler).

---

# 5) Manuel Trace – ActivitySource

Bazı kritik iş akışlarında manuel span açın:

```csharp
using System.Diagnostics;
static readonly ActivitySource Act = new("SBSaaS");

using var act = Act.StartActivity("Billing:CreateInvoice");
act?.SetTag("tenantId", tenantId);
act?.SetTag("subscriptionId", subId);
```

---

# 6) EF Core & Npgsql Log Seviyeleri

`appsettings.json` içinde gürültüyü azaltın:

```json
{
  "Serilog": { "MinimumLevel": { "Override": { "Microsoft.EntityFrameworkCore.Database.Command": "Warning", "Npgsql": "Warning" } } }
}
```

---

# 7) Lokal Gözlemlenebilirlik (Grafana + Loki + Tempo + OTel Collector)

**Klasör**: `observability/`

``

```yaml
version: "3.9"
services:
  otel-collector:
    image: otel/opentelemetry-collector:0.104.0
    command: ["--config=/etc/otel-collector-config.yaml"]
    volumes:
      - ./otel-collector-config.yaml:/etc/otel-collector-config.yaml:ro
    ports: ["4317:4317", "4318:4318"]

  loki:
    image: grafana/loki:2.9.6
    ports: ["3100:3100"]
    command: ["-config.file=/etc/loki/local-config.yaml"]
    volumes:
      - ./loki-config.yaml:/etc/loki/local-config.yaml:ro

  promtail:
    image: grafana/promtail:2.9.6
    volumes:
      - /var/log:/var/log
      - ./promtail-config.yaml:/etc/promtail/config.yaml:ro
    command: ["--config.file=/etc/promtail/config.yaml"]

  tempo:
    image: grafana/tempo:2.5.0
    ports: ["3200:3200"]
    command: ["-config.file=/etc/tempo.yaml"]
    volumes:
      - ./tempo.yaml:/etc/tempo.yaml:ro

  grafana:
    image: grafana/grafana:10.4.5
    ports: ["3000:3000"]
    volumes:
      - ./grafana/provisioning:/etc/grafana/provisioning
```

`` (API/WEB → OTLP → Tempo/Loki)

```yaml
receivers:
  otlp:
    protocols: { grpc: {}, http: {} }

exporters:
  otlp/tempo:
    endpoint: http://tempo:4317
    tls: { insecure: true }
  logging:
    loglevel: warn

processors:
  batch: {}
  memory_limiter: { check_interval: 5s, limit_mib: 400 }

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [otlp/tempo, logging]
```

`` (Serilog JSON → Loki)

```yaml
server: { http_listen_port: 9080, grpc_listen_port: 0 }
positions: { filename: /tmp/positions.yaml }
clients:
  - url: http://loki:3100/loki/api/v1/push
scrape_configs:
  - job_name: sbsaas
    static_configs:
      - targets: [localhost]
        labels:
          job: sbsaas
          __path__: /var/lib/docker/containers/*/*-json.log
```

**Grafana provisioning** (Loki & Tempo datasources) – `observability/grafana/provisioning/datasources/datasources.yaml`

```yaml
apiVersion: 1
datasources:
  - name: Loki
    type: loki
    url: http://loki:3100
  - name: Tempo
    type: tempo
    url: http://tempo:3200
```

**Çalıştırma**

```bash
cd observability
docker compose -f docker-compose.obs.yml up -d
```

> API/WEB için `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` (gRPC) veya `http://localhost:4318` (HTTP) environment’ını ayarlayın. Program.cs’de `.AddOtlpExporter()` bunu kullanır.

---

# 8) Log–Trace Korelasyonu

- Serilog loglarına `trace_id` ve `span_id` ekleyin:

```csharp
using OpenTelemetry.Trace;
app.Use(async (ctx, next) =>
{
    await next();
    var act = System.Diagnostics.Activity.Current;
    if (act != null)
        Log.ForContext("trace_id", act.TraceId.ToString())
           .ForContext("span_id", act.SpanId.ToString())
           .Information("TraceLinked");
});
```

- Grafana’da Tempo trace’ini açıp ilgili logları **Loki** kaynağından `trace_id` ile filtreleyebilirsiniz.

---

# 9) Prod Tavsiyeleri

- **Sampling**: Varsayılan %100, prod’da `%5–20` head sampling (otel collector `probabilistic_sampler`).
- **Retention**: Loki/Tempo saklama politikaları ve S3 backend (MinIO) ile uzun süreli arşiv.
- **Alerting**: Prometheus (metrics) + Alertmanager; API error rate, p95 latency, 5xx oranı alarmları.
- **Security**: Loglarda **sır**/anahtar yazmayın; PII maskeleme; collector ve grafana erişim kontrolü.

---

# 10) Test Senaryoları

- 200/400/500 yanıtlarında log alanları dolu mu? `tenantId`, `userId`, `correlationId`.
- Trace zinciri: WebApp → API → DB çağrısı span’ları görünüyor mu?
- Loki’de `trace_id="..."` ile log eşleşmeleri.
- Yük altında (k6/JMeter) p95/p99 latency metrikleri ve hata oranı grafikleri.

---

# 11) Sonraki Paket

- **E1 – Faturalama & Planlama Şeması**: Abonelik planları, özellik bayrakları, fiyatlandırma ve ödeme tümlemesi (Stripe/Iyzico opsiyonları).
