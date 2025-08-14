Bu belge **E2 – Ödeme Sağlayıcı Entegrasyonu** iş paketinin uçtan uca uygulanabilir kılavuzudur. Hedef: **Stripe** ve **iyzico** ile ödeme alma (tek seferlik ve abonelik), webhook/callback işleme, iç faturalama ile mutabakat ve çok kiracılı (Tenant\_ID) güvenli akış.

---

# 0) DoD – Definition of Done

- Stripe ve iyzico için **sandbox** ortamında başarıyla ödeme alınabiliyor.
- **Hosted/Checkout** akışları ve (Stripe) **Billing/Subscription** akışı çalışıyor.
- Webhook/callback uçları imza doğrulamasıyla güvenli ve **idempotent**.
- Ödemeler, faturalar ve abonelikler **billing** şemasına işlenip **audit.change\_log**’a düşüyor.
- Çok kiracılı güvenlik: ödeme nesneleri **TenantId** ile ilişkilendirildi, cross-tenant erişim engelli.
- Refunding/chargeback/dispute olayları için temel işleyiciler mevcut.

---

# 1) Ön Koşullar & Secrets

**appsettings / user-secrets** (örnek):

```json
{
  "Payments": {
    "Provider": "Stripe|IyziCo", // varsayılan
    "Currency": "TRY",
    "SuccessUrl": "https://app.local/pay/success",
    "CancelUrl": "https://app.local/pay/cancel"
  },
  "Stripe": {
    "SecretKey": "sk_test_...",
    "WebhookSecret": "whsec_...",           // /webhooks/stripe
    "PriceMap": { "PLAN_BASIC": "price_123", "PLAN_PRO": "price_456" }
  },
  "IyziCo": {
    "ApiKey": "sandbox-...",
    "SecretKey": "sandbox-...",
    "BaseUrl": "https://sandbox-api.iyzipay.com",
    "CallbackUrl": "https://api.local/webhooks/iyzico/callback",
    "WebhookSecret": "optional-shared-secret" // varsa
  }
}
```

> **Not:** Production’da TLS zorunlu; tüm gizlileri **User Secrets / Vault** ile yönetin.

---

# 2) NuGet Paketleri

```bash
# Stripe
dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Stripe.net

# iyzico (Iyzipay)
dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Iyzipay
```

---

# 3) Ortak Domain & Servis Sözleşmesi

**Amaç:** Sağlayıcıdan bağımsız bir arayüz ile çalışmak (Strategy Pattern).

\`\`

```csharp
public record PaymentInitRequest(Guid TenantId, string PlanCode, string CustomerEmail, string? Locale = null, string? ReturnUrl = null);
public record PaymentInitResponse(string Provider, string CheckoutUrl, string SessionOrToken, DateTimeOffset ExpiresAt);

public record WebhookProcessResult(bool Ok, string? InvoiceId = null, string? PaymentId = null, string? Message = null);

public interface IPaymentGateway
{
    Task<PaymentInitResponse> InitCheckoutAsync(PaymentInitRequest req, CancellationToken ct);
    Task<WebhookProcessResult> ProcessWebhookAsync(HttpRequest request, CancellationToken ct);
}
```

**Kayıt noktası** – `Payments:Provider` değerine göre **StripeGateway** veya **IyziGateway** DI’dan çözülür.

---

# 4) Stripe Entegrasyonu

## 4.1 Configure & Client

\`\` (özet):

```csharp
using Stripe;
using Stripe.Checkout;

