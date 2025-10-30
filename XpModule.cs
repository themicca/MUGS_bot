using System.Text;
using Microsoft.AspNetCore.ResponseCompression;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace MUGS_bot;

public class XpModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly XpService _xp;
    private readonly XpCatalogService _catalog;
    private readonly LevelCatalogService _levels;
    private readonly RoleSyncService _roleSync;

    public XpModule(XpService xp, XpCatalogService catalog, LevelCatalogService levels, RoleSyncService roleSync)
    {
        _xp = xp;
        _catalog = catalog;
        _levels = levels;
        _roleSync = roleSync;
    }


    [SlashCommand("addsocialsxp", "Add XP in Socials category")]
    public async Task AddSocialsXpAsync(int amount, User user)
        => await AddCategoryXpAsync(XpCategory.Socials, amount, user);

    [SlashCommand("addknowledgexp", "Add XP in Knowledge category")]
    public async Task AddKnowledgeXpAsync(int amount, User user)
        => await AddCategoryXpAsync(XpCategory.Knowledge, amount, user);

    [SlashCommand("addgamemakingxp", "Add XP in GameMaking category")]
    public async Task AddGameMakingXpAsync(int amount, User user)
        => await AddCategoryXpAsync(XpCategory.GameMaking, amount, user);

    [SlashCommand("addsocializingxp", "Add XP in Socializing category")]
    public async Task AddSocializingXpAsync(int amount, User user)
        => await AddCategoryXpAsync(XpCategory.Socializing, amount, user);

    [SlashCommand("addhelpingxp", "Add XP in Helping category")]
    public async Task AddHelpingXpAsync(int amount, User user)
        => await AddCategoryXpAsync(XpCategory.Helping, amount, user);

    private async Task AddCategoryXpAsync(XpCategory category, int amount, User user)
    {
        if (Context.Guild is null)
        {
            await Respond("❌ This command can only be used in a server.");
            return;
        }

        var target = user ?? Context.User;

        var thresholds = await _levels.GetAsync();
        var (state, _) = _xp.Get(Context.Guild.Id, target.Id, thresholds);

        var beforeXp = state.Categories[category].Xp;
        var lvlBefore = state.Categories[category].Level;

        state.Categories[category].Xp += amount;
        var lvlAfter = state.Categories[category].Level =
            state.Categories[category].Level = _xp.LevelForXp(category, state.Categories[category].Xp, thresholds);

        // save (same as your _xp.Save())
        _xp.Save();

        // if level increased, sync roles
        if (lvlAfter > lvlBefore)
            await _roleSync.SyncCategoryRoleAsync(Context.Guild, target.Id, category, lvlAfter);

        // confirmation
        var msg = $"✅ Added **{amount} XP** to **{CatName(category)}** for <@{target.Id}>.\n" +
                  $"Now at {state.Categories[category].Xp} XP (Level {lvlAfter}).";
        await Respond(msg);
    }

    [SlashCommand("addxp", "Grant XP by catalog row number to a user (admin only).")]
    public async Task AddXpAsync(
        [SlashCommandParameter(Description = "Row number from the first column of the XP sheet")]
        int row,
        [SlashCommandParameter(Description = "User to credit")]
        User user,
        [SlashCommandParameter(Description = "MUGS Helping XP (optional)")]
        int? helpingXp = null)
    {
        if (Context.Guild is null)
        {
            await Respond("This command can only be used in a server.");
            return;
        }

        if (helpingXp < 0)
        {
            await Respond("You can't add negative xp.");
            return;
        }

        // simple admin gate: require ManageGuild or Administrator
        var member = await Context.Guild.GetUserAsync(Context.User.Id);
        var perms = member.GetPermissions(Context.Guild);
        if ((perms & Permissions.Administrator) != Permissions.Administrator ||
            (perms & Permissions.ManageGuild) != Permissions.ManageGuild)
        {
            await Respond("Only admins can assign xp.");
            return;
        }

        var (ok, msg, catalogRow, diffs) = await _xp.GrantByRowAsync(Context.Guild.Id, user.Id, Context.User.Id, row, helpingXp);
        if (!ok)
        {
            await Respond($"❌ {msg}");
            return;
        }

        foreach (var (cat, d) in diffs!)
            if (d.lvlAfter > d.lvlBefore)
                await _roleSync.SyncCategoryRoleAsync(Context.Guild, user.Id, cat, d.lvlAfter);

        // Nice response
        var sb = new StringBuilder();
        sb.AppendLine($"✅ Granted **row {catalogRow!.RowNumber}** — *{catalogRow!.Title}* to <@{user.Id}>");
        foreach (var kv in diffs!)
        {
            if (kv.Value.gained <= 0) continue;
            sb.AppendLine($"• **{CatName(kv.Key)}**: +{kv.Value.gained} XP | {kv.Value.before} XP → {kv.Value.after} XP (lvl {kv.Value.lvlAfter})");
        }

        await Respond(sb.ToString());
    }

    [SlashCommand("info", "Show per-category XP and level (defaults to yourself).")]
    public async Task InfoAsync([SlashCommandParameter(Description = "User (optional)")] User? user = null)
    {
        if (Context.Guild is null)
        {
            await Respond("This command can only be used in a server.");
            return;
        }
        var target = user?.Id ?? Context.User.Id;

        var thresholds = await _levels.GetAsync();
        var (state, nextAt) = _xp.Get(Context.Guild.Id, target, thresholds);

        var sb = new StringBuilder();
        sb.AppendLine($"**XP summary for <@{target}>**");
        foreach (var cat in Enum.GetValues<XpCategory>())
        {
            var s = state.Categories[cat];
            var nxt = nextAt[cat];
            sb.AppendLine($"• **{CatName(cat)}** — XP: {s.Xp}, Level: {s.Level}" +
                          (nxt == int.MaxValue ? " (max)" : $" (next at {nxt})"));
        }

        await Respond(sb.ToString());
    }

    [SlashCommand("infodetail", "Show detailed log of rows granted (defaults to yourself).")]
    public async Task InfoFullAsync([SlashCommandParameter(Description = "User (optional)")] User? user = null)
    {
        if (Context.Guild is null)
        {
            await Respond("This command can only be used in a server.");
            return;
        }
        var target = user?.Id ?? Context.User.Id;

        var thresholds = await _levels.GetAsync();
        var (state, nextAt) = _xp.Get(Context.Guild.Id, target, thresholds);

        if (state.Logs.Count == 0)
        {
            await Respond("No entries yet.");
            return;
        }

        // Build a compact table-like text, chunk under Discord 2000 chars
        var lines = new List<string>();
        lines.Add($"**Log for <@{target}>** (latest first)");
        lines.Add("```");
        lines.Add("Date (UTC)          | Row | Title                          | Gained");
        lines.Add("--------------------+-----+-------------------------------+-----------------------------");
        foreach (var log in state.Logs.AsEnumerable().Reverse())
        {
            string gained = string.Join(", ", log.CatXp.Select(kv => $"{ShortCat(kv.Key)}:{kv.Value}"));
            lines.Add($"{log.Date:yyyy-MM-dd HH:mm} | {log.RowNumber,3} | {Trunc(log.Title, 29),-29} | {gained}");
        }
        lines.Add("```");

        foreach (var chunk in ChunkByLength(string.Join('\n', lines), 1900))
            await Respond(chunk);
    }

    [SlashCommand("xprefresh", "Force reload of the XP sheet (admin only).")]
    public async Task XpRefreshAsync()
    {
        if (Context.Guild is null)
        {
            await Respond("Server only.");
            return;
        }

        var member = await Context.Guild.GetUserAsync(Context.User.Id);
        var perms = member.GetPermissions(Context.Guild);
        if ((perms & Permissions.Administrator) != Permissions.Administrator &&
            (perms & Permissions.ManageGuild) != Permissions.ManageGuild)
        {
            await Respond("Need Manage Server.");
            return;
        }

        // Trigger a fetch so we can report row count
        var rows = await _catalog.GetCatalogAsync();
        await Respond($"🔄 Reloaded XP catalog. Rows: {rows.Count}.");
    }

    [SlashCommand("lvlrefresh", "Force reload of the Level sheet (admin only).")]
    public async Task LvlRefreshAsync()
    {
        if (Context.Guild is null)
        {
            await Respond("Server only.");
            return;
        }

        var member = await Context.Guild.GetUserAsync(Context.User.Id);
        var perms = member.GetPermissions(Context.Guild);
        if ((perms & Permissions.Administrator) != Permissions.Administrator &&
            (perms & Permissions.ManageGuild) != Permissions.ManageGuild)
        {
            await Respond("Need Manage Server.");
            return;
        }

        var (ok, err) = await _levels.RefreshAsync();
        await Respond(ok ? "🔄 Level thresholds reloaded." : $"❌ {err}");
    }

    [SlashCommand("resetuser", "Reset XP, levels, logs and level roles for a user (admin only).")]
    public async Task ResetUserCmdAsync([SlashCommandParameter(Description = "User to reset (optional)")] User? user = null)
    {
        if (Context.Guild is null)
        {
            await Respond("This command can only be used in a server.");
            return;
        }

        // defer (ephemeral)
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        // permissions
        var member = await Context.Guild.GetUserAsync(Context.User.Id);
        var perms = member.GetPermissions(Context.Guild);
        if ((perms & Permissions.Administrator) != Permissions.Administrator &&
            (perms & Permissions.ManageGuild) != Permissions.ManageGuild)
        {
            await ModifyResponseAsync(action => action.Content = "You need **Manage Server** to use this.");
            return;
        }

        var target = user ?? Context.User;

        // reset data
        _xp.ResetUser(Context.Guild.Id, target.Id);

        // remove all level roles
        await _roleSync.RemoveAllCategoryRolesAsync(Context.Guild, target.Id);

        await ModifyResponseAsync(action => action.Content = $"🧹 Reset complete for <@{target.Id}> — all XP & levels set to 0, logs cleared, level roles removed.");
    }

    // Helpers
    private static string CatName(XpCategory c) => c switch
    {
        XpCategory.Socializing => "Socializace",
        XpCategory.Socials => "Sociální sítě",
        XpCategory.Knowledge => "Vědomosti",
        XpCategory.GameMaking => "Tvorba Her",
        XpCategory.Helping => "Pomoc MUGS",
        _ => c.ToString()
    };

    private static string ShortCat(XpCategory c) => c switch
    {
        XpCategory.Socializing => "SLZ",
        XpCategory.Socials => "SOC",
        XpCategory.Knowledge => "EDU",
        XpCategory.GameMaking => "GMK",
        XpCategory.Helping => "HLP",
        _ => c.ToString()
    };

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];

    private static IEnumerable<string> ChunkByLength(string text, int max)
    {
        if (text.Length <= max) { yield return text; yield break; }
        int idx = 0;
        while (idx < text.Length)
        {
            int len = Math.Min(max, text.Length - idx);
            yield return text.Substring(idx, len);
            idx += len;
        }
    }

    private async Task Respond(string msg)
    {
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties
        {
            Content = msg,
            Flags = MessageFlags.Ephemeral
        }));
    }
}