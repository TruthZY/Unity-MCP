using System;
using System.Collections.Generic;

namespace McpUnity.Core
{
    /// <summary>
    /// MCP模块接口
    /// </summary>    
    public interface IMcpModule
    {
        string ModuleName { get; }
    }

    /// <summary>
    /// MCP命令特性 - 标记方法为MCP命令
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class McpCommandAttribute : Attribute
    {
        public string CommandName { get; }
        
        public McpCommandAttribute(string commandName)
        {
            CommandName = commandName;
        }
    }

    /// <summary>
    /// MCP模块特性 - 标记类为MCP模块
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class McpModuleAttribute : Attribute
    {
        public string ModuleName { get; }
        
        public McpModuleAttribute(string moduleName)
        {
            ModuleName = moduleName;
        }
    }

    /// <summary>
    /// 命令处理器委托
    /// </summary>
    public delegate object McpCommandHandler(Dictionary<string, string> parameters);
}
