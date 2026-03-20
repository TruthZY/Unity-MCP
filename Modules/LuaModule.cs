using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
