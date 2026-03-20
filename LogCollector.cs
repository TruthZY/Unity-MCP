using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace McpUnity
{
    /// <summary>
    /// Unity 日志收集器 - 捕获并存储控制台日志
    /// </summary>
    [InitializeOnLoad]
    public static class LogCollector
    {
        private const int MaxLogs = 50;
        private static readonly List<LogEntry> Logs = new();
        private static bool _isInitialized = false;

        static LogCollector()
        {
            // 延迟初始化，等待编辑器准备就绪
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            if (_isInitialized) return;

            Application.logMessageReceived += OnLogMessage;
            _isInitialized = true;
            Debug.Log("[MCP] LogCollector initialized");
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            // 避免收集自己的日志造成循环
            if (condition.Contains("[MCP]")) return;

            var entry = new LogEntry
            {
                timestamp = DateTime.Now.ToString("HH:mm:ss"),
                message = condition,
                stackTrace = stackTrace,
                logType = type.ToString()
            };

            lock (Logs)
            {
                Logs.Add(entry);
                // 保持最多50条
                while (Logs.Count > MaxLogs)
                {
                    Logs.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// 获取日志列表
        /// </summary>
        public static List<LogEntry> GetLogs(string filter = null, string logType = null, string search = null)
        {
            lock (Logs)
            {
                var query = Logs.AsEnumerable();

                // 按类型过滤
                if (!string.IsNullOrEmpty(logType))
                {
                    query = query.Where(l => l.logType.Equals(logType, StringComparison.OrdinalIgnoreCase));
                }

                // 按级别过滤 (error, warning, log)
                if (!string.IsNullOrEmpty(filter))
                {
                    switch (filter.ToLower())
                    {
                        case "error":
                            query = query.Where(l => l.logType == "Error" || l.logType == "Exception" || l.logType == "Assert");
                            break;
                        case "warning":
                            query = query.Where(l => l.logType == "Warning");
                            break;
                        case "log":
                            query = query.Where(l => l.logType == "Log");
                            break;
                    }
                }

                // 搜索关键词
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(l => 
                        l.message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        l.stackTrace.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                return query.ToList();
            }
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        public static void ClearLogs()
        {
            lock (Logs)
            {
                Logs.Clear();
            }
        }

        /// <summary>
        /// 获取日志数量
        /// </summary>
        public static int GetLogCount()
        {
            lock (Logs)
            {
                return Logs.Count;
            }
        }
    }

    /// <summary>
    /// 日志条目
    /// </summary>
    [Serializable]
    public class LogEntry
    {
        public string timestamp;
        public string message;
        public string stackTrace;
        public string logType;
    }
}
