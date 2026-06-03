using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using McpUnity.Core;
using Microsoft.CSharp;
using UnityEngine;

namespace McpUnity.Modules
{
    /// <summary>
    /// C# 代码执行模块 - 在 Unity Editor 中动态编译并执行 C# 代码
    /// 使用 CodeDom 内存编译，不生成任何脚本文件
    /// </summary>
    [McpModule("code")]
    public class CodeModule : IMcpModule
    {
        public string ModuleName => "code";

        private const int MaxCodeLength = 50000;
        private const string WrapperClassName = "MCPDynamicCode";
        private const string WrapperMethodName = "Execute";
        private const int WrapperLineOffset = 8; // using x6 + class + method = 8 lines before user code

        private static string[] _cachedAssemblyPaths;

        // 危险模式拦截（默认开启）
        private static readonly HashSet<string> _blockedPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.IO.File.Delete",
            "System.IO.Directory.Delete",
            "FileUtil.DeleteFileOrDirectory",
            "AssetDatabase.DeleteAsset",
            "AssetDatabase.MoveAssetToTrash",
            "EditorApplication.Exit",
            "Process.Start",
            "Process.Kill",
            "while(true)",
            "while (true)",
            "for(;;)",
            "for (;;)",
        };

        /// <summary>
        /// 动态编译并执行 C# 代码
        /// 用户只需写方法体级别的代码（如 return GameObject.Find("Cube");）
        /// </summary>
        [McpCommand("execute", "在 Unity Editor 中动态编译并执行 C# 代码（内存编译，不生成文件）")]
        [McpParameter("code", "要执行的 C# 代码（方法体级别，可用 using System/UnityEngine/UnityEditor）", Required = true, Example = "return GameObject.Find(\"Cube\")?.name;")]
        [McpParameter("safety_checks", "是否启用安全检查（拦截危险模式），默认 true", Required = false, Example = "true")]
        public object Execute(Dictionary<string, string> parameters)
        {
            string code = GetParam(parameters, "code");
            if (string.IsNullOrWhiteSpace(code))
                return new CodeResult { success = false, error = "Parameter 'code' is required" };

            if (code.Length > MaxCodeLength)
                return new CodeResult { success = false, error = $"Code exceeds maximum length of {MaxCodeLength} characters" };

            bool safetyChecks = GetParam(parameters, "safety_checks", "true").ToLowerInvariant() != "false";

            // 安全检查
            if (safetyChecks)
            {
                string violation = CheckBlockedPatterns(code);
                if (violation != null)
                    return new CodeResult { success = false, error = $"Blocked pattern detected: {violation}. Set safety_checks=false to bypass." };
            }

            try
            {
                var startTime = DateTime.UtcNow;
                var result = CompileAndExecute(code);
                var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                if (result is CodeResult cr)
                {
                    cr.elapsedMs = Math.Round(elapsedMs, 1);
                    return cr;
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] ExecuteCode failed: {ex}");
                return new CodeResult
                {
                    success = false,
                    error = $"Execution failed: {ex.Message}"
                };
            }
        }

        // ──────────────────── 编译执行 ────────────────────

        private object CompileAndExecute(string code)
        {
            string wrappedSource = WrapUserCode(code);
            string[] assemblyPaths = GetAssemblyPaths();

            // CodeDom 编译
            var filtered = FilterAssemblyPaths(assemblyPaths);
            using (var provider = new CSharpCodeProvider())
            {
                var compilerParams = new CompilerParameters
                {
                    GenerateInMemory = true,
                    GenerateExecutable = false,
                    TreatWarningsAsErrors = false,
                };

                foreach (var path in filtered)
                    compilerParams.ReferencedAssemblies.Add(path);

                var results = provider.CompileAssemblyFromSource(compilerParams, wrappedSource);

                if (results.Errors.HasErrors)
                {
                    var errors = new List<string>();
                    foreach (CompilerError error in results.Errors)
                    {
                        if (!error.IsWarning)
                        {
                            int userLine = Math.Max(1, error.Line - WrapperLineOffset);
                            errors.Add($"Line {userLine}: {error.ErrorText}");
                        }
                    }
                    return new CodeResult
                    {
                        success = false,
                        error = "Compilation failed: " + string.Join("; ", errors)
                    };
                }

                // 反射调用
                var assembly = results.CompiledAssembly;
                return InvokeCompiled(assembly);
            }
        }

        private object InvokeCompiled(Assembly assembly)
        {
            var type = assembly.GetType(WrapperClassName);
            if (type == null)
                return new CodeResult { success = false, error = "Internal error: compiled type not found" };

