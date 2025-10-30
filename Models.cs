namespace MUGS_bot;

public enum XpCategory
{
    Socializing,      // socializace
    Socials,           // sociální sítě
    Knowledge,        // vědomosti
    GameMaking,        // Tvorba her
    Helping            // pomoc MUGS
}

public sealed class LevelThresholds
{
    // For each category: list of (level, totalXpNeeded), sorted ascending by totalXp
    public Dictionary<XpCategory, List<(int level, int totalXp)>> Map { get; } =
        Enum.GetValues<XpCategory>().ToDictionary(c => c, _ => new List<(int, int)>());
}


public sealed record CatalogRow
{
    public int RowNumber { get; init; }           // value from first column (your custom index)
    public string Title { get; init; } = "";
    public Dictionary<XpCategory, int> CatXp { get; init; } = new();
}

public sealed record XpLogEntry
{
    public DateTimeOffset Date { get; init; }
    public int RowNumber { get; init; }
    public string Title { get; init; } = "";
    public Dictionary<XpCategory, int> CatXp { get; init; } = new();
    public ulong GrantedByUserId { get; init; } // who ran /addxp
}

public sealed class UserCategoryState
{
    public int Xp { get; set; }
    public int Level { get; set; }
}

public sealed class UserState
{
    public Dictionary<XpCategory, UserCategoryState> Categories { get; set; } =
        Enum.GetValues<XpCategory>().ToDictionary(c => c, _ => new UserCategoryState());

    public List<XpLogEntry> Logs { get; set; } = new();
}