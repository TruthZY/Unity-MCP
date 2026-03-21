using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using McpUnity.Core;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Editor
{
    /// <summary>
    /// MCP 配置生成器 - 自动扫描所有 MCP 命令并生成 tools.json 配置文件
    /// </summary>
    public class McpConfigGenerator : EditorWindow
    {
        private string outputPath = "Assets/Editor/MCPEditor/.MCPServer/McpServer/tools.json";
        private string lastGeneratedContent = "";
        private Vector2 scrollPosition;
        private Vector2 commandListScrollPosition;
        private bool autoGenerateOnBuild = true;
        
        // 命令选择和缓存
        private List<CommandInfo> availableCommands = new List<CommandInfo>();
        private HashSet<string> selectedCommands = new HashSet<string>();
        private string searchFilter = "";
        private bool showOnlySelected = false;
        private bool isInitialized = false;

        [MenuItem("MCP/Config Generator")]
        public static void ShowWindow()
        {
            GetWindow<McpConfigGenerator>("MCP Config Generator");
        }

        private void OnEnable()
        {
            LoadSettings();
            RefreshCommandList();
        }

        private void OnGUI()
        {
            GUILayout.Label("MCP 配置生成器", EditorStyles.largeLabel);
            EditorGUILayout.Space();

            // 输出路径
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("输出路径:", GUILayout.Width(80));
            outputPath = EditorGUILayout.TextField(outputPath);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                string selectedPath = EditorUtility.SaveFilePanel("选择输出路径", 
                    Path.GetDirectoryName(outputPath), 
                    "tools.json", "json");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    outputPath = GetRelativePath(selectedPath);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // 命令选择区域
            DrawCommandSelection();

            EditorGUILayout.Space();

            // 生成按钮
            EditorGUILayout.BeginHorizontal();
            
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button($"生成 tools.json ({selectedCommands.Count} 个命令)", GUILayout.Height(40)))
            {
                GenerateConfig();
            }
            GUI.backgroundColor = Color.white;
            
            if (GUILayout.Button("重置选择", GUILayout.Width(100), GUILayout.Height(40)))
            {
                if (EditorUtility.DisplayDialog("确认", "确定要重置所有选择吗？", "确定", "取消"))
                {
                    ResetSelection();
                }
            }
            
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // 显示上次生成的内容
            if (!string.IsNullOrEmpty(lastGeneratedContent))
            {
                GUILayout.Label("生成的配置预览:", EditorStyles.boldLabel);
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
                EditorGUILayout.TextArea(lastGeneratedContent, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        /// <summary>
        /// 绘制命令选择界面
        /// </summary>
        private void DrawCommandSelection()
        {
            GUILayout.Label("选择要导出的命令", EditorStyles.boldLabel);
            
            // 搜索和过滤
            EditorGUILayout.BeginHorizontal();
            searchFilter = EditorGUILayout.TextField("🔍 搜索", searchFilter);
            showOnlySelected = GUILayout.Toggle(showOnlySelected, "仅显示已选", GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            // 快捷操作按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选"))
            {
                foreach (var cmd in availableCommands)
                {
                    selectedCommands.Add(cmd.FullName);
                }
                SaveSelection();
            }
            if (GUILayout.Button("清空"))
            {
                selectedCommands.Clear();
                SaveSelection();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);

            // 命令列表
            commandListScrollPosition = EditorGUILayout.BeginScrollView(commandListScrollPosition, 
                GUILayout.Height(350));
            
            string currentModule = "";
            
            var filteredCommands = availableCommands.Where(c =>
            {
                // 搜索过滤
                bool matchesSearch = string.IsNullOrEmpty(searchFilter) || 
                    c.FullName.ToLower().Contains(searchFilter.ToLower()) ||
                    c.Description.ToLower().Contains(searchFilter.ToLower());
                
                // 仅显示已选过滤
                bool matchesSelected = !showOnlySelected || selectedCommands.Contains(c.FullName);
                
                return matchesSearch && matchesSelected;
            }).ToList();
            
            foreach (var cmd in filteredCommands)
            {
                // 模块分组标题
                if (cmd.ModuleName != currentModule)
                {
                    currentModule = cmd.ModuleName;
                    EditorGUILayout.Space(5);
                    GUILayout.Label($"[{currentModule.ToUpper()}]", EditorStyles.boldLabel);
                }
                
                EditorGUILayout.BeginHorizontal();
                
                // 复选框
                bool isSelected = selectedCommands.Contains(cmd.FullName);
                bool newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20));
                
                if (newSelected != isSelected)
                {
                    if (newSelected)
                        selectedCommands.Add(cmd.FullName);
                    else
                        selectedCommands.Remove(cmd.FullName);
                    SaveSelection();
                }
                
                // 命令名称和描述
                EditorGUILayout.BeginVertical();
                GUILayout.Label(cmd.CommandName, EditorStyles.boldLabel);
                GUILayout.Label(cmd.Description, EditorStyles.miniLabel);
                
                // 显示参数信息
                if (cmd.Parameters.Count > 0)
                {
                    var paramNames = string.Join(", ", cmd.Parameters.Select(p => 
                        $"{p.Name}{(p.Required ? "*" : "")}"));
                    GUILayout.Label($"  参数: {paramNames}", new GUIStyle(EditorStyles.miniLabel) 
                    { 
                        normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } 
                    });
                }
                
                EditorGUILayout.EndVertical();
                
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(3);
            }
            
            EditorGUILayout.EndScrollView();
            
            GUILayout.Label($"已选择: {selectedCommands.Count} / {availableCommands.Count}", 
                EditorStyles.miniLabel);
        }

        /// <summary>
        /// 刷新命令列表
        /// </summary>
        private void RefreshCommandList()
        {
            availableCommands.Clear();
            
            // 获取所有带有 McpModule 特性的类
            var moduleTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.GetCustomAttribute<McpModuleAttribute>() != null && 
                           typeof(IMcpModule).IsAssignableFrom(t))
                .ToList();

            foreach (var moduleType in moduleTypes)
            {
                var moduleAttr = moduleType.GetCustomAttribute<McpModuleAttribute>();
                var moduleName = moduleAttr?.ModuleName ?? moduleType.Name.ToLower().Replace("module", "");

                var methods = moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<McpCommandAttribute>() != null)
                    .ToList();

                foreach (var method in methods)
                {
                    var cmdAttr = method.GetCustomAttribute<McpCommandAttribute>();
                    var paramAttrs = method.GetCustomAttributes<McpParameterAttribute>().ToList();
                    
                    var cmdInfo = new CommandInfo
                    {
                        ModuleName = moduleName,
                        CommandName = cmdAttr?.CommandName ?? method.Name.ToLower(),
                        FullName = $"{moduleName}_{cmdAttr?.CommandName ?? method.Name.ToLower()}",
                        Description = cmdAttr?.Description ?? $"{method.Name} command",
                        Parameters = GetParameterInfos(paramAttrs)
                    };
                    
                    availableCommands.Add(cmdInfo);
                }
            }
            
            availableCommands = availableCommands.OrderBy(c => c.ModuleName)
                .ThenBy(c => c.CommandName).ToList();
            
            // 加载之前的选择
            LoadSelection();
        }

        private List<ParameterInfo> GetParameterInfos(List<McpParameterAttribute> paramAttrs)
        {
            var result = new List<ParameterInfo>();
            foreach (var attr in paramAttrs)
            {
                result.Add(new ParameterInfo
                {
                    Name = attr.Name,
                    Description = attr.Description,
                    Required = attr.Required,
                    DefaultValue = attr.DefaultValue,
                    Example = attr.Example
                });
            }
            return result;
        }

        /// <summary>
        /// 生成配置文件
        /// </summary>
        public static void GenerateConfig()
        {
            var window = GetWindow<McpConfigGenerator>();
            window.DoGenerate();
        }

        private void DoGenerate()
        {
            try
            {
                // 只生成选中的命令
                var selectedCmds = availableCommands.Where(c => 
                    selectedCommands.Contains(c.FullName)).ToList();
                
                if (selectedCmds.Count == 0)
                {
                    EditorUtility.DisplayDialog("警告", "请至少选择一个命令", "确定");
                    return;
                }

                var config = new McpToolsConfig
                {
                    generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    unityVersion = Application.unityVersion,
                    commands = selectedCmds.Select(cmd => new CommandConfig
                    {
                        module = cmd.ModuleName,
                        command = cmd.CommandName,
                        toolName = $"unity_{cmd.FullName}",
                        description = cmd.Description,
                        parameters = cmd.Parameters.Select(p => new ParameterConfig
                        {
                            name = p.Name,
                            description = p.Description,
                            required = p.Required,
                            defaultValue = p.DefaultValue ?? "",
                            example = p.Example ?? ""
                        }).ToList()
                    }).ToList()
                };

                string json = JsonUtility.ToJson(config, true);
                json = FormatJson(json);

                string directory = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, json);
                lastGeneratedContent = json;

                AssetDatabase.Refresh();

                Debug.Log($"[MCP] 配置已生成: {outputPath}");
                Debug.Log($"[MCP] 共 {selectedCmds.Count} 个命令");
                
                EditorUtility.DisplayDialog("生成成功", 
                    $"已生成 {selectedCmds.Count} 个命令的配置文件\n路径: {outputPath}", "确定");

                SaveSettings();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] 配置生成失败: {ex}");
                EditorUtility.DisplayDialog("生成失败", ex.Message, "确定");
            }
        }

        private void ResetSelection()
        {
            selectedCommands.Clear();
            SaveSelection();
        }

        private void SaveSelection()
        {
            string selectionString = string.Join(",", selectedCommands);
            EditorPrefs.SetString("McpConfigGenerator_Selection", selectionString);
        }

        private void LoadSelection()
        {
            string selectionString = EditorPrefs.GetString("McpConfigGenerator_Selection", "");
            if (!string.IsNullOrEmpty(selectionString))
            {
                selectedCommands = new HashSet<string>(selectionString.Split(','));
                
                // 清理不存在的命令
                selectedCommands.RemoveWhere(cmd => 
                    !availableCommands.Any(c => c.FullName == cmd));
            }
            else
            {
                // 默认全选
                foreach (var cmd in availableCommands)
                {
                    selectedCommands.Add(cmd.FullName);
                }
            }
        }

        private void SaveSettings()
        {
            EditorPrefs.SetString("McpConfigGenerator_OutputPath", outputPath);
            EditorPrefs.SetBool("McpConfigGenerator_AutoGenerate", autoGenerateOnBuild);
        }

        private void LoadSettings()
        {
            outputPath = EditorPrefs.GetString("McpConfigGenerator_OutputPath", 
                "Assets/Editor/MCPEditor/.MCPServer/McpServer/tools.json");
            autoGenerateOnBuild = EditorPrefs.GetBool("McpConfigGenerator_AutoGenerate", true);
        }

        private string GetRelativePath(string absolutePath)
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            if (absolutePath.StartsWith(projectPath))
            {
                return absolutePath.Substring(projectPath.Length + 1).Replace('\\', '/');
            }
            return absolutePath;
        }

        private string FormatJson(string json)
        {
            var sb = new System.Text.StringBuilder();
            int indent = 0;
            bool inString = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                {
                    inString = !inString;
                }

                if (!inString)
                {
                    if (c == '{' || c == '[')
                    {
                        sb.Append(c);
                        sb.Append('\n');
                        indent++;
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (c == '}' || c == ']')
                    {
                        sb.Append('\n');
                        indent--;
                        sb.Append(new string(' ', indent * 2));
                        sb.Append(c);
                    }
                    else if (c == ',')
                    {
                        sb.Append(c);
                        sb.Append('\n');
                        sb.Append(new string(' ', indent * 2));
                    }
                    else if (c == ':')
                    {
                        sb.Append(c);
                        sb.Append(' ');
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        #region 数据类

        private class CommandInfo
        {
            public string ModuleName { get; set; }
            public string CommandName { get; set; }
            public string FullName { get; set; }
            public string Description { get; set; }
            public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
        }

        private class ParameterInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public bool Required { get; set; }
            public string DefaultValue { get; set; }
            public string Example { get; set; }
        }

        [Serializable]
        public class McpToolsConfig
        {
            public string generatedAt;
            public string unityVersion;
            public List<CommandConfig> commands;
        }

        [Serializable]
        public class CommandConfig
        {
            public string module;
            public string command;
            public string toolName;
            public string description;
            public List<ParameterConfig> parameters;
        }

        [Serializable]
        public class ParameterConfig
        {
            public string name;
            public string description;
            public bool required;
            public string defaultValue;
            public string example;
        }

        #endregion
    }
}
