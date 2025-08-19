Bu belge **E4 – İç Yönetim (Admin) UI** iş paketinin uçtan uca kılavuzudur. Hedef: Razor WebApp içinde yalnızca **Owner/Admin** rollerine açık bir yönetim paneli ile **plan/feature CRUD**, **abonelik yönetimi**, **manuel fatura kesme**, **iade/refund**, **mutabakat raporları** ve temel **denetim** (audit) görünürlüğünü sağlamak.

---

# 0) DoD – Definition of Done
- `/admin` altında RBAC korumalı menü ve sayfalar.
- Plan/Feature CRUD işlemleri API üzerinden çalışıyor; doğrulamalar ve i18n mesajları var.
- Abonelik arama/listeleme, iptal/yenileme, plan değişimi (Stripe/Portal linki ya da iyzico uyarısı) akışları mevcut.
- Manuel fatura oluşturma ve iade/refund komutları API ile entegre.
- Gün/ay bazlı mutabakat raporları (provider vs. internal) görüntülenebiliyor.
- İşlemler **audit.change_log** ile izlenebilir; Admin UI’da basit bir audit izleyici var.

---

# 1) Navigasyon (RBAC)
```
/Admin
  / Plans          -> Plan/Feature CRUD
  / Subscriptions  -> Abonelik arama/listeleme
  / Invoices       -> Manuel fatura kesme + liste
  / Refunds        -> İade başlatma / durum
  / Reconcile      -> Mutabakat raporları
  / Audit          -> Audit değişiklik listesi (read-only)
```
**Yetki**: `[Authorize(Policy = "AdminOnly")]` veya `[Authorize(Roles = "Admin,Owner")]` + UI tarafında Authorize TagHelper.

---

# 2) API İstemcisi (Admin)
**`Services/AdminApiClient.cs`**
```csharp
using System.Net.Http.Json;

public class AdminApiClient
{
    private readonly HttpClient _http;
    public AdminApiClient(IHttpClientFactory f) { _http = f.CreateClient("ApiClient"); }

    // Plans
    public Task<List<PlanDto>?> GetPlansAsync() => _http.GetFromJsonAsync<List<PlanDto>>("api/v1/billing/plans");
    public Task<HttpResponseMessage> CreatePlanAsync(PlanUpsertDto dto) => _http.PostAsJsonAsync("api/v1/billing/plans", dto);
    public Task<HttpResponseMessage> UpdatePlanAsync(Guid id, PlanUpsertDto dto) => _http.PutAsJsonAsync($"api/v1/billing/plans/{id}", dto);
    public Task<HttpResponseMessage> DeletePlanAsync(Guid id) => _http.DeleteAsync($"api/v1/billing/plans/{id}");

    // Features
    public Task<List<FeatureDto>?> GetFeaturesAsync(Guid planId) => _http.GetFromJsonAsync<List<FeatureDto>>($"api/v1/billing/plans/{planId}/features");
    public Task<HttpResponseMessage> UpsertFeatureAsync(Guid planId, FeatureUpsertDto dto) => _http.PostAsJsonAsync($"api/v1/billing/plans/{planId}/features", dto);
    public Task<HttpResponseMessage> DeleteFeatureAsync(Guid planId, Guid featureId) => _http.DeleteAsync($"api/v1/billing/plans/{planId}/features/{featureId}");

    // Subscriptions
    public Task<Paged<SubscriptionDto>?> SearchSubscriptionsAsync(string? term, int page) => _http.GetFromJsonAsync<Paged<SubscriptionDto>>($"api/v1/billing/admin/subscriptions?term={term}&page={page}");
    public Task<HttpResponseMessage> CancelSubscriptionAsync(Guid id) => _http.PatchAsync($"api/v1/billing/subscriptions/{id}/cancel", null);

    // Invoices & Payments
    public Task<HttpResponseMessage> CreateInvoiceAsync(InvoiceCreateDto dto) => _http.PostAsJsonAsync("api/v1/billing/invoices", dto);
    public Task<List<InvoiceDto>?> GetInvoicesAsync() => _http.GetFromJsonAsync<List<InvoiceDto>>("api/v1/billing/invoices");
    public Task<HttpResponseMessage> RefundAsync(Guid paymentId, RefundRequestDto dto) => _http.PostAsJsonAsync($"api/v1/billing/payments/{paymentId}/refund", dto);

    // Reconcile
    public Task<ReconcileReportDto?> ReconcileAsync(DateOnly date) => _http.GetFromJsonAsync<ReconcileReportDto>($"api/v1/billing/admin/reconcile?date={date:yyyy-MM-dd}");

    // Audit
    public Task<Paged<AuditLogDto>?> GetAuditAsync(
        int page = 1, int pageSize = 50,
        DateTime? from = null, DateTime? to = null,
        string? table = null, string? operation = null, string? userId = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString(),
            ["from"] = from?.ToString("o"), // ISO 8601 formatı
            ["to"] = to?.ToString("o"),
            ["table"] = table,
            ["operation"] = operation,
            ["userId"] = userId
        };
        var queryString = string.Join("&", query.Where(kvp => !string.IsNullOrEmpty(kvp.Value)).Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value!)}"));
        return _http.GetFromJsonAsync<Paged<AuditLogDto>>($"api/v1/audit/change-log?{queryString}");
    }
}

public record PlanUpsertDto(string Code, string Name, string? Description, decimal Price, string Currency, string BillingCycle);
public record FeatureUpsertDto(Guid? Id, string Key, string? Value);
public record InvoiceCreateDto(Guid SubscriptionId, DateOnly IssueDate, DateOnly DueDate, decimal Amount, string Currency, string? Note);
public record RefundRequestDto(decimal Amount, string Reason);
public record ReconcileReportDto(DateOnly Date, decimal ProviderTotal, decimal InternalTotal, int MismatchCount, List<string> Notes);
// AuditRow, API
```
**Kayıt**: `Program.cs` → `builder.Services.AddScoped<AdminApiClient>();`

