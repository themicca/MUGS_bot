using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using MUGS_bot.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MUGS_bot.Services
{
    public class LevelCatalogService
    {
        private readonly IHttpClientFactory _http;
        private readonly string _csvUrl;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private LevelThresholds _current = new();

        public LevelCatalogService(IHttpClientFactory http, IConfiguration cfg)
        {
            _http = http;
            _csvUrl = cfg["LevelSheet:CsvUrl"] ?? throw new InvalidOperationException("Missing LevelSheet:CsvUrl");
        }

        public Task<LevelThresholds> GetAsync() => Task.FromResult(_current);

        public async Task<(bool ok, string? error)> RefreshAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
            try
            {
                var client = _http.CreateClient();
                using var stream = await client.GetStreamAsync(_csvUrl, ct);
                using var reader = new StreamReader(stream);
                using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    DetectDelimiter = true,
                    TrimOptions = TrimOptions.Trim,
                    BadDataFound = null,
                    MissingFieldFound = null,
                });

                if (!await csv.ReadAsync())
                    throw new InvalidOperationException("Empty level CSV");
                csv.ReadHeader();

                var next = new LevelThresholds();

                while (await csv.ReadAsync())
                {
                    var rec = csv.Parser.Record;
                    if (rec is null || rec.Length == 0) continue;

                    string? a = rec.ElementAtOrDefault(0);  // Socials Level
                    string? c = rec.ElementAtOrDefault(2);  // Socials Total
                    string? d = rec.ElementAtOrDefault(3);  // Knowledge Level
                    string? f = rec.ElementAtOrDefault(5);  // Knowledge Total
                    string? g = rec.ElementAtOrDefault(6);  // GameMaking Level
                    string? i = rec.ElementAtOrDefault(8);  // GameMaking Total
                    string? j = rec.ElementAtOrDefault(9);  // Socializing Level
                    string? l = rec.ElementAtOrDefault(11); // Socializing Total
                    string? m = rec.ElementAtOrDefault(12); // Helping Level
                    string? o = rec.ElementAtOrDefault(14); // Helping Total

                    static int ParseInt(string? s) => int.TryParse(s?.Trim(), out var n) ? n : 0;

                    void TryAdd(XpCategory cat, string? lvlStr, string? totalStr)
                    {
                        var lvl = ParseInt(lvlStr);
                        var total = ParseInt(totalStr);
                        if (lvl > 0) next.Map[cat].Add((lvl, total));
                    }

                    TryAdd(XpCategory.Socials, a, c);
                    TryAdd(XpCategory.Knowledge, d, f);
                    TryAdd(XpCategory.GameMaking, g, i);
                    TryAdd(XpCategory.Socializing, j, l);
                    TryAdd(XpCategory.Helping, m, o);
                }

                // sort each list by total xp asc
                foreach (var list in next.Map.Values)
                    list.Sort((x, y) => x.totalXp.CompareTo(y.totalXp));

                _current = next;
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