            var method = type.GetMethod(WrapperMethodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                return new CodeResult { success = false, error = "Internal error: Execute method not found" };

            object result = null;
            Exception executionError = null;
            var capturedLogs = new List<string>();

            // 挂载日志监听，捕获执行期间的 Debug.Log/Warning/Error 输出
            Application.LogCallback logHandler = (message, stackTrace, logType) =>
            {
                string prefix = logType == LogType.Log ? "" : $"[{logType}] ";
                capturedLogs.Add(prefix + message);
            };
            Application.logMessageReceived += logHandler;

            try
            {
                result = method.Invoke(null, null);
            }
            catch (TargetInvocationException tie)
            {
                executionError = tie.InnerException ?? tie;
            }
            catch (Exception e)
            {
                executionError = e;
            }
            finally
            {
                Application.logMessageReceived -= logHandler;
            }

            var logArray = capturedLogs.Count > 0 ? capturedLogs.ToArray() : null;

            if (executionError != null)
            {
                return new CodeResult
                {
                    success = false,
                    error = $"Runtime error: {executionError.Message}",
                    errorType = executionError.GetType().Name,
                    stackTrace = executionError.StackTrace,
                    logs = logArray
                };
            }

            return new CodeResult
            {
                success = true,
                result = SerializeResult(result),
                logs = logArray
            };
        }

        // ──────────────────── 辅助方法 ────────────────────

        /// <summary>
        /// 将用户代码包装进固定的类壳中
        /// 自动检测是否包含 return 语句：有则返回 object，无则返回 void
        /// 预置 using: System, System.Collections.Generic, System.Linq, System.Reflection, UnityEngine, UnityEditor
        /// </summary>
        private string WrapUserCode(string code)
        {
            bool hasReturn = HasReturnStatement(code);

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Reflection;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine($"public static class {WrapperClassName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static {(hasReturn ? "object" : "void")} {WrapperMethodName}()");
            sb.AppendLine("    {");
            sb.AppendLine(code);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// 简易检测用户代码是否包含 return 语句（跳过注释和字符串字面量）
        /// </summary>
        private bool HasReturnStatement(string code)
        {
            var lines = code.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimStart();
                if (line.StartsWith("//")) continue;
                // 去掉字符串内容，避免误匹配 "return" 字面量
                var stripped = System.Text.RegularExpressions.Regex.Replace(line, "\"(?:[^\"\\\\]|\\\\.)*\"", "");
                // 去掉行内注释
                var commentIdx = stripped.IndexOf("//");
                if (commentIdx >= 0) stripped = stripped.Substring(0, commentIdx);
                // 匹配 return 关键字（前后必须是非单词字符或行首/行尾）
                if (System.Text.RegularExpressions.Regex.IsMatch(stripped, @"(^|[^a-zA-Z0-9_])return([^a-zA-Z0-9_]|$)"))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 获取当前 AppDomain 所有已加载程序集的路径（缓存）
        /// </summary>
        private string[] GetAssemblyPaths()
        {
            if (_cachedAssemblyPaths != null)
                return _cachedAssemblyPaths;

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.IsDynamic) continue;
                    var location = assembly.Location;
                    if (string.IsNullOrEmpty(location)) continue;
                    if (!File.Exists(location)) continue;
                    paths.Add(location);
                }
                catch (NotSupportedException) { }
            }

            _cachedAssemblyPaths = paths.ToArray();
            return _cachedAssemblyPaths;
        }

        /// <summary>
        /// CodeDom 无法处理 netstandard.dll 与 mscorlib/System.Runtime 的类型重复问题，
        /// 当检测到 netstandard.dll 时过滤掉冲突的程序集
        /// </summary>
        private static readonly HashSet<string> _duplicateAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mscorlib",
            "System.Runtime",
            "System.Private.CoreLib",
            "System.Collections",
        };

        private string[] FilterAssemblyPaths(string[] allPaths)
        {
            bool hasNetstandard = allPaths.Any(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), "netstandard", StringComparison.OrdinalIgnoreCase));

            if (!hasNetstandard)
                return allPaths;

            return allPaths.Where(p =>
                !_duplicateAssemblies.Contains(Path.GetFileNameWithoutExtension(p))).ToArray();
        }

        private string CheckBlockedPatterns(string code)
        {
            foreach (var pattern in _blockedPatterns)
            {
                if (code.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return $"'{pattern}'";
            }
            return null;
        }

        /// <summary>
        /// 序列化执行结果：基本类型直接返回，复杂对象转 JSON 字符串
        /// </summary>
        private string SerializeResult(object result)
        {
            if (result == null) return null;

            var type = result.GetType();
            if (type.IsPrimitive || result is string || result is decimal)
                return result.ToString();

            // 尝试用 JsonUtility 序列化（不支持 Transform/GameObject 等 Unity 对象）
            try
            {
                return JsonUtility.ToJson(result);
            }
            catch (ArgumentException)
            {
                return result.ToString();
            }
        }

        private string GetParam(Dictionary<string, string> parameters, string key, string defaultValue = "")
        {
            return parameters.TryGetValue(key, out var value) ? value : defaultValue;
        }

        #region Result Classes

        [Serializable]
        public class CodeResult
        {
            public bool success;
            public string error;
            public string result;
            public string[] logs;
            public string errorType;
            public string stackTrace;
            public double elapsedMs;
        }

        #endregion
    }
}
