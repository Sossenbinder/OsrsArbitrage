using ArbitrageTracker.Core.Detection;
using ArbitrageTracker.Core.State;
using ArbitrageTracker.Data;
using ArbitrageTracker.Ingestion;
using ArbitrageTracker.Ingestion.Pollers;
using ArbitrageTracker.Web.Components;
using ArbitrageTracker.Web.Hubs;
using ArbitrageTracker.Web.Pipeline;
using ArbitrageTracker.Web.Validation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR();

// Behind the Caddy reverse proxy (TLS terminated there): trust the forwarded scheme/host so
// HTTPS redirect, generated links and SignalR see the real https origin. Caddy is the only hop,
// so we don't restrict known proxies/networks.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Persistence
builder.Services.AddDbContext<ArbitrageDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=data/arbitrage.db"));
builder.Services.AddScoped<SnapshotRepository>();

// Time
builder.Services.AddSingleton(TimeProvider.System);

// Core singletons (shared hot state + stateless services)
builder.Services.AddSingleton<MarketState>();
builder.Services.AddSingleton<OpportunityDetector>();
builder.Services.AddSingleton<DecantDetector>();
builder.Services.AddSingleton<PriceUpdateChannel>();
builder.Services.AddSingleton<FeedHealth>();
builder.Services.AddSingleton<OpportunityCache>();

// Wiki API client with REQUIRED descriptive User-Agent (Wiki blocks default agents).
builder.Services.AddHttpClient<IWikiPricesClient, WikiPricesClient>(c =>
{
    c.BaseAddress = new Uri("https://prices.runescape.wiki/api/v1/osrs/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(
        builder.Configuration["Wiki:UserAgent"]
        ?? "ArbitrageTracker/1.0 (personal flipping tool; contact stefan.daniel.schranz96@gmail.com)");
});

// Background services
builder.Services.AddHostedService<MappingLoader>();
builder.Services.AddHostedService<LatestPoller>();
builder.Services.AddHostedService<FiveMinutePoller>();
builder.Services.AddHostedService<HourlyPoller>();
builder.Services.AddHostedService<DetectionPipeline>();
builder.Services.AddHostedService<ProxyOutcomeJob>();

var app = builder.Build();

app.UseForwardedHeaders();

// Ensure the SQLite data directory exists, then apply migrations on startup.
Directory.CreateDirectory("data");
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ArbitrageDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapHub<OpportunitiesHub>("/hubs/opportunities");

app.Run();
