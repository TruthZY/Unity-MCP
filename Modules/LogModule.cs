using System;
using System.Collections.Generic;
using System.Linq;
using McpUnity.Core;
using UnityEngine;

namespace McpUnity.Modules
{
    /// <summary>
    /// 日志管理模块
    /// </summary>
    [McpModule("log")]
    public class LogModule : IMcpModule
    {
        public string ModuleName => "log";

        [McpCommand("get_logs")]
        public object GetLogs(Dictionary<string, string> parameters)
        {
            string filter = GetParam(parameters, "filter");
            string logType = GetParam(parameters, "logType");
            string search = GetParam(parameters, "search");
            string limitStr = GetParam(parameters, "limit", "50");

            int limit = int.TryParse(limitStr, out int l) ? l : 50;
            if (limit > 50) limit = 50;

            var logs = LogCollector.GetLogs(filter, logType, search);
            var limitedLogs = logs.Take(limit).ToList();

            return new LogsResult
            {
                success = true,
                totalCount = logs.Count,
                returnedCount = limitedLogs.Count,
                logs = limitedLogs.Select(l => new LogInfo
                {
                    timestamp = l.timestamp,
                    message = l.message,
                    logType = l.logType
                }).ToArray()
            };
        }

        [McpCommand("clear_logs")]
        public object ClearLogs(Dictionary<string, string> parameters)
        {
            LogCollector.ClearLogs();
            return new ClearLogsResult
            {
                success = true,
                message = "Logs cleared successfully"
            };
        }

        [McpCommand("get_log_count")]
        public object GetLogCount(Dictionary<string, string> parameters)
        {
            int count = LogCollector.GetLogCount();
            return new LogCountResult
            {
                success = true,
                count = count
            };
        }

        #region Helper Methods

        private string GetParam(Dictionary<string, string> parameters, string key, string defaultValue = "")
        {
            return parameters.TryGetValue(key, out var value) ? value : defaultValue;
        }

        #endregion

        #region Result Classes

        [Serializable]
        public class LogsResult
        {
            public bool success;
            public int totalCount;
            public int returnedCount;
            public LogInfo[] logs;
        }

        [Serializable]
        public class LogInfo
        {
            public string timestamp;
            public string message;
            public string logType;
        }

        [Serializable]
        public class ClearLogsResult
        {
            public bool success;
            public string message;
        }

        [Serializable]
        public class LogCountResult
        {
            public bool success;
            public int count;
        }

        #endregion
    }
}
