using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MUGS_bot
{
    public class LevelCatalogRefresher : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly TimeSpan _interval;

        public LevelCatalogRefresher(IServiceProvider sp, IConfiguration cfg)
        {
            _sp = sp;
            var mins = int.TryParse(cfg["LevelSheet:AutoRefreshMinutes"], out var m) ? Math.Max(1, m) : 15;
            _interval = TimeSpan.FromMinutes(mins);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
            var svc = scope.ServiceProvider.GetRequiredService<LevelCatalogService>();
            var (ok, err) = await svc.RefreshAsync(ct);
            Console.WriteLine(ok ? "[LVL] Level sheet refreshed." : $"[LVL] Refresh failed: {err}");
        }
    }
}