---

# 3) Admin Layout & Menü
**`Views/Shared/_AdminLayout.cshtml`** (özet)
```razor
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, Microsoft.AspNetCore.Authorization
<!DOCTYPE html>
<html>
<body>
<nav>
  <ul>
    <authorize roles="Admin,Owner">
      <li><a asp-controller="AdminPlans" asp-action="Index">Plans</a></li>
      <li><a asp-controller="AdminSubscriptions" asp-action="Index">Subscriptions</a></li>
      <li><a asp-controller="AdminInvoices" asp-action="Index">Invoices</a></li>
      <li><a asp-controller="AdminRefunds" asp-action="Index">Refunds</a></li>
      <li><a asp-controller="AdminReconcile" asp-action="Index">Reconcile</a></li>
      <li><a asp-controller="AdminAudit" asp-action="Index">Audit</a></li>
    </authorize>
  </ul>
</nav>
<main>@RenderBody()</main>
</body>
</html>
```

---

# 4) Plan/Feature CRUD
## Controller
**`Controllers/AdminPlansController.cs`** (özet)
```csharp
[Authorize(Roles = "Admin,Owner")]
public class AdminPlansController : Controller
{
    private readonly AdminApiClient _api; public AdminPlansController(AdminApiClient api) => _api = api;

    public async Task<IActionResult> Index()
        => View(await _api.GetPlansAsync() ?? new());

    [HttpGet] public IActionResult Create() => View(new PlanUpsertDto("","","",0,"TRY","monthly"));
    [HttpPost] public async Task<IActionResult> Create(PlanUpsertDto dto)
    { if (!ModelState.IsValid) return View(dto); var r = await _api.CreatePlanAsync(dto); return RedirectToAction("Index"); }

    [HttpGet] public async Task<IActionResult> Edit(Guid id)
    { var plan = (await _api.GetPlansAsync() ?? new()).FirstOrDefault(x => x.Id==id); return View(plan); }
    [HttpPost] public async Task<IActionResult> Edit(Guid id, PlanUpsertDto dto)
    { var r = await _api.UpdatePlanAsync(id, dto); return RedirectToAction("Index"); }

    [HttpPost] public async Task<IActionResult> Delete(Guid id)
    { await _api.DeletePlanAsync(id); return RedirectToAction("Index"); }

    // Features
    public async Task<IActionResult> Features(Guid id)
    { ViewBag.PlanId = id; return View(await _api.GetFeaturesAsync(id) ?? new()); }

    [HttpPost] public async Task<IActionResult> UpsertFeature(Guid id, FeatureUpsertDto dto)
    { await _api.UpsertFeatureAsync(id, dto); return RedirectToAction("Features", new { id }); }

    [HttpPost] public async Task<IActionResult> DeleteFeature(Guid id, Guid featureId)
    { await _api.DeleteFeatureAsync(id, featureId); return RedirectToAction("Features", new { id }); }
}
```

## Views (özet)
- `Views/AdminPlans/Index.cshtml`: Grid/list + Create/Edit/Delete butonları.
- `Views/AdminPlans/Create.cshtml`, `Edit.cshtml`: form alanları (Code, Name, Price, Currency, Cycle, Description).
- `Views/AdminPlans/Features.cshtml`: feature list + upsert form (Key/Value).

---