public class StripeGateway : IPaymentGateway
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<StripeGateway> _log;
    private readonly IBillingWriter _billing; // billing.* tablolarına yazar (E1)

    public StripeGateway(IConfiguration cfg, ILogger<StripeGateway> log, IBillingWriter billing)
    {
        _cfg = cfg; _log = log; StripeConfiguration.ApiKey = _cfg["Stripe:SecretKey"]; }

    public async Task<PaymentInitResponse> InitCheckoutAsync(PaymentInitRequest req, CancellationToken ct)
    {
        var priceMap = _cfg.GetSection("Stripe:PriceMap").Get<Dictionary<string,string>>()!;
        if (!priceMap.TryGetValue(req.PlanCode, out var priceId)) throw new InvalidOperationException("Price not mapped");

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = _cfg["Payments:SuccessUrl"] + "?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = _cfg["Payments:CancelUrl"],
            CustomerEmail = req.CustomerEmail,
            LineItems = new List<SessionLineItemOptions> { new() { Price = priceId, Quantity = 1 } },
            Metadata = new Dictionary<string, string> { {"tenantId", req.TenantId.ToString()}, {"plan", req.PlanCode} }
        };
        var service = new SessionService();
        var session = await service.CreateAsync(options, null, ct);
        return new("Stripe", session.Url, session.Id, DateTimeOffset.FromUnixTimeSeconds(session.ExpiresAt ?? 0));
    }

    public async Task<WebhookProcessResult> ProcessWebhookAsync(HttpRequest request, CancellationToken ct)
    {
        var json = await new StreamReader(request.Body).ReadToEndAsync(ct);
        var secret = _cfg["Stripe:WebhookSecret"];
        var sigHeader = request.Headers["Stripe-Signature"].ToString();
        Event stripeEvent;
        try { stripeEvent = EventUtility.ConstructEvent(json, sigHeader, secret); }
        catch (Exception ex) { _log.LogWarning(ex, "Stripe signature invalid"); return new(false, Message: "invalid_signature"); }

        switch (stripeEvent.Type)
        {
            case Events.InvoicePaid:
                var inv = (Invoice)stripeEvent.Data.Object;
                await _billing.OnStripeInvoicePaidAsync(inv, ct);
                return new(true, InvoiceId: inv.Id, PaymentId: inv.PaymentIntentId);

            case Events.CustomerSubscriptionCreated:
            case Events.CustomerSubscriptionUpdated:
            case Events.CustomerSubscriptionDeleted:
                var sub = (Subscription)stripeEvent.Data.Object;
                await _billing.OnStripeSubscriptionChangedAsync(sub, stripeEvent.Type, ct);
                return new(true);

            case Events.CheckoutSessionCompleted:
                var sess = (Session)stripeEvent.Data.Object;
                await _billing.OnStripeCheckoutCompletedAsync(sess, ct);
                return new(true);
        }
        return new(true);
    }
}
```

## 4.2 Billing Writer (Stripe → E1 tabloları)

\`\` Stripe/iyzico ortak yazıcıdır. Örnek yöntemler:

```csharp
Task OnStripeCheckoutCompletedAsync(Stripe.Checkout.Session s, CancellationToken ct);
Task OnStripeInvoicePaidAsync(Stripe.Invoice inv, CancellationToken ct);
Task OnStripeSubscriptionChangedAsync(Stripe.Subscription sub, string evtType, CancellationToken ct);
Task OnIyziPaymentCompletedAsync(Iyzipay.Model.CheckoutForm form, CancellationToken ct);
```

Uygulama: `billing.subscription`, `billing.invoice`, `billing.payment` tablolarında kayıt/ güncelleme; TenantId eşlemesi için **metadata** veya müşteri notları kullanılır (`session.Metadata["tenantId"]`).

---

# 5) iyzico Entegrasyonu (Iyzipay)

## 5.1 Hosted Checkout (Önerilen)

Akış: API `InitCheckout` → iyzico **CheckoutFormInitialize** → dönen **paymentPageUrl**’i WebApp yönlendirir → kullanıcı ödeme yapar → iyzico **callbackUrl**’e `token` gönderir → API `CheckoutForm.Retrieve` ile detayları çeker.

\`\` (özet):

```csharp
using Iyzipay;
using Iyzipay.Model;
using Iyzipay.Request;

