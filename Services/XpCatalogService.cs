using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using MUGS_bot.Helpers;
using MUGS_bot.Models;
using System.Globalization;

namespace MUGS_bot.Services;

public class XpCatalogService
{
    private readonly IHttpClientFactory _http;
    private readonly string _csvUrl;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<CatalogRow> _current = new();

    public XpCatalogService(IHttpClientFactory http, IConfiguration cfg)
    {
        _http = http;
        _csvUrl = cfg["XpSheet:CsvUrl"] ?? throw new InvalidOperationException("Missing XpSheet:CsvUrl");
    }


    public Task<List<CatalogRow>> GetCatalogAsync()
        => Task.FromResult(_current);


    public async Task<(bool ok, int rows, string? error)> RefreshAsync(CancellationToken ct = default)
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
                throw new InvalidOperationException("Empty CSV");
            csv.ReadHeader();

            var rows = new List<CatalogRow>();

            while (await csv.ReadAsync())
            {
                var record = csv.Parser.Record;
                if (record == null || record.Length == 0) continue;

                string? a = record.ElementAtOrDefault(0); // row number
                string? b = record.ElementAtOrDefault(1); // title
                string? c = record.ElementAtOrDefault(2);
                string? d = record.ElementAtOrDefault(3);
                string? e = record.ElementAtOrDefault(4);
                string? f = record.ElementAtOrDefault(5);
                string? g = record.ElementAtOrDefault(6);

                if (!int.TryParse(a?.Trim(), out var rowNumber) || rowNumber <= 0)
                    continue;

                var title = (b ?? "").Trim();
                if (string.IsNullOrWhiteSpace(title) ||
                    title.Contains("název", StringComparison.OrdinalIgnoreCase))
                    continue;

                int ParseXp(string? s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return 0;
                    var span = s.AsSpan().Trim();
                    if (int.TryParse(span, out var n)) return n;
                    var idx = span.IndexOf('X');
                    return idx > 0 && int.TryParse(new string(span[..idx]).Trim(), out n) ? n : 0;
                }

                var catXp = new Dictionary<XpCategory, int>
                {
                    [XpCategory.Socials] = ParseXp(c),
                    [XpCategory.Knowledge] = ParseXp(d),
                    [XpCategory.GameMaking] = ParseXp(e),
                    [XpCategory.Socializing] = ParseXp(f)
                };

                rows.Add(new CatalogRow
                {
                    RowNumber = rowNumber,
                    Title = title,
                    CatXp = catXp
                });
            }

            _current = rows;
            return (true, rows.Count, null);
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }
}