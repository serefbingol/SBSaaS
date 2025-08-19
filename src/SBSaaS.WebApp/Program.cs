var builder = WebApplication.CreateBuilder(args);

// Servisleri container'a ekleyin.
// Mimari dokümanlarınızda (C1, C2, D2 vb.) belirtilen servisleri buraya ekleyeceksiniz.
// Örnek: builder.Services.AddLocalization(), builder.Services.AddAuthentication(), builder.Host.UseSerilog()
builder.Services.AddRazorPages();

var app = builder.Build();

// HTTP request pipeline'ını yapılandırın.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Mimari dokümanlarınızda belirtilen middleware'leri buraya ekleyeceksiniz.
// Örnek: app.UseAuthentication(), app.UseAuthorization(), app.UseRequestLocalization()
app.UseAuthorization();

app.MapRazorPages();

app.Run();
