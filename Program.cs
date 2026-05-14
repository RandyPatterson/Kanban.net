using kanban.net.Services;

var builder = WebApplication.CreateBuilder(args);

// When running as a Windows Service, the working directory is System32.
// Set ContentRoot to the exe directory so App_Data and static files resolve correctly.
if (!builder.Environment.IsDevelopment())
{
    builder.Host.UseContentRoot(AppContext.BaseDirectory);
}

builder.Host.UseWindowsService();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<JsonStorageService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
