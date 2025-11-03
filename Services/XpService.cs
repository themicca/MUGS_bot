using MUGS_bot.Helpers;
using MUGS_bot.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace MUGS_bot.Services;

public class XpService
{
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "xpdata.json");
    private readonly ConcurrentDictionary<(ulong GuildId, ulong UserId), UserState> _data = new();
    private readonly XpCatalogService _catalog;
    private readonly LevelCatalogService _levels;
    private readonly RoleSyncService _roleSync;

    public XpService(XpCatalogService catalog, LevelCatalogService levels, RoleSyncService roleSync)
    {
        _catalog = catalog;
        _levels = levels;
        _roleSync = roleSync;
        if (File.Exists(_path))
        {
            try
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<List<SerializedRecord>>(json) ?? [];
                foreach (var r in loaded)
                    _data[(r.GuildId, r.UserId)] = r.ToUserState();
            }
            catch { /* ignore */ }
        }
    }

    public async Task<(bool ok, string message, CatalogRow? row, Dictionary<XpCategory, (int before, int after, int gained, int lvlBefore, int lvlAfter)>? diffs)>
        GrantByRowAsync(ulong guildId, ulong targetUserId, ulong grantedByUserId, int rowNumber, int? helpXp)
    {
        var catalog = await _catalog.GetCatalogAsync();
        var row = catalog.FirstOrDefault(r => r.RowNumber == rowNumber);
        if (row is null)
            return (false, $"Row {rowNumber} not found in the catalog.", null, null);

        var state = _data.GetOrAdd((guildId, targetUserId), _ => new UserState());
        if (helpXp != null) row.CatXp[XpCategory.Helping] = (int)helpXp;

        var thresholds = await _levels.GetAsync();

        var diffs = new Dictionary<XpCategory, (int before, int after, int gained, int lvlBefore, int lvlAfter)>();

        foreach (var (cat, gain) in row.CatXp)
        {
            if (gain <= 0) continue;

            var catState = state.Categories[cat];
            var beforeXp = catState.Xp;
            var lvlBefore = catState.Level;

            catState.Xp += gain;

            var lvlAfter = LevelForXp(cat, catState.Xp, thresholds);
            catState.Level = lvlAfter;

            diffs[cat] = (beforeXp, catState.Xp, gain, lvlBefore, lvlAfter);
        }

        // Log
        state.Logs.Add(new XpLogEntry
        {
            Date = DateTimeOffset.UtcNow,
            RowNumber = row.RowNumber,
            Title = row.Title,
            CatXp = row.CatXp.Where(kv => kv.Value > 0).ToDictionary(k => k.Key, v => v.Value),
            GrantedByUserId = grantedByUserId
        });

        Save();

        return (true, "OK", row, diffs);
    }

    public (UserState state, Dictionary<XpCategory, int> nextLevelAt) Get(ulong guildId, ulong userId, LevelThresholds thresholds)
    {
        var s = _data.GetOrAdd((guildId, userId), _ => new UserState());
        var next = s.Categories.ToDictionary(
            kv => kv.Key, kv =>
            {
                var list = thresholds.Map[kv.Key];
                var curLvl = LevelForXp(kv.Key, kv.Value.Xp, thresholds);
                var target = list.FirstOrDefault(t => t.level == curLvl + 1);
                return target.totalXp == 0 ? int.MaxValue : target.totalXp;
            });
        return (s, next);
    }

    public void ResetUser(ulong guildId, ulong userId)
    {
        var state = _data.GetOrAdd((guildId, userId), _ => new UserState());

        foreach (var cat in Enum.GetValues<XpCategory>())
        {
            var c = state.Categories[cat];
            c.Xp = 0;
            c.Level = 0;
        }

        state.Logs.Clear();
        Save();
    }

    public int LevelForXp(XpCategory cat, int xp, LevelThresholds t)
    {
        var list = t.Map[cat];
        int lvl = 0;
        foreach (var (l, total) in list)
            if (xp >= total) lvl = l; else break;
        return lvl;
    }

    public void Save()
    {
        var list = _data.Select(kv => new SerializedRecord(kv.Key.GuildId, kv.Key.UserId, kv.Value)).ToList();
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private sealed record SerializedRecord(ulong GuildId, ulong UserId, UserState State)
    {
        public UserState ToUserState() => State;
    }
}