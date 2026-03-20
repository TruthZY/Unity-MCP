using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using McpUnity.Core;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Editor
{
    /// <summary>
    /// MCP 指令测试工具 - 通用命令测试界面
    /// </summary>
    public class McpCommandTester : EditorWindow
    {
        // 指令列表
        private List<CommandInfo> availableCommands = new List<CommandInfo>();
        private int selectedCommandIndex = 0;
        private string searchFilter = "";
        private Vector2 commandListScrollPosition;
        
        // 参数输入
        private Dictionary<string, string> parameterValues = new Dictionary<string, string>();
        private Vector2 paramScrollPosition;
        
        // 测试结果
        private string testResults = "";
        private Vector2 resultScrollPosition;
        private bool isTesting = false;
        
        // 模块缓存
        private Dictionary<string, IMcpModule> moduleCache = new Dictionary<string, IMcpModule>();
        
        [MenuItem("MCP/Command Tester")]
        public static void ShowWindow()
        {
            GetWindow<McpCommandTester>("MCP Command Tester", typeof(SceneView));
        }
        
        private void OnEnable()
        {
            RefreshCommandList();
        }
        
        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            
            // 左侧：指令列表
            DrawCommandList();
            
            // 右侧：参数输入和结果
            DrawMainPanel();
            
            EditorGUILayout.EndHorizontal();
        }
        
        /// <summary>
        /// 绘制左侧指令列表
        /// </summary>
        private void DrawCommandList()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(300));
            
            GUILayout.Label("可用指令", EditorStyles.boldLabel);
            
            // 搜索框
            EditorGUI.BeginChangeCheck();
            searchFilter = EditorGUILayout.TextField("🔍", searchFilter);
            if (EditorGUI.EndChangeCheck())
            {
                selectedCommandIndex = 0;
            }
            
            EditorGUILayout.Space(5);
            
            // 刷新按钮
            if (GUILayout.Button("刷新指令列表", GUILayout.Height(25)))
            {
                RefreshCommandList();
            }
            
            EditorGUILayout.Space(5);
            
            // 指令列表
            commandListScrollPosition = EditorGUILayout.BeginScrollView(commandListScrollPosition);
            
            var filteredCommands = availableCommands
                .Where(c => string.IsNullOrEmpty(searchFilter) || 
                           c.FullName.ToLower().Contains(searchFilter.ToLower()) ||
                           c.Description.ToLower().Contains(searchFilter.ToLower()))
                .ToList();
            
            for (int i = 0; i < filteredCommands.Count; i++)
            {
                var cmd = filteredCommands[i];
                bool isSelected = i == selectedCommandIndex;
                
                var style = new GUIStyle(EditorStyles.toolbarButton);
                if (isSelected)
                {
                    style.normal.background = EditorGUIUtility.Load("builtin skins/darkskin/images/pre background act.png") as Texture2D;
                    style.normal.textColor = Color.white;
                }
                
                if (GUILayout.Button(cmd.DisplayName, style, GUILayout.Height(30)))
                {
                    selectedCommandIndex = i;
                    OnCommandSelected(cmd);
                }
                
                // 显示描述提示
                var rect = GUILayoutUtility.GetLastRect();
                if (rect.Contains(Event.current.mousePosition))
                {
                    EditorGUI.LabelField(rect, new GUIContent("", cmd.Description));
                }
            }
            
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.EndVertical();
        }
        
        /// <summary>
        /// 绘制右侧主面板
        /// </summary>
        private void DrawMainPanel()
        {
            EditorGUILayout.BeginVertical();
            
            if (availableCommands.Count == 0 || selectedCommandIndex >= availableCommands.Count)
            {
                GUILayout.Label("请选择一个指令", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }
            
            var selectedCmd = availableCommands[selectedCommandIndex];
            
            // 指令信息头部
            GUILayout.Label(selectedCmd.DisplayName, EditorStyles.largeLabel);
            GUILayout.Label($"模块: {selectedCmd.ModuleName}", EditorStyles.miniLabel);
            EditorGUILayout.Space();
            
            // 参数输入区域
            DrawParameterInputs(selectedCmd);
            
            EditorGUILayout.Space(10);
            
            // 执行按钮
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("执行指令", GUILayout.Height(40)))
            {
                ExecuteCommand(selectedCmd);
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.Space(10);
            
            // 结果显示区域
            DrawResultPanel();
            
            EditorGUILayout.EndVertical();
        }
        
        /// <summary>
        /// 绘制参数输入
        /// </summary>
        private void DrawParameterInputs(CommandInfo cmd)
        {
            GUILayout.Label("参数", EditorStyles.boldLabel);
            
            paramScrollPosition = EditorGUILayout.BeginScrollView(paramScrollPosition, GUILayout.MaxHeight(300));
            
            if (cmd.Parameters.Count == 0)
            {
                GUILayout.Label("(此指令无需参数)", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var param in cmd.Parameters)
                {
                    EditorGUILayout.BeginHorizontal();
                    
                    // 参数名
                    GUILayout.Label(param.Name, GUILayout.Width(120));
                    
                    // 参数输入框
                    if (!parameterValues.ContainsKey(param.Name))
                    {
                        parameterValues[param.Name] = param.DefaultValue ?? "";
                    }
                    
                    if (param.IsBoolean)
                    {
                        // 布尔值用 Toggle
                        bool boolValue = parameterValues[param.Name].ToLower() == "true";
                        boolValue = EditorGUILayout.Toggle(boolValue);
                        parameterValues[param.Name] = boolValue.ToString().ToLower();
                    }
                    else if (param.IsMultiline)
                    {
                        // 多行文本
                        parameterValues[param.Name] = EditorGUILayout.TextArea(
                            parameterValues[param.Name], 
                            GUILayout.Height(60));
                    }
                    else
                    {
                        // 普通文本
                        parameterValues[param.Name] = EditorGUILayout.TextField(parameterValues[param.Name]);
                    }
                    
                    // 必需标记
                    if (param.IsRequired)
                    {
                        GUILayout.Label("*", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.red } }, GUILayout.Width(15));
                    }
                    
                    EditorGUILayout.EndHorizontal();
                    
                    // 参数描述
                    if (!string.IsNullOrEmpty(param.Description))
                    {
                        GUILayout.Label($"  {param.Description}", EditorStyles.miniLabel);
                    }
                    
                    EditorGUILayout.Space(5);
                }
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        /// <summary>
        /// 绘制结果面板
        /// </summary>
        private void DrawResultPanel()
        {
            GUILayout.Label("测试结果", EditorStyles.boldLabel);
            
            var resultStyle = new GUIStyle(EditorStyles.textArea);
            resultStyle.wordWrap = true;
            resultStyle.richText = true;
            
            resultScrollPosition = EditorGUILayout.BeginScrollView(resultScrollPosition, GUILayout.ExpandHeight(true));
            
            if (isTesting)
            {
                GUILayout.Label("<color=yellow>执行中...</color>", resultStyle);
            }
            else if (!string.IsNullOrEmpty(testResults))
            {
                GUILayout.Label(testResults, resultStyle);
            }
            else
            {
                GUILayout.Label("(点击\"执行指令\"查看结果)", EditorStyles.centeredGreyMiniLabel);
            }
            
            EditorGUILayout.EndScrollView();
            
            // 复制结果按钮
            if (!string.IsNullOrEmpty(testResults) && GUILayout.Button("复制结果到剪贴板"))
            {
                EditorGUIUtility.systemCopyBuffer = testResults;
                ShowNotification(new GUIContent("已复制到剪贴板!"));
            }
        }
        
        /// <summary>
        /// 刷新指令列表
        /// </summary>
        private void RefreshCommandList()
        {
            availableCommands.Clear();
            moduleCache.Clear();
            
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
                
                // 获取模块实例
                IMcpModule module = null;
                try
                {
                    module = Activator.CreateInstance(moduleType) as IMcpModule;
                    moduleCache[moduleName] = module;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"无法创建模块实例 {moduleType.Name}: {ex.Message}");
                    continue;
                }
                
                // 获取所有带有 McpCommand 特性的方法
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
                        MethodInfo = method,
                        ModuleInstance = module,
                        Description = !string.IsNullOrEmpty(cmdAttr?.Description) ? cmdAttr.Description : GetCommandDescription(method),
                        Parameters = GetCommandParameters(method, paramAttrs)
                    };
                    
                    availableCommands.Add(cmdInfo);
                }
            }
            
            // 排序
            availableCommands = availableCommands.OrderBy(c => c.ModuleName).ThenBy(c => c.CommandName).ToList();
            
            // 默认选择第一个
            if (availableCommands.Count > 0)
            {
                OnCommandSelected(availableCommands[0]);
            }
            
            Debug.Log($"刷新了 {availableCommands.Count} 个指令");
        }
        
        /// <summary>
        /// 获取命令描述
        /// </summary>
        private string GetCommandDescription(MethodInfo method)
        {
            // 从 XML 文档或特性获取描述
            var summary = method.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.Name == "SummaryAttribute");
            
            if (summary != null && summary.ConstructorArguments.Count > 0)
            {
                return summary.ConstructorArguments[0].Value?.ToString() ?? "";
            }
            
            return $"{method.Name} 命令";
        }
        
        /// <summary>
        /// 获取命令参数信息
        /// </summary>
        private List<ParameterInfo> GetCommandParameters(MethodInfo method, List<McpParameterAttribute> paramAttrs = null)
        {
            var parameters = new List<ParameterInfo>();
            
            // 如果是 Dictionary<string, string> 参数，使用特性信息
            var dictParam = method.GetParameters().FirstOrDefault(p => p.ParameterType == typeof(Dictionary<string, string>));
            if (dictParam != null && paramAttrs != null && paramAttrs.Count > 0)
            {
                foreach (var attr in paramAttrs)
                {
                    var paramInfo = new ParameterInfo
                    {
                        Name = attr.Name,
                        Type = typeof(string),
                        IsRequired = attr.Required,
                        DefaultValue = attr.DefaultValue ?? "",
                        Description = $"{attr.Description}{(string.IsNullOrEmpty(attr.Example) ? "" : $" (示例: {attr.Example})")}",
                        IsBoolean = attr.DefaultValue?.ToLower() == "true" || attr.DefaultValue?.ToLower() == "false"
                    };
                    parameters.Add(paramInfo);
                }
                return parameters;
            }
            
            // 否则使用反射获取参数信息
            var paramInfos = method.GetParameters();
            foreach (var param in paramInfos)
            {
                var paramInfo = new ParameterInfo
                {
                    Name = param.Name,
                    Type = param.ParameterType,
                    IsRequired = !param.IsOptional,
                    DefaultValue = param.DefaultValue?.ToString() ?? "",
                    Description = GetParameterDescription(param)
                };
                
                // 检测特殊类型
                if (param.ParameterType == typeof(bool) || 
                    (param.ParameterType == typeof(string) && param.Name.ToLower().Contains("bool")))
                {
                    paramInfo.IsBoolean = true;
                }
                
                if (param.Name.ToLower().Contains("json") || 
                    param.Name.ToLower().Contains("content") ||
                    param.Name.ToLower().Contains("data"))
                {
                    paramInfo.IsMultiline = true;
                }
                
                parameters.Add(paramInfo);
            }
            
            return parameters;
        }
        
        /// <summary>
        /// 获取参数描述
        /// </summary>
        private string GetParameterDescription(System.Reflection.ParameterInfo param)
        {
            // 根据参数名推断描述
            var name = param.Name.ToLower();
            
            if (name.Contains("path")) return "路径 (如: Assets/UI/MyPrefab.prefab)";
            if (name.Contains("name")) return "名称";
            if (name.Contains("key")) return "参数键名";
            if (name.Contains("value")) return "参数值";
            if (name.Contains("type")) return "类型";
            if (name.Contains("filter")) return "过滤条件";
            if (name.Contains("parent")) return "父对象路径";
            if (name.Contains("prefab")) return "Prefab 路径";
            
            return $"{param.ParameterType.Name} 类型参数";
        }
        
        /// <summary>
        /// 选择指令时调用
        /// </summary>
        private void OnCommandSelected(CommandInfo cmd)
        {
            parameterValues.Clear();
            
            // 设置默认值
            foreach (var param in cmd.Parameters)
            {
                parameterValues[param.Name] = param.DefaultValue ?? "";
            }
            
            testResults = "";
        }
        
        /// <summary>
        /// 执行指令
        /// </summary>
        private void ExecuteCommand(CommandInfo cmd)
        {
            isTesting = true;
            testResults = "";
            
            try
            {
                // 构建参数字典
                var parameters = new Dictionary<string, string>();
                
                // 查找 Dictionary<string, string> 参数
                var dictParam = cmd.MethodInfo.GetParameters()
                    .FirstOrDefault(p => p.ParameterType == typeof(Dictionary<string, string>));
                
                if (dictParam != null)
                {
                    foreach (var kvp in parameterValues)
                    {
                        if (!string.IsNullOrEmpty(kvp.Value))
                        {
                            parameters[kvp.Key] = kvp.Value;
                        }
                    }
                }
                
                // 调用方法
                object result = null;
                
                if (dictParam != null)
                {
                    result = cmd.MethodInfo.Invoke(cmd.ModuleInstance, new object[] { parameters });
                }
                else
                {
                    // 构建参数数组
                    var args = cmd.Parameters.Select(p => 
                    {
                        if (parameterValues.TryGetValue(p.Name, out var val))
                        {
                            return Convert.ChangeType(val, p.Type);
                        }
                        return p.Type.IsValueType ? Activator.CreateInstance(p.Type) : null;
                    }).ToArray();
                    
                    result = cmd.MethodInfo.Invoke(cmd.ModuleInstance, args);
                }
                
                // 格式化结果
                if (result != null)
                {
                    testResults = JsonUtility.ToJson(result, true);
                }
                else
                {
                    testResults = "指令执行成功 (无返回值)";
                }
                
                Debug.Log($"指令 {cmd.FullName} 执行成功");
            }
            catch (Exception ex)
            {
                testResults = $"<color=red>错误: {ex.Message}</color>\n\n{ex.StackTrace}";
                Debug.LogError($"指令 {cmd.FullName} 执行失败: {ex}");
            }
            finally
            {
                isTesting = false;
                Repaint();
            }
        }
        
        #region 数据类
        
        /// <summary>
        /// 指令信息
        /// </summary>
        private class CommandInfo
        {
            public string ModuleName { get; set; }
            public string CommandName { get; set; }
            public string FullName => $"{ModuleName}_{CommandName}";
            public string DisplayName => $"[{ModuleName}] {CommandName}";
            public string Description { get; set; }
            public MethodInfo MethodInfo { get; set; }
            public IMcpModule ModuleInstance { get; set; }
            public List<ParameterInfo> Parameters { get; set; } = new List<ParameterInfo>();
        }
        
        /// <summary>
        /// 参数信息
        /// </summary>
        private class ParameterInfo
        {
            public string Name { get; set; }
            public Type Type { get; set; }
            public bool IsRequired { get; set; }
            public string DefaultValue { get; set; }
            public string Description { get; set; }
            public bool IsBoolean { get; set; }
            public bool IsMultiline { get; set; }
        }
        
        #endregion
    }
}
