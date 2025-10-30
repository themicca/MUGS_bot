using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MUGS_bot;

public class XpCatalogRefresher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly TimeSpan _interval;

    public XpCatalogRefresher(IServiceProvider sp, IConfiguration cfg)
    {
        _sp = sp;
        var mins = int.TryParse(cfg["XpSheet:AutoRefreshMinutes"], out var m) ? Math.Max(1, m) : 15;
        _interval = TimeSpan.FromMinutes(mins);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // first load on startup
        await RefreshOnce(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                await RefreshOnce(stoppingToken);
            }
            catch (TaskCanceledException) { }
        }
    }

    private async Task RefreshOnce(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<XpCatalogService>();
        var (ok, rows, err) = await svc.RefreshAsync(ct);
        Console.WriteLine(ok
            ? $"[XP] Catalog refreshed: {rows} rows"
            : $"[XP] Catalog refresh failed: {err}");
    }
}