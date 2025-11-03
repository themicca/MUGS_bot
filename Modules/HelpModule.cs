using MUGS_bot.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using System.Text;

namespace MUGS_bot.Modules
{

    public class HelpModule : ApplicationCommandModule<ApplicationCommandContext>
    {
        private readonly HelpCatalog _help;

        public HelpModule(HelpCatalog help) => _help = help;

        [SlashCommand("help", "List available commands")]
        public async Task HelpAsync()
        {
            await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var sb = new StringBuilder();
            sb.AppendLine("**Available commands**");
            foreach (var c in _help.Commands)
                sb.AppendLine($"• `/{c.Name}` — {c.Description}");

            await ModifyResponseAsync(a => a.Content = sb.ToString());
        }

        [SlashCommand("helpfull", "List commands with parameters (required vs optional)")]
        public async Task HelpFullAsync()
        {
            await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var sb = new StringBuilder();
            sb.AppendLine("**Commands (full)**");
            foreach (var c in _help.Commands)
            {
                sb.Append($"• `/{c.Name}`");
                if (c.Params.Count > 0)
                {
                    sb.Append(" ");
                    sb.Append(string.Join(" ",
                        c.Params.Select(p => p.Optional
                            ? $"[`{p.Name}:{p.Type}`]"   // optional: in brackets
                            : $"<{p.Name}:{p.Type}>"))); // required: in angle brackets
                }
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(c.Description))
                    sb.AppendLine($"    – {c.Description}");

                foreach (var p in c.Params)
                {
                    var tag = p.Optional ? "(optional)" : "(required)";
                    var desc = string.IsNullOrWhiteSpace(p.Description) ? "" : $" — {p.Description}";
                    sb.AppendLine($"    • `{p.Name}`: {p.Type} {tag}{desc}");
                }
            }

            await ModifyResponseAsync(a => a.Content = sb.ToString());
        }
    }
}
