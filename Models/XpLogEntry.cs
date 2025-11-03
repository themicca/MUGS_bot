using MUGS_bot.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MUGS_bot.Models
{
    public sealed record XpLogEntry
    {
        public DateTimeOffset Date { get; init; }
        public int RowNumber { get; init; }
        public string Title { get; init; } = "";
        public Dictionary<XpCategory, int> CatXp { get; init; } = new();
        public ulong GrantedByUserId { get; init; }
    }
}