public class IyziGateway : IPaymentGateway
{
    private readonly IConfiguration _cfg; private readonly ILogger<IyziGateway> _log; private readonly IBillingWriter _billing;
    public IyziGateway(IConfiguration cfg, ILogger<IyziGateway> log, IBillingWriter billing) { _cfg = cfg; _log = log; _billing = billing; }

    Iyzipay.Options Opt => new() { ApiKey = _cfg["IyziCo:ApiKey"], SecretKey = _cfg["IyziCo:SecretKey"], BaseUrl = _cfg["IyziCo:BaseUrl"] };

    public async Task<PaymentInitResponse> InitCheckoutAsync(PaymentInitRequest req, CancellationToken ct)
    {
        var price = await ResolvePlanPriceAsync(req.PlanCode, ct); // E1 plan tablosundan, TRY
        var request = new CreateCheckoutFormInitializeRequest
        {
            Locale = req.Locale ?? "tr",
            ConversationId = Guid.NewGuid().ToString(),
            Price = price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            PaidPrice = price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Currency = Currency.TRY.ToString(),
            BasketId = req.PlanCode,
            CallbackUrl = _cfg["IyziCo:CallbackUrl"],
            EnabledInstallments = new List<int> { 1, 3, 6, 9 },
            Buyer = new Buyer { Id = req.TenantId.ToString(), Email = req.CustomerEmail, Name = req.CustomerEmail, Surname = "-" },
            BillingAddress = new Address { ContactName = req.CustomerEmail, Country = "Turkey", City = "Istanbul", Description = "-" },
            BasketItems = new List<BasketItem>{ new BasketItem{ Id = req.PlanCode, Name = req.PlanCode, Category1 = "Subscription", ItemType = BasketItemType.VIRTUAL.ToString(), Price = price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) } }
        };
        var init = CheckoutFormInitialize.Create(request, Opt);
        return new("IyziCo", init.PaymentPageUrl, init.Token, DateTimeOffset.UtcNow.AddMinutes(15));
    }

    public async Task<WebhookProcessResult> ProcessWebhookAsync(HttpRequest request, CancellationToken ct)
    {
        // iyzico klasik akışta webhook yerine Callback + Retrieve kullanır
        // Bu nedenle burada true döndürüp gerçek işleme callback endpoint’inde yapılır.
        return new(true);
    }
}
```

## 5.2 Callback İşleme (iyzico)

**Controller**: `/webhooks/iyzico/callback` (POST form-data ile `token` gelir):

```csharp
[ApiController]
[Route("webhooks/iyzico")]
public class IyziWebhookController : ControllerBase
{
    private readonly IConfiguration _cfg; private readonly IBillingWriter _billing;
    public IyziWebhookController(IConfiguration cfg, IBillingWriter billing) { _cfg = cfg; _billing = billing; }

