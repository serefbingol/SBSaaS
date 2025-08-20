Bu belge **E3 – Müşteri & Faturalama UI** iş paketinin uçtan uca kılavuzudur. Hedef: Razor WebApp’te müşteri (tenant) açısından **fatura/ödeme geçmişi**, **plan görüntüleme/değiştirme**, **checkout başlatma**, (opsiyonel) **Stripe Billing Portal** entegrasyonu ve i18n uyumlu, güvenli bir arayüz sağlamak.

---

# 0) DoD – Definition of Done
- Kullanıcı aktif tenant ile **fatura** ve **ödemeleri** listeleyebiliyor, detay görebiliyor.
- Plan listesi/ayrıntıları, mevcut abonelik durumu ve **upgrade/downgrade** akışları çalışıyor.
- "Satın al"/"Yükselt" butonları **E2** checkout başlatma uçlarına bağlandı.
- (Opsiyonel) **Stripe Billing Portal** linki üretilip gösteriliyor.
- UI, **C1 i18n** ile çok dilli; **C3 tenant seçici** ve **X-Tenant-Id** header’ı ile uyumlu.
- Yetki: Sayfalar `[Authorize]`, admin/owner görünürlüğü menüde kontrol ediliyor.

---

# 1) Navigasyon ve Rotalar
```
/ Billing
  / Plans            -> Plan listesi ve mevcut abonelik
  / Invoices         -> Fatura listesi
  / Invoices/{id}    -> Fatura detay
  / Payments         -> Ödeme geçmişi
  / Portal           -> (Ops.) Stripe Billing Portal redirect
```

---

# 2) API İstemcisi (WebApp → API)
**`Services/BillingApiClient.cs`**
```csharp
using System.Net.Http.Json;

public class BillingApiClient
{
    private readonly HttpClient _http;
    public BillingApiClient(IHttpClientFactory f) { _http = f.CreateClient("ApiClient"); }

    public Task<List<PlanDto>?> GetPlansAsync() => _http.GetFromJsonAsync<List<PlanDto>>("api/v1/billing/plans");
    public Task<SubscriptionDto?> GetActiveSubscriptionAsync() => _http.GetFromJsonAsync<SubscriptionDto>("api/v1/billing/subscriptions/active");
    public Task<List<InvoiceDto>?> GetInvoicesAsync() => _http.GetFromJsonAsync<List<InvoiceDto>>("api/v1/billing/invoices");
    public Task<InvoiceDto?> GetInvoiceAsync(Guid id) => _http.GetFromJsonAsync<InvoiceDto>($"api/v1/billing/invoices/{id}");
    public Task<List<PaymentDto>?> GetPaymentsAsync() => _http.GetFromJsonAsync<List<PaymentDto>>("api/v1/billing/payments");

    public Task<HttpResponseMessage> StartCheckoutAsync(StartCheckoutRequest req)
        => _http.PostAsJsonAsync("api/v1/pay/checkout", req);

    public Task<PortalLinkResponse?> GetStripePortalLinkAsync()
        => _http.GetFromJsonAsync<PortalLinkResponse>("api/v1/pay/stripe/portal-link");
}

public record PlanDto(Guid Id, string Code, string Name, string? Description, decimal Price, string Currency, string BillingCycle, IReadOnlyDictionary<string,string> Features);
public record SubscriptionDto(Guid Id, string PlanCode, string Status, DateOnly StartDate, DateOnly? EndDate);
public record InvoiceDto(Guid Id, string Number, DateOnly IssueDate, DateOnly DueDate, decimal Amount, string Currency, string Status);
public record PaymentDto(Guid Id, Guid InvoiceId, DateOnly PaymentDate, decimal Amount, string Currency, string Method, string? TransactionId);
public record StartCheckoutRequest(string PlanCode, string Email);
public record PortalLinkResponse(string Url);
```
**Kayıt** – `Program.cs` (C3’deki `ApiClient` konfigürasyonunu kullanır):
```csharp
builder.Services.AddScoped<BillingApiClient>();
```

---

# 3) Controller & Views
## 3.1 BillingController
**`Controllers/BillingController.cs`**
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize]
public class BillingController : Controller
{
    private readonly BillingApiClient _api;
    private readonly IHttpContextAccessor _http;
    public BillingController(BillingApiClient api, IHttpContextAccessor http) { _api = api; _http = http; }

    [HttpGet]
    public async Task<IActionResult> Plans()
    {
        var plans = await _api.GetPlansAsync() ?? new();
        var sub = await _api.GetActiveSubscriptionAsync();
        return View((plans, sub));
    }

    [HttpPost]
    public async Task<IActionResult> Buy(string planCode)
    {
        var email = User.Identity?.Name ?? User.FindFirst("email")?.Value ?? "user@example.com";
        var res = await _api.StartCheckoutAsync(new StartCheckoutRequest(planCode, email));
        if (!res.IsSuccessStatusCode) return RedirectToAction("Plans");
        var payload = await res.Content.ReadFromJsonAsync<Dictionary<string,string>>();
        if (payload != null && payload.TryGetValue("url", out var url)) return Redirect(url);
        return RedirectToAction("Plans");
    }

