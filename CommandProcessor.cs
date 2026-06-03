using System;
using System.Collections.Generic;
using System.Linq;
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

                // 参数校验：检查是否有未知参数并给出建议
                var expectedParams = CommandRouter.GetCommandParameterNames(command);
                if (expectedParams != null && parameters.Count > 0)
                {
                    foreach (var key in parameters.Keys)
                    {
                        if (!expectedParams.Contains(key))
                        {
                            string suggestion = FindClosestParam(key, expectedParams);
                            string availableStr = string.Join(", ", expectedParams);
                            string errorMsg = suggestion != null
                                ? $"Unknown parameter '{key}'. Did you mean '{suggestion}'? Available parameters: {availableStr}"
                                : $"Unknown parameter '{key}'. Available parameters: {availableStr}";
                            return ResponseHelper.Error(errorMsg);
                        }
                    }
                }

                // 使用 CommandRouter 执行命令
                var result = CommandRouter.Execute(command, parameters);
                
                // 检查命令结果是否包含 success=false，正确传递状态
                if (result != null && IsFailureResult(result))
                {
                    return ResponseHelper.Error(JsonUtility.ToJson(result));
                }
                
                return ResponseHelper.Success(result ?? new object());
            }
            catch (Exception ex)
            {
                return ResponseHelper.Error(ex.Message);
            }
        }

        /// <summary>
        /// 查找最接近的参数名（简单编辑距离）
        /// </summary>
        private static string FindClosestParam(string input, List<string> candidates)
        {
            string best = null;
            int bestDist = int.MaxValue;

            foreach (var candidate in candidates)
            {
                int dist = LevenshteinDistance(input.ToLower(), candidate.ToLower());
                if (dist < bestDist && dist <= 3)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }

            return best;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            int[,] dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            }

            return dp[a.Length, b.Length];
        }

        #region JSON Parsing Helpers

        /// <summary>
        /// 通过反射检测命令结果对象是否包含 success=false
        /// </summary>
        private static bool IsFailureResult(object result)
        {
            var type = result.GetType();
            var prop = type.GetField("success");
            if (prop != null && prop.FieldType == typeof(bool))
            {
                return !(bool)prop.GetValue(result);
            }
            return false;
        }

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
