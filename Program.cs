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
builder.Services.AddSingleton<SqliteStorageService>();

// MCP server: exposes CRUD tools for cards, columns, labels, priorities and
// projects over HTTP. Legacy SSE (Server-Sent Events) transport is enabled so
// clients that don't support Streamable HTTP can connect via "/mcp/sse" and
// post messages to "/mcp/message". Tools are discovered via
// [McpServerToolType]/[McpServerTool] attributes in this assembly.
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // SSE requires stateful mode; it is never mapped when Stateless = true.
        options.Stateless = false;

#pragma warning disable MCP9004 // EnableLegacySse is obsolete
        // Enable legacy SSE endpoints (/sse and /message) alongside Streamable HTTP.
        options.EnableLegacySse = true;
#pragma warning restore MCP9004
    })
    .WithToolsFromAssembly();

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

// Map the MCP server under "/mcp" so it does not collide with the MVC "/" route.
// Streamable HTTP clients connect to "/mcp".
// Legacy SSE clients connect to "/mcp/sse" and POST to "/mcp/message"
// (because EnableLegacySse was set to true above).
app.MapMcp("/mcp");


app.Run();
