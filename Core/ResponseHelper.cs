using System;
using UnityEngine;

namespace McpUnity.Core
{
    /// <summary>
    /// MCP响应辅助类
    /// </summary>
    public static class ResponseHelper
    {
        /// <summary>
        /// 创建成功响应
        /// </summary>
        public static string Success(object data)
        {
            string dataJson = data != null ? JsonUtility.ToJson(data) : "{}";
            // 如果 dataJson 是简单值（不是对象），包装一下
            if (!dataJson.StartsWith("{") && !dataJson.StartsWith("["))
            {
                dataJson = $"\"{dataJson}\"";
            }
            
            var response = new McpResponse
            {
                success = true,
                data = dataJson
            };
            return JsonUtility.ToJson(new FinalResponse { response = response });
        }

        /// <summary>
        /// 创建错误响应
        /// </summary>
        public static string Error(string errorMessage)
        {
            var response = new McpResponse
            {
                success = false,
                error = errorMessage
            };
            return JsonUtility.ToJson(new FinalResponse { response = response });
        }

        /// <summary>
        /// 序列化响应对象
        /// </summary>
        public static string Serialize(object data)
        {
            return JsonUtility.ToJson(data);
        }

        [Serializable]
        private class McpResponse
        {
            public bool success;
            public string data;
            public string error;
        }

        [Serializable]
        private class FinalResponse
        {
            public McpResponse response;
        }
    }

    /// <summary>
    /// 通用结果类
    /// </summary>
    [Serializable]
    public class CommandResult
    {
        public bool success;
        public string error;
    }

    /// <summary>    
    /// 带数据的结果类
    /// </summary>
    [Serializable]
    public class CommandResult<T> : CommandResult
    {
        public T data;
    }
}
