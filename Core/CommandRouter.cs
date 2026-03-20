using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace McpUnity.Core
{
    /// <summary>
    /// MCP命令路由器 - 自动扫描并注册所有模块和命令
    /// </summary>
    public static class CommandRouter
    {
        private static readonly Dictionary<string, McpCommandHandler> Commands = new();
        private static bool _isInitialized = false;

        /// <summary>
        /// 初始化路由器，自动扫描所有模块
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            Commands.Clear();
            
            // 扫描所有程序集中的MCP模块
            var moduleTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.GetCustomAttribute<McpModuleAttribute>() != null && 
                           typeof(IMcpModule).IsAssignableFrom(t) && 
                           !t.IsInterface && 
                           !t.IsAbstract);

            foreach (var moduleType in moduleTypes)
            {
                RegisterModule(moduleType);
            }

            _isInitialized = true;
            Debug.Log($"[MCP] CommandRouter initialized with {Commands.Count} commands");
        }

        /// <summary>
        /// 注册单个模块
        /// </summary>
        private static void RegisterModule(Type moduleType)
        {
            var moduleAttr = moduleType.GetCustomAttribute<McpModuleAttribute>();
            var moduleName = moduleAttr.ModuleName;
            
            // 创建模块实例
            var module = Activator.CreateInstance(moduleType) as IMcpModule;
            
            // 扫描模块中的所有命令方法
            var methods = moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<McpCommandAttribute>() != null);

            foreach (var method in methods)
            {
                var cmdAttr = method.GetCustomAttribute<McpCommandAttribute>();
                var fullCommandName = $"{moduleName}_{cmdAttr.CommandName}";
                
                // 创建委托
                var handler = CreateHandler(module, method);
                Commands[fullCommandName] = handler;
                
                Debug.Log($"[MCP] Registered command: {fullCommandName}");
            }
        }

        /// <summary>
        /// 创建命令处理器委托
        /// </summary>
        private static McpCommandHandler CreateHandler(IMcpModule module, MethodInfo method)
        {
            return (Dictionary<string, string> parameters) =>
            {
                try
                {
                    return method.Invoke(module, new object[] { parameters });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
            };
        }

        /// <summary>
        /// 执行命令
        /// </summary>
        public static object Execute(string commandName, Dictionary<string, string> parameters)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            if (!Commands.TryGetValue(commandName, out var handler))
            {
                throw new Exception($"Unknown command: {commandName}");
            }

            return handler(parameters);
        }

        /// <summary>
        /// 检查命令是否存在
        /// </summary>
        public static bool HasCommand(string commandName)
        {
            if (!_isInitialized)
            {
                Initialize();
            }
            return Commands.ContainsKey(commandName);
        }

        /// <summary>
        /// 获取所有注册的命令名
        /// </summary>
        public static string[] GetAllCommands()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
            return Commands.Keys.ToArray();
        }
    }
}
