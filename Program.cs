using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using System.Reflection;

namespace MUGS_bot
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Configuration.AddUserSecrets<Program>();

            var token = builder.Configuration["Discord:Token"];
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("❌ Missing bot token in appsettings.json (Discord:Token).");
                return;
            }

            builder.Services
                .AddDiscordGateway(options =>
                {
                    options.Token = token;
                    options.Intents = GatewayIntents.Guilds | GatewayIntents.GuildUsers;
                })
                .AddApplicationCommands();   // enable slash commands

            builder.Services.AddHttpClient();

            builder.Services.AddSingleton<XpCatalogService>();
            builder.Services.AddSingleton<XpService>();
            builder.Services.AddHostedService<XpCatalogRefresher>();
            builder.Services.AddSingleton<LevelCatalogService>();
            builder.Services.AddHostedService<LevelCatalogRefresher>();
            builder.Services.AddSingleton<RoleSyncService>();

            var app = builder.Build();

            app.AddModules(typeof(XpModule).Assembly);

            await app.RunAsync();
        }
    }
}