# 5) Abonelik Yönetimi
**`Controllers/AdminSubscriptionsController.cs`** (özet)
```csharp
[Authorize(Roles = "Admin,Owner")]
public class AdminSubscriptionsController : Controller
{
    private readonly AdminApiClient _api;
    public AdminSubscriptionsController(AdminApiClient api) => _api = api;

    public async Task<IActionResult> Index(string? term, int page = 1)
        => View(await _api.SearchSubscriptionsAsync(term, page) ?? new Paged<SubscriptionDto>(page,20,0,new()));

    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id)
    { await _api.CancelSubscriptionAsync(id); return RedirectToAction("Index"); }
}
```
**View**: Arama kutusu (tenant adı/email), tablo, `Cancel` butonu.

---

# 6) Manuel Fatura & İade
**`Controllers/AdminInvoicesController.cs`** (özet)
```csharp
[Authorize(Roles = "Admin,Owner")]
public class AdminInvoicesController : Controller
{
    private readonly AdminApiClient _api; public AdminInvoicesController(AdminApiClient api) => _api = api;

    public async Task<IActionResult> Index() => View(await _api.GetInvoicesAsync() ?? new());

    [HttpGet] public IActionResult Create(Guid subscriptionId)
        => View(new InvoiceCreateDto(subscriptionId, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today.AddDays(7)),0,"TRY",null));

    [HttpPost] public async Task<IActionResult> Create(InvoiceCreateDto dto)
    { await _api.CreateInvoiceAsync(dto); return RedirectToAction("Index"); }
}
```

**`Controllers/AdminRefundsController.cs`** (özet)
```csharp
[Authorize(Roles = "Admin,Owner")]
public class AdminRefundsController : Controller
{
    private readonly AdminApiClient _api; public AdminRefundsController(AdminApiClient api) => _api = api;

    public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Refund(Guid paymentId, decimal amount, string reason)
    { await _api.RefundAsync(paymentId, new RefundRequestDto(amount, reason)); return RedirectToAction("Index"); }
}
```

---

# 7) Mutabakat Raporları
**`Controllers/AdminReconcileController.cs`** (özet)
```csharp
[Authorize(Roles = "Admin,Owner")]
public class AdminReconcileController : Controller
{
    private readonly AdminApiClient _api; public AdminReconcileController(AdminApiClient api) => _api = api;

    public async Task<IActionResult> Index(DateOnly? date)
    {
        var d = date ?? DateOnly.FromDateTime(DateTime.Today);
        var rep = await _api.ReconcileAsync(d) ?? new ReconcileReportDto(d,0,0,0,new());
        return View(rep);
    }
}
```
**View**: Tarih seçici + sonuç kartları: ProviderTotal, InternalTotal, MismatchCount, Notes listesi.

---

# 8) Audit Görünümü
**`Controllers/AdminAuditController.cs`**
```csharp
[Authorize(Roles = "Admin,Owner")]
public class AdminAuditController : Controller
{
    private readonly AdminApiClient _api; public AdminAuditController(AdminApiClient api) => _api = api;
    public async Task<IActionResult> Index(int page = 1)
        => View(await _api.GetAuditAsync(page) ?? new Paged<AuditRow>(new List<AuditRow>(), page, 50, 0));
}
```
**View**: Tablo sütunları – `At`, `Actor`, `Table`, `Action`, `Keys`, `Changes` (PII maskeleme uygulanmış hali).

---

# 9) i18n & UX Notları
- C1 ile uyumlu yerelleştirme; `Shared.resx`’e Admin anahtarları: `Plans`, `Features`, `Subscriptions`, `Invoices`, `Refunds`, `Reconcile`, `Audit`, `Create`, `Edit`, `Delete`, `Save`, `Cancel`, `Search`, `Amount`, `Reason`.
- Hata mesajları ve başarı bildirimleri için TempData + localized mesajlar.
- Büyük listeler için tablo filtreleme/sayfalama (sunucu tarafı).

---

# 10) Güvenlik
- `[Authorize(Roles=...)]` + `RequireTenant` **uygulanmaz** (Admin tüm tenant’ları yönetebilir ise). Eğer admin tenant-bazlıysa **C3 RequireTenant** kullanılabilir.
- CSRF: tüm POST formlarında `@Html.AntiForgeryToken()`.
- Girdi doğrulama: fiyat/para birimi/billing cycle enumları.

---

# 11) Test Senaryoları
- Plan create/edit/delete + feature upsert/delete.
- Abonelik iptali ve listede durum güncellemesi.
- Manuel fatura oluşturma ve listede görünmesi.
- Refund işlemi → ödeme/fatura durumlarının güncellenmesi.
- Mutabakat raporunda sapmaların doğru hesaplanması.
- RBAC: Yetkisiz kullanıcıların `/admin` altında 403 alması.

---

# 12) Sonraki Paket
- **F1 – Feature Flag & Limit Enforcement**: Özellik anahtarlarını (E1) UI ve API katmanında enforcement, kota/limit kontrolleri, overage fiyatlandırma opsiyonları.
