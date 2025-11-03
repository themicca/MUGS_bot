using MUGS_bot.Models;

namespace MUGS_bot.Helpers;

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
    public Dictionary<XpCategory, List<(int level, int totalXp)>> Map { get; } =
        Enum.GetValues<XpCategory>().ToDictionary(c => c, _ => new List<(int, int)>());
}


public sealed class UserState
{
    public Dictionary<XpCategory, UserCategoryState> Categories { get; set; } =
        Enum.GetValues<XpCategory>().ToDictionary(c => c, _ => new UserCategoryState());

    public List<XpLogEntry> Logs { get; set; } = new();
}