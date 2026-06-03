using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using McpUnity.Core;

namespace McpUnity.Modules
{
    /// <summary>
    /// 系统模块 - 提供帮助、批量执行等系统级功能
    /// </summary>
    [McpModule("system")]
    public class SystemModule : IMcpModule
    {
        public string ModuleName => "system";

        [McpCommand("help", "返回所有可用命令的名称、参数说明和描述")]
        [McpParameter("filter", "按模块名过滤(可选)，如 scene, lua, prefab", Required = false, Example = "scene")]
        public object Help(Dictionary<string, string> parameters)
        {
            string filter = parameters.TryGetValue("filter", out var f) ? f : null;

            var commands = new List<CommandHelpInfo>();

            var moduleTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t => t.GetCustomAttribute<McpModuleAttribute>() != null &&
                           typeof(IMcpModule).IsAssignableFrom(t) &&
                           !t.IsInterface &&
                           !t.IsAbstract);

            foreach (var moduleType in moduleTypes)
            {
                var moduleAttr = moduleType.GetCustomAttribute<McpModuleAttribute>();
                string moduleName = moduleAttr.ModuleName;

                if (!string.IsNullOrEmpty(filter) &&
                    !moduleName.Equals(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var methods = moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<McpCommandAttribute>() != null);

                foreach (var method in methods)
                {
                    var cmdAttr = method.GetCustomAttribute<McpCommandAttribute>();
                    var paramAttrs = method.GetCustomAttributes<McpParameterAttribute>();

                    commands.Add(new CommandHelpInfo
                    {
                        command = $"{moduleName}_{cmdAttr.CommandName}",
                        description = cmdAttr.Description ?? "",
                        parameters = paramAttrs.Select(p => new ParameterHelpInfo
                        {
                            name = p.Name,
                            description = p.Description ?? "",
                            required = p.Required,
                            defaultValue = p.DefaultValue ?? ""
                        }).ToArray()
                    });
                }
            }

            commands.Sort((a, b) => string.Compare(a.command, b.command, StringComparison.Ordinal));

            return new HelpResult
            {
                success = true,
                totalCount = commands.Count,
                commands = commands.ToArray()
            };
        }

        #region Result Classes

        [Serializable]
        public class HelpResult
        {
            public bool success;
            public int totalCount;
            public CommandHelpInfo[] commands;
        }

        [Serializable]
        public class CommandHelpInfo
        {
            public string command;
            public string description;
            public ParameterHelpInfo[] parameters;
        }

        [Serializable]
        public class ParameterHelpInfo
        {
            public string name;
            public string description;
            public bool required;
            public string defaultValue;
        }

        #endregion
    }
}
