using NetCord.Services.ApplicationCommands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MUGS_bot.Services
{
    public sealed class HelpCatalog
    {
        public sealed record ParamInfo(string Name, string Type, bool Optional, string? Description);
        public sealed record CommandInfo(string Name, string Description, IReadOnlyList<ParamInfo> Params);

        private readonly List<CommandInfo> _commands = new();

        public IReadOnlyList<CommandInfo> Commands => _commands;

        public HelpCatalog(Assembly assemblyToScan)
        {
            var moduleTypes = assemblyToScan.GetTypes()
                .Where(t => !t.IsAbstract && t.BaseType != null &&
                            t.BaseType.IsGenericType &&
                            t.BaseType.GetGenericTypeDefinition() == typeof(ApplicationCommandModule<>));

            foreach (var t in moduleTypes)
            {
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    var cmdAttr = m.GetCustomAttribute<SlashCommandAttribute>();
                    if (cmdAttr is null) continue;

                    var cmdName = cmdAttr.Name;
                    var cmdDesc = cmdAttr.Description ?? "";

                    var pInfos = new List<ParamInfo>();
                    foreach (var p in m.GetParameters())
                    {
                        var pAttr = p.GetCustomAttribute<SlashCommandParameterAttribute>();
                        var pName = pAttr?.Name ?? p.Name ?? "param";
                        var pDesc = pAttr?.Description;

                        var isOptional = p.HasDefaultValue ||
                                         (p.ParameterType.IsGenericType &&
                                          p.ParameterType.GetGenericTypeDefinition() == typeof(Nullable<>)) ||
                                         (p.ParameterType.IsClass && IsNullableRefType(p));

                        var typeName = PrettyTypeName(p.ParameterType);
                        pInfos.Add(new ParamInfo(pName, typeName, isOptional, pDesc));
                    }

                    _commands.Add(new CommandInfo(cmdName, cmdDesc, pInfos));
                }
            }

            _commands.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }

        private static bool IsNullableRefType(ParameterInfo p)
        {
            var attr = p.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute");
            if (attr == null) return false;
            return true;
        }

        private static string PrettyTypeName(Type t)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
                t = Nullable.GetUnderlyingType(t)!;

            return t.Name switch
            {
                "Int32" => "int",
                "UInt64" => "ulong",
                "Boolean" => "bool",
                "String" => "string",
                _ => t.Name
            };
        }
    }
}
