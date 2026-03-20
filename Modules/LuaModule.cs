using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using McpUnity.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace McpUnity.Modules
{
    /// <summary>
    /// LuaBehaviour操作模块 - 用于读取和修改Lua序列化参数
    /// 使用SerializedObject访问LuaBehaviour，避免反射的复杂性
    /// </summary>
    [McpModule("lua")]
    public class LuaModule : IMcpModule
    {
        public string ModuleName => "lua";

        // LuaBehaviour类型缓存
        private Type _luaBehaviourType;
        private Type LuaBehaviourType
        {
            get
            {
                if (_luaBehaviourType == null)
                {
                    _luaBehaviourType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.Name == "LuaBehaviour");
                }
                return _luaBehaviourType;
            }
        }

        /// <summary>
        /// 直接从prefab资源中获取LuaBehaviour参数（无需实例化到场景）
        /// 使用AssetDatabase.LoadAssetAtPath加载prefab，然后通过SerializedObject读取序列化数据
        /// </summary>
        [McpCommand("get_prefab_lua_params", "从prefab资源中获取LuaBehaviour参数，无需实例化到场景")]
        [McpParameter("prefabPath", "Prefab文件路径，如 Assets/UI/MyPrefab.prefab", Required = true, Example = "Assets/Game/ResourcesAB/UI2/G/Act99/Prefab/Act99_AFK_MyTips.prefab")]
        [McpParameter("gameObjectName", "可选：指定特定GameObject名称过滤", Required = false, Example = "Act99_AFK_MyTips")]
        public object GetPrefabLuaParams(Dictionary<string, string> parameters)
        {
            string prefabPath = GetParam(parameters, "prefabPath");
            string gameObjectName = GetParam(parameters, "gameObjectName");

            if (string.IsNullOrEmpty(prefabPath))
            {
                return new PrefabLuaParamsResult
                {
                    success = false,
                    error = "Parameter 'prefabPath' is required"
                };
            }

            // 确保路径以Assets开头
            if (!prefabPath.StartsWith("Assets/") && !prefabPath.StartsWith("Assets\\"))
            {
                prefabPath = "Assets/" + prefabPath;
            }

            // 加载prefab资源
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return new PrefabLuaParamsResult
                {
                    success = false,
                    error = $"Prefab not found at path: {prefabPath}"
                };
            }

            try
            {
                var results = ParsePrefabLuaBehaviours(prefab, gameObjectName);

                return new PrefabLuaParamsResult
                {
                    success = true,
                    prefabPath = prefabPath,
                    luaBehaviours = results.ToArray()
                };
            }
            catch (Exception ex)
            {
                return new PrefabLuaParamsResult
                {
                    success = false,
                    error = $"Error parsing prefab: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 解析prefab中的LuaBehaviour数据
        /// 遍历prefab中的所有GameObject，查找带有LuaBehaviour组件的对象
        /// </summary>
        private List<PrefabLuaBehaviourInfo> ParsePrefabLuaBehaviours(GameObject prefab, string filterGameObjectName)
        {
            var results = new List<PrefabLuaBehaviourInfo>();

            if (LuaBehaviourType == null)
            {
                Debug.LogError("[MCP] LuaBehaviour type not found");
                return results;
            }

            // 递归获取所有子对象（包括隐藏的）
            var allTransforms = prefab.GetComponentsInChildren<Transform>(true);

            foreach (var transform in allTransforms)
            {
                string goName = transform.name;

                // 过滤GameObject名称
                if (!string.IsNullOrEmpty(filterGameObjectName) && goName != filterGameObjectName)
                {
                    continue;
                }

                // 获取LuaBehaviour组件
                var luaBehaviour = transform.GetComponent(LuaBehaviourType);
                if (luaBehaviour == null)
                {
                    continue;
                }

                // 使用SerializedObject读取序列化数据
                var info = ExtractLuaBehaviourInfo(luaBehaviour, goName);
                if (info != null)
                {
                    results.Add(info);
                }
            }

            return results;
        }

        /// <summary>
        /// 从LuaBehaviour组件提取信息
        /// 使用Unity的SerializedObject API安全地读取序列化字段
        /// </summary>
        private PrefabLuaBehaviourInfo ExtractLuaBehaviourInfo(Component luaBehaviour, string gameObjectName)
        {
            try
            {
                var serializedObject = new SerializedObject(luaBehaviour);

                // 读取LuaScriptPath
                var luaScriptPathProp = serializedObject.FindProperty("LuaScriptPath");
                string luaScriptPath = luaScriptPathProp?.stringValue;

                if (string.IsNullOrEmpty(luaScriptPath))
                {
                    return null;
                }

                var info = new PrefabLuaBehaviourInfo
                {
                    gameObjectName = gameObjectName,
                    luaScriptPath = luaScriptPath,
                    objectParams = new List<LuaObjectParam>(),
                    valueParams = new List<LuaValueParam>()
                };

                // 读取SerializedObjValues数组
                var objValuesProp = serializedObject.FindProperty("SerializedObjValues");
                if (objValuesProp != null)
                {
                    for (int i = 0; i < objValuesProp.arraySize; i++)
                    {
                        var element = objValuesProp.GetArrayElementAtIndex(i);
                        var keyProp = element.FindPropertyRelative("key");
                        var valueProp = element.FindPropertyRelative("value");

                        if (keyProp != null)
                        {
                            string key = keyProp.stringValue;
                            UnityEngine.Object value = valueProp?.objectReferenceValue;

                            info.objectParams.Add(new LuaObjectParam
                            {
                                key = key,
                                type = value != null ? value.GetType().Name : "null",
                                value = value != null ? value.name : "null"
                            });
                        }
                    }
                }

                // 读取SerializedValues数组
                var valuesProp = serializedObject.FindProperty("SerializedValues");
                if (valuesProp != null)
                {
                    for (int i = 0; i < valuesProp.arraySize; i++)
                    {
                        var element = valuesProp.GetArrayElementAtIndex(i);
                        var keyProp = element.FindPropertyRelative("key");
                        var jsonStrProp = element.FindPropertyRelative("jsonStr");

                        if (keyProp != null)
                        {
                            info.valueParams.Add(new LuaValueParam
                            {
                                key = keyProp.stringValue,
                                jsonValue = jsonStrProp?.stringValue ?? ""
                            });
                        }
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Error extracting LuaBehaviour info: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 设置 prefab 中 LuaBehaviour 的参数值
        /// </summary>
        [McpCommand("set_prefab_lua_param", "修改 prefab 中 LuaBehaviour 的参数值")]
        [McpParameter("prefabPath", "Prefab 文件路径", Required = true, Example = "Assets/Game/ResourcesAB/UI2/G/Act99/Prefab/Act99_AFK_MyTips.prefab")]
        [McpParameter("gameObjectName", "GameObject 名称，为空则使用 prefab 根物体", Required = false, Example = "Act99_AFK_MyTips")]
        [McpParameter("paramType", "参数类型: object 或 value", Required = true, Example = "object")]
        [McpParameter("key", "参数键名", Required = true, Example = "CloseBtn")]
        [McpParameter("newValue", "新值 (object类型填对象名称/value类型填JSON)", Required = true, Example = "NewButton")]
        public object SetPrefabLuaParam(Dictionary<string, string> parameters)
        {
            string prefabPath = GetParam(parameters, "prefabPath");
            string gameObjectName = GetParam(parameters, "gameObjectName");
            string paramType = GetParam(parameters, "paramType");
            string key = GetParam(parameters, "key");
            string newValue = GetParam(parameters, "newValue");

            // 参数验证
            if (string.IsNullOrEmpty(prefabPath))
                return CreateErrorResult("Parameter 'prefabPath' is required");
            if (string.IsNullOrEmpty(key))
                return CreateErrorResult("Parameter 'key' is required");
            if (string.IsNullOrEmpty(newValue))
                return CreateErrorResult("Parameter 'newValue' is required");
            if (paramType != "object" && paramType != "value")
                return CreateErrorResult("Parameter 'paramType' must be 'object' or 'value'");

            // 确保路径以 Assets 开头
            if (!prefabPath.StartsWith("Assets/") && !prefabPath.StartsWith("Assets\\"))
                prefabPath = "Assets/" + prefabPath;

            // 加载 prefab
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return CreateErrorResult($"Prefab not found at path: {prefabPath}");

            if (LuaBehaviourType == null)
                return CreateErrorResult("LuaBehaviour type not found");

            // 如果没有指定 gameObjectName，使用 prefab 根物体
            if (string.IsNullOrEmpty(gameObjectName))
                gameObjectName = prefab.name;

            try
            {
                // 查找目标 GameObject
                Transform targetTransform = FindTransformInPrefab(prefab, gameObjectName);
                if (targetTransform == null)
                    return CreateErrorResult($"GameObject '{gameObjectName}' not found in prefab");

                // 获取 LuaBehaviour 组件
                var luaBehaviour = targetTransform.GetComponent(LuaBehaviourType);
                if (luaBehaviour == null)
                    return CreateErrorResult($"LuaBehaviour component not found on '{gameObjectName}'");

                // 记录修改以便撤销
                Undo.RecordObject(luaBehaviour, "Set Lua Parameter via MCP");

                // 使用 SerializedObject 修改参数
                bool success = false;
                if (paramType == "object")
                    success = SetObjectParam(luaBehaviour, key, newValue, prefab);
                else
                    success = SetValueParam(luaBehaviour, key, newValue);

                if (!success)
                    return CreateErrorResult($"Failed to set parameter '{key}'");

                // 标记 prefab 为已修改并保存
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();

                return new SetLuaParamResult
                {
                    success = true,
                    key = key,
                    type = paramType,
                    value = newValue
                };
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Error setting parameter: {ex.Message}");
            }
        }

        /// <summary>
        /// 在 prefab 中查找指定名称的 Transform
        /// </summary>
        private Transform FindTransformInPrefab(GameObject prefab, string name)
        {
            // 先检查根对象
            if (prefab.name == name)
                return prefab.transform;

            // 递归查找子对象
            var allTransforms = prefab.GetComponentsInChildren<Transform>(true);
            foreach (var transform in allTransforms)
            {
                if (transform.name == name)
                    return transform;
            }
            return null;
        }

        /// <summary>
        /// 设置 Object 类型参数
        /// 从 SerializedObjValues 中读取期望的类型，然后在目标物体上查找对应组件
        /// 参考 PrefabLuaTemplateGenerator.DeserializeFromLuaBehaviour() 实现
        /// </summary>
        private bool SetObjectParam(Component luaBehaviour, string key, string targetObjectName, GameObject prefab)
        {
            var so = new SerializedObject(luaBehaviour);
            var objValuesProp = so.FindProperty("SerializedObjValues");
            if (objValuesProp == null) 
            {
                Debug.LogError("[MCP] SerializedObjValues property not found");
                return false;
            }

            // 查找已存在的参数项，获取期望的类型
            Type expectedType = null;
            int index = -1;
            for (int i = 0; i < objValuesProp.arraySize; i++)
            {
                var element = objValuesProp.GetArrayElementAtIndex(i);
                var keyProp = element.FindPropertyRelative("key");
                if (keyProp != null && keyProp.stringValue == key)
                {
                    index = i;
                    // 获取当前绑定的对象，从中推断期望类型
                    var existingValueProp = element.FindPropertyRelative("value");
                    if (existingValueProp != null && existingValueProp.objectReferenceValue != null)
                    {
                        expectedType = existingValueProp.objectReferenceValue.GetType();
                        Debug.Log($"[MCP] Found existing binding for key '{key}', expected type: {expectedType.Name}");
                    }
                    break;
                }
            }

            // 如果没找到现有绑定，默认使用 UnityEngine.Object
            if (expectedType == null)
            {
                expectedType = typeof(UnityEngine.Object);
                Debug.Log($"[MCP] No existing binding for key '{key}', using default type: UnityEngine.Object");
            }

            // 查找目标对象（在 prefab 内部和场景中查找）
            UnityEngine.Object targetObj = FindObjectByName(targetObjectName, prefab, expectedType);
            Debug.Log($"[MCP] Found target object: {targetObj?.name ?? "null"}, Type: {targetObj?.GetType().FullName ?? "null"}");
            
            if (targetObj == null)
                throw new Exception($"Target object '{targetObjectName}' with type '{expectedType.Name}' not found in prefab or scene");

            // 如果找不到，添加新项
            if (index == -1)
            {
                index = objValuesProp.arraySize;
                objValuesProp.InsertArrayElementAtIndex(index);
                var newElement = objValuesProp.GetArrayElementAtIndex(index);
                var keyProp = newElement.FindPropertyRelative("key");
                if (keyProp != null)
                    keyProp.stringValue = key;
            }

            // 设置值
            var elementProp = objValuesProp.GetArrayElementAtIndex(index);
            var valueProp = elementProp.FindPropertyRelative("value");
            if (valueProp != null)
            {
                valueProp.objectReferenceValue = targetObj;
                Debug.Log($"[MCP] Set object reference value: {targetObj.name} ({targetObj.GetType().Name})");
            }

            so.ApplyModifiedProperties();
            Debug.Log($"[MCP] Applied modified properties");
            return true;
        }



        /// <summary>
        /// 设置 Value 类型参数
        /// </summary>
        private bool SetValueParam(Component luaBehaviour, string key, string jsonValue)
        {
            var so = new SerializedObject(luaBehaviour);
            var valuesProp = so.FindProperty("SerializedValues");
            if (valuesProp == null) return false;

            // 查找或创建参数项
            int index = -1;
            for (int i = 0; i < valuesProp.arraySize; i++)
            {
                var element = valuesProp.GetArrayElementAtIndex(i);
                var keyProp = element.FindPropertyRelative("key");
                if (keyProp != null && keyProp.stringValue == key)
                {
                    index = i;
                    break;
                }
            }

            // 如果找不到，添加新项
            if (index == -1)
            {
                index = valuesProp.arraySize;
                valuesProp.InsertArrayElementAtIndex(index);
                var newElement = valuesProp.GetArrayElementAtIndex(index);
                var keyProp = newElement.FindPropertyRelative("key");
                if (keyProp != null)
                    keyProp.stringValue = key;
            }

            // 设置值
            var elementProp = valuesProp.GetArrayElementAtIndex(index);
            var jsonStrProp = elementProp.FindPropertyRelative("jsonStr");
            if (jsonStrProp != null)
            {
                jsonStrProp.stringValue = jsonValue;
            }

            so.ApplyModifiedProperties();
            return true;
        }

        /// <summary>
        /// 通过名称查找对象（先在 prefab 内部查找，再在场景中查找）
        /// 支持路径查找，如 "Panel/Text"
        /// 根据期望类型获取正确的组件
        /// </summary>
        private UnityEngine.Object FindObjectByName(string name, GameObject prefab, Type expectedType)
        {
            Transform targetTransform = null;

            // 1. 先在 prefab 内部查找（支持路径）
            if (name.Contains("/"))
            {
                // 路径查找
                targetTransform = prefab.transform.Find(name);
            }
            else
            {
                // 简单名称查找 - 先在 prefab 内部递归查找
                targetTransform = FindTransformInChildren(prefab.transform, name);
            }

            // 2. 如果没找到，在场景中查找
            if (targetTransform == null)
            {
                GameObject go = GameObject.Find(name);
                if (go != null)
                    targetTransform = go.transform;
            }

            // 3. 如果还没找到，查找所有同名的场景中的 GameObject
            if (targetTransform == null)
            {
                var allObjects = GameObject.FindObjectsOfType<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (obj.name == name)
                    {
                        targetTransform = obj.transform;
                        break;
                    }
                }
            }

            if (targetTransform == null)
                return null;

            // 根据期望类型返回正确的组件
            return GetComponentOfType(targetTransform, expectedType);
        }

        /// <summary>
        /// 根据期望类型从 Transform 上获取组件
        /// </summary>
        private UnityEngine.Object GetComponentOfType(Transform transform, Type expectedType)
        {
            if (expectedType == null || expectedType == typeof(UnityEngine.Object))
                return transform;

            // 如果期望的是 GameObject 类型
            if (expectedType == typeof(GameObject))
                return transform.gameObject;

            // 如果期望的是 Transform 类型
            if (expectedType == typeof(Transform))
                return transform;

            // 尝试获取组件
            Component component = transform.GetComponent(expectedType);
            if (component != null)
                return component;

            // 如果没找到，尝试在子对象中查找（对于某些特殊组件）
            component = transform.GetComponentInChildren(expectedType, true);
            if (component != null)
                return component;

            // 如果还是找不到，返回 Transform 作为后备
            Debug.LogWarning($"[MCP] Component of type '{expectedType.Name}' not found on '{transform.name}', returning Transform instead.");
            return transform;
        }

        /// <summary>
        /// 递归查找子物体
        /// </summary>
        private Transform FindTransformInChildren(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            foreach (Transform child in parent)
            {
                Transform result = FindTransformInChildren(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>
        /// 创建错误结果
        /// </summary>
        private SetLuaParamResult CreateErrorResult(string error)
        {
            return new SetLuaParamResult
            {
                success = false,
                error = error
            };
        }

        #region Reflection Helpers

        private string GetLuaScriptPath(Component luaBehaviour)
        {
            var so = new SerializedObject(luaBehaviour);
            var prop = so.FindProperty("LuaScriptPath");
            return prop?.stringValue;
        }

        private IList<object> GetSerializedObjValues(Component luaBehaviour)
        {
            var so = new SerializedObject(luaBehaviour);
            var prop = so.FindProperty("SerializedObjValues");
            if (prop != null)
            {
                var list = new List<object>();
                for (int i = 0; i < prop.arraySize; i++)
                {
                    list.Add(prop.GetArrayElementAtIndex(i));
                }
                return list;
            }
            return null;
        }

        private IList<object> GetSerializedValues(Component luaBehaviour)
        {
            var so = new SerializedObject(luaBehaviour);
            var prop = so.FindProperty("SerializedValues");
            if (prop != null)
            {
                var list = new List<object>();
                for (int i = 0; i < prop.arraySize; i++)
                {
                    list.Add(prop.GetArrayElementAtIndex(i));
                }
                return list;
            }
            return null;
        }

        #endregion

        #region Private Methods

        private string GetParam(Dictionary<string, string> parameters, string key, string defaultValue = "")
        {
            return parameters.TryGetValue(key, out var value) ? value : defaultValue;
        }

        private string GetGameObjectPath(GameObject obj)
        {
            if (obj.transform.parent == null)
                return obj.name;
            return GetGameObjectPath(obj.transform.parent.gameObject) + "/" + obj.name;
        }

        #endregion

        #region Lua Script Creation

        /// <summary>
        /// 创建 Lua 脚本文件
        /// 在指定路径生成 xxx.lua.txt 文件并附带基础模板
        /// </summary>
        [McpCommand("create_script", "创建 Lua 脚本文件")]
        [McpParameter("filePath", "Lua 文件完整路径", Required = true, Example = "Assets/Game/ResourcesAB/Lua/Module/Activity99/Act99_TestView.lua.txt")]
        [McpParameter("templateType", "模板类型: NewView 或 NewLuaBehaviour", Required = false, Example = "NewView")]
        public object CreateLuaScript(Dictionary<string, string> parameters)
        {
            string filePath = GetParam(parameters, "filePath");
            string templateType = GetParam(parameters, "templateType", "NewView");

            if (string.IsNullOrEmpty(filePath))
                return CreateErrorResult("Parameter 'filePath' is required");

            if (templateType != "NewView" && templateType != "NewLuaBehaviour")
                return CreateErrorResult("Parameter 'templateType' must be 'NewView' or 'NewLuaBehaviour'");

            try
            {
                // 直接使用传入的文件路径
                string fullPath = filePath;
                string directory = Path.GetDirectoryName(fullPath);
                // 从文件名提取脚本名 (去掉 .lua.txt)
                string fileName = Path.GetFileName(fullPath);
                string scriptName = fileName.Replace(".lua.txt", "").Replace(".lua", "");

                // 确保目录存在
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Debug.Log($"[MCP] Created directory: {directory}");
                }

                // 检查文件是否已存在
                if (File.Exists(fullPath))
                {
                    return new CreateScriptResult
                    {
                        success = false,
                        error = $"Lua script already exists at: {fullPath}",
                        filePath = fullPath
                    };
                }

                // 生成模板内容
                string templateContent = GenerateLuaTemplate(scriptName, templateType);

                // 写入文件
                File.WriteAllText(fullPath, templateContent);

                // 刷新 AssetDatabase
                AssetDatabase.Refresh();

                Debug.Log($"[MCP] Created Lua script: {fullPath}");

                return new CreateScriptResult
                {
                    success = true,
                    filePath = fullPath,
                    templateType = templateType
                };
            }
            catch (Exception ex)
            {
                return new CreateScriptResult
                {
                    success = false,
                    error = $"Failed to create Lua script: {ex.Message}",
                    filePath = filePath
                };
            }
        }

        /// <summary>
        /// 生成 Lua 脚本模板
        /// </summary>
        private string GenerateLuaTemplate(string scriptName, string templateType)
        {
            StringBuilder sb = new StringBuilder();

            // DefineList 部分
            sb.AppendLine("local DefineList = {");
            sb.AppendLine("    -- {name = \"ComponentName\", type = typeof(CS.UnityEngine.UI.Text)},");
            sb.AppendLine("}");
            sb.AppendLine("if EXECUTE_IN_EDITOR then return {_DefineList = DefineList} end");
            sb.AppendLine();
            sb.AppendLine("----------------------------------------------------------------");
            sb.AppendLine();

            // 类定义
            if (templateType == "NewLuaBehaviour")
            {
                sb.AppendLine($"local M = NewLuaBehaviour(\"{scriptName}\")");
            }
            else
            {
                sb.AppendLine($"local M = NewView(\"{scriptName}\")");
            }

            sb.AppendLine("M._DefineList = DefineList");
            sb.AppendLine();

            // 生命周期函数
            sb.AppendLine("function M:Awake()");
            sb.AppendLine("    -- 初始化逻辑");
            sb.AppendLine("end");
            sb.AppendLine();

            sb.AppendLine("function M:Start()");
            sb.AppendLine("    -- 启动逻辑");
            sb.AppendLine("end");
            sb.AppendLine();

            sb.AppendLine("function M:OnDestroy()");
            sb.AppendLine("    -- 销毁逻辑");
            sb.AppendLine("end");
            sb.AppendLine();

            sb.AppendLine("return M");

            return sb.ToString();
        }

        #endregion

        #region LuaBehaviour Attachment

        /// <summary>
        /// 将 Lua 脚本绑定到预制体的 LuaBehaviour 组件
        /// 如果 LuaBehaviour 不存在则自动添加
        /// </summary>
        [McpCommand("attach_script", "绑定 Lua 脚本到预制体")]
        [McpParameter("prefabPath", "预制体路径", Required = true, Example = "Assets/Game/ResourcesAB/UI2/G/Act99/Prefab/Act99_Test.prefab")]
        [McpParameter("scriptPath", "Lua 脚本路径（支持文件路径或命名空间格式）", Required = true, Example = "Assets/Game/ResourcesAB/Lua/Module/Activity99/Act99_TestView.lua.txt")]
        [McpParameter("gameObjectName", "目标 GameObject 名称，为空则使用根物体", Required = false, Example = "Act99_Test")]
        public object AttachLuaScript(Dictionary<string, string> parameters)
        {
            string prefabPath = GetParam(parameters, "prefabPath");
            string scriptPath = GetParam(parameters, "scriptPath");
            string gameObjectName = GetParam(parameters, "gameObjectName");

            if (string.IsNullOrEmpty(prefabPath))
                return CreateErrorResult("Parameter 'prefabPath' is required");
            if (string.IsNullOrEmpty(scriptPath))
                return CreateErrorResult("Parameter 'scriptPath' is required");

            // 检查 Lua 文件是否存在（如果传入的是文件路径）
            if (scriptPath.Contains("/") || scriptPath.Contains("\\"))
            {
                string normalizedPath = scriptPath.Replace("\\", "/");
                if (!normalizedPath.StartsWith("Assets/"))
                    normalizedPath = "Assets/" + normalizedPath;
                
                if (!File.Exists(normalizedPath))
                {
                    return new AttachScriptResult
                    {
                        success = false,
                        error = $"Lua script file not found: {normalizedPath}",
                        prefabPath = prefabPath,
                        scriptPath = scriptPath
                    };
                }
                Debug.Log($"[MCP] Verified Lua file exists: {normalizedPath}");
            }

            // 将文件路径转换为命名空间格式
            string luaScriptPath = ConvertToLuaNamespace(scriptPath);
            Debug.Log($"[MCP] Converted script path: {scriptPath} -> {luaScriptPath}");

            // 确保路径以 Assets 开头
            if (!prefabPath.StartsWith("Assets/") && !prefabPath.StartsWith("Assets\\"))
                prefabPath = "Assets/" + prefabPath;

            try
            {
                // 加载预制体
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                    return CreateErrorResult($"Prefab not found at path: {prefabPath}");

                if (LuaBehaviourType == null)
                    return CreateErrorResult("LuaBehaviour type not found");

                // 如果没有指定 gameObjectName，使用 prefab 根物体
                if (string.IsNullOrEmpty(gameObjectName))
                    gameObjectName = prefab.name;

                // 查找目标 GameObject
                Transform targetTransform = FindTransformInPrefab(prefab, gameObjectName);
                if (targetTransform == null)
                    return CreateErrorResult($"GameObject '{gameObjectName}' not found in prefab");

                // 获取或添加 LuaBehaviour 组件
                var luaBehaviour = targetTransform.GetComponent(LuaBehaviourType);
                bool isNewComponent = luaBehaviour == null;

                if (isNewComponent)
                {
                    luaBehaviour = targetTransform.gameObject.AddComponent(LuaBehaviourType);
                    Debug.Log($"[MCP] Added LuaBehaviour component to '{gameObjectName}'");
                }
                else
                {
                    Debug.Log($"[MCP] Found existing LuaBehaviour on '{gameObjectName}'");
                }

                // 记录修改以便撤销
                Undo.RecordObject(luaBehaviour, "Attach Lua Script via MCP");

                // 使用 SerializedObject 设置 LuaScriptPath
                var so = new SerializedObject(luaBehaviour);
                var luaScriptPathProp = so.FindProperty("LuaScriptPath");
                if (luaScriptPathProp != null)
                {
                    luaScriptPathProp.stringValue = luaScriptPath;
                    so.ApplyModifiedProperties();
                }

                // 初始化空的 SerializedObjValues 和 SerializedValues
                var objValuesProp = so.FindProperty("SerializedObjValues");
                if (objValuesProp != null)
                {
                    objValuesProp.ClearArray();
                    so.ApplyModifiedProperties();
                }

                var valuesProp = so.FindProperty("SerializedValues");
                if (valuesProp != null)
                {
                    valuesProp.ClearArray();
                    so.ApplyModifiedProperties();
                }

                // 标记预制体为已修改
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();

                // 验证是否设置成功 - 重新加载预制体检查
                GameObject verifyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (verifyPrefab != null)
                {
                    Transform verifyTransform = FindTransformInPrefab(verifyPrefab, gameObjectName);
                    if (verifyTransform != null)
                    {
                        var verifyBehaviour = verifyTransform.GetComponent(LuaBehaviourType);
                        if (verifyBehaviour != null)
                        {
                            var verifySo = new SerializedObject(verifyBehaviour);
                            var verifyPathProp = verifySo.FindProperty("LuaScriptPath");
                            string verifiedPath = verifyPathProp?.stringValue;
                            
                            if (string.IsNullOrEmpty(verifiedPath))
                            {
                                return new AttachScriptResult
                                {
                                    success = false,
                                    error = "Failed to verify: LuaScriptPath is empty after save",
                                    prefabPath = prefabPath,
                                    gameObjectName = gameObjectName,
                                    scriptPath = luaScriptPath
                                };
                            }
                            
                            if (verifiedPath != luaScriptPath)
                            {
                                return new AttachScriptResult
                                {
                                    success = false,
                                    error = $"Failed to verify: LuaScriptPath mismatch (expected: {luaScriptPath}, actual: {verifiedPath})",
                                    prefabPath = prefabPath,
                                    gameObjectName = gameObjectName,
                                    scriptPath = luaScriptPath
                                };
                            }
                            
                            Debug.Log($"[MCP] Verified Lua script attached: {verifiedPath}");
                            
                            // 验证成功，返回结果
                            return new AttachScriptResult
                            {
                                success = true,
                                prefabPath = prefabPath,
                                gameObjectName = gameObjectName,
                                scriptPath = luaScriptPath,
                                isNewComponent = isNewComponent
                            };
                        }
                        else
                        {
                            return new AttachScriptResult
                            {
                                success = false,
                                error = "Failed to verify: LuaBehaviour component not found after save",
                                prefabPath = prefabPath,
                                gameObjectName = gameObjectName,
                                scriptPath = luaScriptPath
                            };
                        }
                    }
                    else
                    {
                        return new AttachScriptResult
                        {
                            success = false,
                            error = "Failed to verify: Target GameObject not found after save",
                            prefabPath = prefabPath,
                            gameObjectName = gameObjectName,
                            scriptPath = luaScriptPath
                        };
                    }
                }
                else
                {
                    return new AttachScriptResult
                    {
                        success = false,
                        error = "Failed to verify: Could not reload prefab after save",
                        prefabPath = prefabPath,
                        gameObjectName = gameObjectName,
                        scriptPath = luaScriptPath
                    };
                }
            }
            catch (Exception ex)
            {
                return new AttachScriptResult
                {
                    success = false,
                    error = $"Failed to attach Lua script: {ex.Message}",
                    prefabPath = prefabPath,
                    scriptPath = luaScriptPath
                };
            }
        }

        /// <summary>
        /// 将文件路径转换为 Lua 命名空间格式
        /// 例如: Assets/Game/ResourcesAB/Lua/Module/Activity99/Act99_TestView.lua.txt -> Module.Activity99.Act99_TestView
        /// </summary>
        private string ConvertToLuaNamespace(string scriptPath)
        {
            // 如果已经是命名空间格式（没有 / 和 \），直接返回
            if (!scriptPath.Contains("/") && !scriptPath.Contains("\\"))
                return scriptPath;

            // 移除 Assets/Game/ResourcesAB/Lua/ 前缀
            string[] prefixesToRemove = new string[]
            {
                "Assets/Game/ResourcesAB/Lua/",
                "Assets/Game/Lua/",
                "Assets/Lua/",
                "Assets/"
            };

            string result = scriptPath.Replace("\\", "/");
            
            foreach (var prefix in prefixesToRemove)
            {
                if (result.StartsWith(prefix))
                {
                    result = result.Substring(prefix.Length);
                    break;
                }
            }

            // 移除 .lua.txt 后缀
            if (result.EndsWith(".lua.txt"))
                result = result.Substring(0, result.Length - 8);
            else if (result.EndsWith(".lua"))
                result = result.Substring(0, result.Length - 4);

            // 将 / 替换为 .
            result = result.Replace("/", ".");

            return result;
        }

        #endregion

        #region Result Classes

        [Serializable]
        public class LuaParamsResult
        {
            public bool success;
            public string error;
            public string gameObjectName;
            public string luaScriptPath;
            public LuaObjectParam[] objectParams;
            public LuaValueParam[] valueParams;
        }

        [Serializable]
        public class LuaObjectParam
        {
            public string key;
            public string type;
            public string value;
        }

        [Serializable]
        public class LuaValueParam
        {
            public string key;
            public string jsonValue;
        }

        [Serializable]
        public class SetLuaParamResult
        {
            public bool success;
            public string error;
            public string key;
            public string type;
            public string value;
        }

        [Serializable]
        public class CreateScriptResult
        {
            public bool success;
            public string error;
            public string filePath;
            public string templateType;
        }

        [Serializable]
        public class AttachScriptResult
        {
            public bool success;
            public string error;
            public string prefabPath;
            public string gameObjectName;
            public string scriptPath;
            public bool isNewComponent;
        }

        [Serializable]
        public class LuaBehaviourInfo
        {
            public string gameObjectName;
            public string path;
            public string luaScriptPath;
            public int paramCount;
        }

        [Serializable]
        public class FindLuaBehavioursResult
        {
            public bool success;
            public string error;
            public int count;
            public LuaBehaviourInfo[] behaviours;
        }

        [Serializable]
        public class PrefabLuaParamsResult
        {
            public bool success;
            public string error;
            public string prefabPath;
            public PrefabLuaBehaviourInfo[] luaBehaviours;
        }

        [Serializable]
        public class PrefabLuaBehaviourInfo
        {
            public string gameObjectName;
            public string gameObjectFileID;
            public string luaScriptPath;
            public List<LuaObjectParam> objectParams;
            public List<LuaValueParam> valueParams;
        }

        #endregion
    }
}
