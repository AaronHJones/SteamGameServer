using MudBlazor.Services;
using RustServerHealth.Components;
using RustServerHealth.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Core services
builder.Services.AddSingleton<GameServerRegistry>();
builder.Services.AddSingleton<ServiceController>();

// GameServerManager is the BackgroundService that owns all per-server monitors
builder.Services.AddSingleton<GameServerManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GameServerManager>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