    [HttpPost("callback")]
    public IActionResult Callback([FromForm] string token)
    {
        var opt = new Iyzipay.Options { ApiKey = _cfg["IyziCo:ApiKey"], SecretKey = _cfg["IyziCo:SecretKey"], BaseUrl = _cfg["IyziCo:BaseUrl"] };
        var req = new RetrieveCheckoutFormRequest { Token = token };
        var form = CheckoutForm.Retrieve(req, opt);
        if (form.PaymentStatus == "SUCCESS")
        {
            // form.BasketId → PlanCode, form.Price → amount
            _ = _billing.OnIyziPaymentCompletedAsync(form, HttpContext.RequestAborted);
            return Ok();
        }
        return BadRequest();
    }
}
```

> İmzalı webhook desteğiniz varsa ek **signature** doğrulaması yapın (shared secret).

---

# 6) Webhook Uçları (API)

```csharp
[ApiController]
[Route("webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IEnumerable<IPaymentGateway> _gateways;
    public WebhooksController(IEnumerable<IPaymentGateway> gateways) => _gateways = gateways;

    [HttpPost("stripe")]
    public Task<IActionResult> Stripe()
        => Process(async (g, r, ct) => g is StripeGateway ? await g.ProcessWebhookAsync(r, ct) : new(false));

    [HttpPost("iyzico")] // çoğu senaryoda gerçek işleme IyziWebhookController’dadır
    public Task<IActionResult> IyziCo()
        => Process(async (g, r, ct) => g is IyziGateway ? await g.ProcessWebhookAsync(r, ct) : new(true));

    private async Task<IActionResult> Process(Func<IPaymentGateway, HttpRequest, CancellationToken, Task<WebhookProcessResult>> fn)
    {
        foreach (var g in _gateways)
        {
            var res = await fn(g, Request, HttpContext.RequestAborted);
            if (res.Ok) return Ok();
        }
        return BadRequest();
    }
}
```

---

# 7) İdempotensi, Retry ve Kuyruklama

- **Idempotency-Key**: Stripe tüm write çağrılarında header destekler; aynı key için çift kayıt oluşmaz.
- iyzico callback’inde `ConversationId` + `token` birlikteliğini **unique** tutun; işlendi mi tablosu.
- Webhook’ları **durum makinesi** ile işleyin: `received → validated → persisted → completed`. İşleyici **background queue** (HostedService/Worker) kullanabilir.

**İzleme Alanları**: `eventId`, `provider`, `rawPayloadHash`, `processedUtc`.

---

# 8) Tenant Güvenliği & Mutabakat

- Tüm ödeme/abonelik/fatura kayıtlarında **TenantId** zorunlu; mapping için:
  - Stripe: `metadata["tenantId"]` veya `customer.metadata`.
  - iyzico: `Buyer.Id` (TenantId) + `BasketId` (PlanCode).
- Mutabakat raporu: Sağlayıcı toplamları ↔ `billing.invoice/payment` toplamları (gün/ay) karşılaştır.

---

# 9) Para Birimi, Vergi, 3DS/SCA

- Varsayılan para birimi **TRY**; Stripe’ta **Price** objesi para birimiyle tanımlanır.
- KDV/VAT: Planlara `vat_rate` ekleyin; Stripe Tax/iyzico vergi seçenekleri ile entegre edilebilir.
- 3DS/SCA: Stripe Checkout otomatik yönetir; iyzico Hosted Form 3DS yönlendirmelerini içerir.

---

# 10) Refund / Dispute

- API uçları:
  - `POST /api/v1/billing/payments/{id}/refund`
- Stripe: `RefundService.Create` ile; iyzico: `CreateRefundRequest` ile iade.
- Dispute/chargeback olaylarında aboneliği beklemeye alın, müşteri ile iletişim akışı tetikle.

---

# 11) OpenAPI (G1) Güncellemeleri

- `POST /api/v1/pay/checkout` (body: `planCode`, `email`) → { url }
- `POST /webhooks/stripe` (raw)
- `POST /webhooks/iyzico` (raw) ve/veya `POST /webhooks/iyzico/callback` (form)
- `POST /api/v1/billing/payments/{id}/refund`

---

# 12) Test Planı (Sandbox)

- **Stripe**: test kartları (4242...), `invoice.paid`, `checkout.session.completed` eventleri simüle edin.
- **iyzico**: sandbox kartları; başarılı/başarısız ödeme; callback token tekrar denemesi (idempotency) testi.
- Negatif: imza doğrulaması başarısız → 400; hatalı TenantId mapping.

---

# 13) Üretim Notları

- **Webhook IP allowlist** (mümkünse) ve TLS zorunluluğu.
- Sağlayıcı **status page**’leri ile bağıl alarm; retry politikaları.
- Rate limit & circuit breaker (Polly) ile outbound çağrılarda koruma.

---

# 14) Sonraki Paket

- **E3 – Müşteri & Faturalama UI**: Fatura listesi, ödeme geçmişi, plan yükseltme/düşürme; self-service portal (Stripe Billing Portal entegrasyonu opsiyonel).

