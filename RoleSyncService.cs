using Microsoft.Extensions.Configuration;
using NetCord;
using NetCord.Gateway;

namespace MUGS_bot
{
    public class RoleSyncService
    {
        private readonly IConfiguration _cfg;

        public RoleSyncService(IConfiguration cfg) => _cfg = cfg;

        private string Prefix(XpCategory c) => _cfg[$"LevelRoles:Prefix:{c}"] ?? c.ToString();
        private Color RoleColor(XpCategory c)
        {
            var hex = _cfg[$"LevelRoles:ColorHex:{c}"]?.TrimStart('#') ?? "FFFFFF";
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
                return new Color((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            return new Color(255, 255, 255);
        }

        public async Task SyncCategoryRoleAsync(Guild guild, ulong userId, XpCategory cat, int level)
        {
            if (level <= 0) return;

            var wantedName = $"{Prefix(cat)} {level}";
            var color = RoleColor(cat);

            // 1) ensure role exists
            var roles = await guild.GetRolesAsync();
            var role = roles.FirstOrDefault(r => string.Equals(r.Name, wantedName, StringComparison.Ordinal));

            if (role is null)
            {
                role = await guild.CreateRoleAsync(new()
                {
                    Name = wantedName,
                    Color = color,
                    Hoist = false,
                    Mentionable = false
                });
            }

            // 2) remove any previous level-roles for this category
            var prefix = Prefix(cat);
            var toRemove = roles.Where(r => r.Name.StartsWith(prefix + " ", StringComparison.Ordinal) && r.Id != role.Id)
                                .Select(r => r.Id)
                                .ToList();
            if (toRemove.Count > 0)
            {
                var member = await guild.GetUserAsync(userId);
                var memberRoleIds = member.RoleIds.ToHashSet();
                var removeIds = toRemove.Where(memberRoleIds.Contains).ToList();
                if (removeIds.Count > 0)
                {
                    foreach (var memberRoleId in removeIds)
                    {
                        await member.RemoveRoleAsync(memberRoleId);
                    }
                }
            }

            // 3) add the wanted role
            await guild.AddUserRoleAsync(userId, role.Id);
        }

        public async Task RemoveAllCategoryRolesAsync(Guild guild, ulong userId)
        {
            // build all level-role prefixes, e.g. "Sociální sítě Lv. "
            var prefixes = Enum.GetValues<XpCategory>()
                .Select(c => Prefix(c) + " ")
                .ToList();

            var roles = await guild.GetRolesAsync();

            // every role that looks like a category level role
            var candidateRoleIds = roles
                .Where(r => prefixes.Any(p => r.Name.StartsWith(p, StringComparison.Ordinal)))
                .Select(r => r.Id)
                .ToList();

            if (candidateRoleIds.Count == 0)
                return;

            var member = await guild.GetUserAsync(userId);
            var memberRoleIds = member.RoleIds.ToHashSet();

            var removeIds = candidateRoleIds.Where(memberRoleIds.Contains).ToList();
            if (removeIds.Count > 0)
                foreach (var memberRoleId in removeIds)
                {
                    await member.RemoveRoleAsync(memberRoleId);
                }
        }
    }
}