    [HttpGet]
    public async Task<IActionResult> Invoices()
    {
        var list = await _api.GetInvoicesAsync() ?? new();
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Invoice(Guid id)
    {
        var inv = await _api.GetInvoiceAsync(id);
        if (inv is null) return RedirectToAction("Invoices");
        return View(inv);
    }

    [HttpGet]
    public async Task<IActionResult> Payments()
    {
        var list = await _api.GetPaymentsAsync() ?? new();
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Portal()
    {
        var link = await _api.GetStripePortalLinkAsync();
        if (link?.Url is string url) return Redirect(url);
        return RedirectToAction("Plans");
    }
}
```

## 3.2 Views (özet örnekler)
**`Views/Billing/Plans.cshtml`**
```razor
@model (List<PlanDto> Plans, SubscriptionDto? Sub)
@inject Microsoft.AspNetCore.Mvc.Localization.IViewLocalizer L
<h2>@L["Plans"]</h2>
@if (Model.Sub is not null)
{
  <div class="alert">@L["CurrentPlan"]: @Model.Sub.PlanCode (@Model.Sub.Status)</div>
}
<div class="grid">
@foreach (var p in Model.Plans)
{
  <div class="card">
    <h3>@p.Name</h3>
    <p>@p.Description</p>
    <div>@p.Price @p.Currency / @p.BillingCycle</div>
    <ul>
      @foreach (var f in p.Features)
      { <li><strong>@f.Key</strong>: @f.Value</li> }
    </ul>
    <form asp-action="Buy" method="post">
      <input type="hidden" name="planCode" value="@p.Code" />
      <button type="submit">@L["BuyOrUpgrade"]</button>
    </form>
  </div>
}
</div>
<div class="mt-3">
  <a asp-action="Portal">@L["OpenBillingPortal"]</a>
</div>
```

**`Views/Billing/Invoices.cshtml`**
```razor
@model List<InvoiceDto>
@inject Microsoft.AspNetCore.Mvc.Localization.IViewLocalizer L
<h2>@L["Invoices"]</h2>
<table>
  <thead><tr><th>@L["Number"]</th><th>@L["IssueDate"]</th><th>@L["DueDate"]</th><th>@L["Amount"]</th><th>@L["Status"]</th><th></th></tr></thead>
  <tbody>
  @foreach (var i in Model)
  {
    <tr>
      <td>@i.Number</td>
      <td>@i.IssueDate</td>
      <td>@i.DueDate</td>
      <td>@i.Amount @i.Currency</td>
      <td>@i.Status</td>
      <td><a asp-action="Invoice" asp-route-id="@i.Id">@L["Details"]</a></td>
    </tr>
  }
  </tbody>
</table>
```

**`Views/Billing/Invoice.cshtml`**
```razor
@model InvoiceDto
@inject Microsoft.AspNetCore.Mvc.Localization.IViewLocalizer L
<h2>@L["InvoiceDetails"]</h2>
<dl>
  <dt>@L["Number"]</dt><dd>@Model.Number</dd>
  <dt>@L["IssueDate"]</dt><dd>@Model.IssueDate</dd>
  <dt>@L["DueDate"]</dt><dd>@Model.DueDate</dd>
  <dt>@L["Amount"]</dt><dd>@Model.Amount @Model.Currency</dd>
  <dt>@L["Status"]</dt><dd>@Model.Status</dd>
</dl>
```

**`Views/Billing/Payments.cshtml`**
```razor
@model List<PaymentDto>
@inject Microsoft.AspNetCore.Mvc.Localization.IViewLocalizer L
<h2>@L["Payments"]</h2>
<table>
  <thead><tr><th>@L["Date"]</th><th>@L["Amount"]</th><th>@L["Method"]</th><th>@L["Invoice"]</th></tr></thead>
  <tbody>
  @foreach (var p in Model)
  {
    <tr>
      <td>@p.PaymentDate</td><td>@p.Amount @p.Currency</td><td>@p.Method</td><td>@p.InvoiceId</td>
    </tr>
  }
  </tbody>
</table>
```

---

# 4) Plan Değiştirme ve Prorasyon
- **Stripe** aboneliklerinde upgrade/downgrade işlemleri Checkout veya Billing Portal üzerinden proration ile yapılır; E2’deki `Portal` akışı önerilir.
- **iyzico** tarafında dönemsel abonelik yoksa plan değişimi bir sonraki döneme uygulanacak şekilde UI’da bilgilendirin.

---

# 5) Güvenlik & Yetki
- `[Authorize]` zorunlu; Owner/Admin dışındaki rollerde plan değiştirme butonu gizlenebilir (`<authorize roles="Admin,Owner">`).
- `X-Tenant-Id` header’ı (C3) otomatik eklenecek şekilde `ApiClient` kullanın.
- `returnUrl` doğrulaması ve CSRF token’ları form’larda aktif.

---

# 6) i18n ve Biçimlendirme
- C1/C4 ile uyumlu `IViewLocalizer` anahtarları: `Plans`, `CurrentPlan`, `BuyOrUpgrade`, `Invoices`, `InvoiceDetails`, `Payments`.
- Para birimi/tarih biçimlendirmesi için C1’deki kültür ve WebApp projesinde tanımlanan `IFormatService`’i kullanın (bkz. C1, bölüm 9).

---

# 7) Test Senaryoları
- Plan listesinde mevcut abonelik doğru işaretleniyor mu?
- "Buy" tıklandığında Stripe/iyzico yönlendirmesi başarılı mı, iptal/success dönüşlerinde UI mesajları?
- Fatura/ödeme listeleri; boş durumlar ve hata mesajları.
- Owner olmayan kullanıcı için plan değiştirme gizli mi?

---

# 8) Sonraki Paket
- **E4 – İç Yönetim (Admin) UI**: Plan/özellik CRUD, manuel fatura, iade işlemleri, mutabakat raporları; RBAC ile korumalı yönetim paneli.
