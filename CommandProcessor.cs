using System;
using System.Collections.Generic;
using McpUnity.Core;
using UnityEngine;

namespace McpUnity
{
    /// <summary>
    /// 命令处理器 - 使用 CommandRouter 分发命令到各个模块
    /// </summary>
    public static class CommandProcessor
    {
        public static string Process(string jsonRequest)
        {
            try
            {
                // 手动解析JSON获取命令名
                string command = ExtractJsonValue(jsonRequest, "command");
                string parametersJson = ExtractJsonObject(jsonRequest, "parameters");
                
                if (string.IsNullOrEmpty(command))
                {
                    return ResponseHelper.Error("Missing command");
                }

                // 解析参数
                var parameters = ParseParameters(parametersJson);
                
                // 使用 CommandRouter 执行命令
                var result = CommandRouter.Execute(command, parameters);
                
                // 直接传递对象，让 ResponseHelper 处理序列化
                return ResponseHelper.Success(result ?? new object());
            }
            catch (Exception ex)
            {
                return ResponseHelper.Error(ex.Message);
            }
        }

        #region JSON Parsing Helpers

        private static string ExtractJsonValue(string json, string key)
        {
            string pattern = $"\"{key}\":\\s*\"([^\"]*)\"";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            
            pattern = $"\"{key}\":\\s*([^,\\}}]+)";
            match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            
            return null;
        }

        private static string ExtractJsonObject(string json, string key)
        {
            int keyIndex = json.IndexOf($"\"{key}\":");
            if (keyIndex < 0) return "{}";
            
            int start = keyIndex + key.Length + 3;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            
            if (start >= json.Length) return "{}";
            
            if (json[start] == '{')
            {
                int depth = 1;
                int end = start + 1;
                while (end < json.Length && depth > 0)
                {
                    if (json[end] == '{') depth++;
                    else if (json[end] == '}') depth--;
                    end++;
                }
                return json.Substring(start, end - start);
            }
            
            return "{}";
        }

        private static Dictionary<string, string> ParseParameters(string parametersJson)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(parametersJson) || parametersJson == "{}")
                return dict;

            parametersJson = parametersJson.Trim('{', '}');
            var pairs = SplitJsonPairs(parametersJson);
            
            foreach (var pair in pairs)
            {
                var colonIndex = pair.IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = pair.Substring(0, colonIndex).Trim().Trim('"');
                    string value = pair.Substring(colonIndex + 1).Trim();
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                    dict[key] = value;
                }
            }
            return dict;
        }

        private static List<string> SplitJsonPairs(string json)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;
            bool inString = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                    inString = !inString;
                else if (!inString)
                {
                    if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') depth--;
                    else if (c == ',' && depth == 0)
                    {
                        result.Add(json.Substring(start, i - start));
                        start = i + 1;
                    }
                }
            }
            if (start < json.Length)
                result.Add(json.Substring(start));
            return result;
        }

        #endregion
    }
}
