var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<PCSTORE.Services.ExcelService>();
builder.Services.AddSingleton<PCSTORE.Services.DataStoreService>();
builder.Services.AddSingleton<PCSTORE.Services.AuthService>();
builder.Services.AddSingleton<PCSTORE.Services.CustomerService>();
builder.Services.AddSingleton<PCSTORE.Services.StartupService>();
builder.Services.AddScoped<PCSTORE.Services.AIChatService>();
builder.Services.AddHttpClient<PCSTORE.Services.ImageSearchService>();

// Cấu hình Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Tự động import dữ liệu khi ứng dụng khởi động
try
{
    var startupService = app.Services.GetRequiredService<PCSTORE.Services.StartupService>();
    startupService.AutoImportData();
    
    // Tự động cập nhật hình ảnh cho sản phẩm (chạy bất đồng bộ, không chặn khởi động)
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(5000); // Đợi 5 giây để đảm bảo ứng dụng đã khởi động xong
            await startupService.AutoUpdateImagesAsync();
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Lỗi khi tự động cập nhật hình ảnh khi khởi động");
        }
    });
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Lỗi khi tự động import dữ liệu khi khởi động");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Sử dụng Session
app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
