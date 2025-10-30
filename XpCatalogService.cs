using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MUGS_bot;

public class XpCatalogService
{
    private readonly IHttpClientFactory _http;
    private readonly string _csvUrl;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<CatalogRow> _current = new();   // in-memory snapshot

    public XpCatalogService(IHttpClientFactory http, IConfiguration cfg)
    {
        _http = http;
        _csvUrl = cfg["XpSheet:CsvUrl"] ?? throw new InvalidOperationException("Missing XpSheet:CsvUrl");
    }

    /// Returns the latest in-memory snapshot (never hits the network).
    public Task<List<CatalogRow>> GetCatalogAsync()
        => Task.FromResult(_current);

    /// Refreshes the in-memory snapshot from Google (network).
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

            // 1) Read and discard the header ONCE
            if (!await csv.ReadAsync())
                throw new InvalidOperationException("Empty CSV");
            csv.ReadHeader();

            // 2) Iterate records reliably
            var rows = new List<CatalogRow>();

            while (await csv.ReadAsync())
            {
                // Get the whole record in one shot
                var record = csv.Parser.Record; // string[] of the current row
                if (record == null || record.Length == 0) continue;

                // Safely index columns (A=0, B=1, C=2, ...)
                string? a = record.ElementAtOrDefault(0); // row number
                string? b = record.ElementAtOrDefault(1); // title
                string? c = record.ElementAtOrDefault(2);
                string? d = record.ElementAtOrDefault(3);
                string? e = record.ElementAtOrDefault(4);
                string? f = record.ElementAtOrDefault(5);
                string? g = record.ElementAtOrDefault(6);

                // Parse number in column A
                if (!int.TryParse(a?.Trim(), out var rowNumber) || rowNumber <= 0)
                    continue; // skip header/title/blank rows

                var title = (b ?? "").Trim();
                if (string.IsNullOrWhiteSpace(title) ||
                    title.Contains("název", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Parse category cells like "8 XP"
                int ParseXp(string? s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return 0;
                    var span = s.AsSpan().Trim();
                    // fast path: just number
                    if (int.TryParse(span, out var n)) return n;
                    // slow path: look for digits before "XP"
                    var idx = span.IndexOf('X'); // cheap check
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

            // atomically swap your cache
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