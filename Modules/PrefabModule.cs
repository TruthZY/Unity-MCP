using System;
using System.Collections.Generic;
using System.Linq;
using McpUnity.Core;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Modules
{
    /// <summary>
    /// 预制体分析模块
    /// </summary>
    [McpModule("prefab")]
    public class PrefabModule : IMcpModule
    {
        public string ModuleName => "prefab";

        [McpCommand("get_hierarchy")]
        public object GetHierarchy(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");

            if (string.IsNullOrEmpty(path))
            {
                return new PrefabHierarchyResult { success = false, error = "Path is required" };
            }

            // 安全检查
            if (!path.StartsWith("Assets"))
            {
                return new PrefabHierarchyResult { success = false, error = "Path must be in Assets folder" };
            }

            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    return new PrefabHierarchyResult { success = false, error = $"Prefab not found: {path}" };
                }

                var rootInfo = SerializePrefabGameObject(prefab, true);

                return new PrefabHierarchyResult
                {
                    success = true,
                    name = prefab.name,
                    path = path,
                    root = rootInfo
                };
            }
            catch (Exception ex)
            {
                return new PrefabHierarchyResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("get_components")]
        public object GetComponents(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string objectPath = GetParam(parameters, "objectPath");

            if (string.IsNullOrEmpty(path))
            {
                return new PrefabComponentsResult { success = false, error = "Prefab path is required" };
            }

            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    return new PrefabComponentsResult { success = false, error = $"Prefab not found: {path}" };
                }

                // 查找目标对象
                Transform target = prefab.transform;
                if (!string.IsNullOrEmpty(objectPath) && objectPath != prefab.name)
                {
                    target = prefab.transform.Find(objectPath);
                    if (target == null)
                    {
                        return new PrefabComponentsResult { success = false, error = $"Object not found: {objectPath}" };
                    }
                }

                var components = target.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => new ComponentDetail
                    {
                        type = c.GetType().Name,
                        fullTypeName = c.GetType().FullName,
                        enabled = c is Behaviour behaviour ? behaviour.enabled : true
                    }).ToArray();

                return new PrefabComponentsResult
                {
                    success = true,
                    prefabName = prefab.name,
                    objectName = target.name,
                    objectPath = objectPath ?? prefab.name,
                    components = components
                };
            }
            catch (Exception ex)
            {
                return new PrefabComponentsResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("find_objects")]
        public object FindObjects(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string name = GetParam(parameters, "name");
            string componentType = GetParam(parameters, "componentType");

            if (string.IsNullOrEmpty(path))
            {
                return new FindObjectsResult { success = false, error = "Prefab path is required" };
            }

            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    return new FindObjectsResult { success = false, error = $"Prefab not found: {path}" };
                }

                // 获取所有子物体
                var allTransforms = prefab.GetComponentsInChildren<Transform>(true);
                var results = new List<PrefabObjectInfo>();

                foreach (var t in allTransforms)
                {
                    bool match = true;

                    // 按名称过滤
                    if (!string.IsNullOrEmpty(name) && !t.name.Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                    }

                    // 按组件类型过滤
                    if (!string.IsNullOrEmpty(componentType))
                    {
                        var hasComponent = t.GetComponents<Component>()
                            .Any(c => c != null && c.GetType().Name.Equals(componentType, StringComparison.OrdinalIgnoreCase));
                        if (!hasComponent)
                        {
                            match = false;
                        }
                    }

                    if (match)
                    {
                        results.Add(new PrefabObjectInfo
                        {
                            name = t.name,
                            path = GetTransformPath(t),
                            childCount = t.childCount,
                            componentTypes = t.GetComponents<Component>()
                                .Where(c => c != null)
                                .Select(c => c.GetType().Name)
                                .ToArray()
                        });
                    }
                }

                return new FindObjectsResult
                {
                    success = true,
                    prefabName = prefab.name,
                    foundCount = results.Count,
                    objects = results.ToArray()
                };
            }
            catch (Exception ex)
            {
                return new FindObjectsResult { success = false, error = ex.Message };
            }
        }

        #region Helper Methods

        private string GetParam(Dictionary<string, string> parameters, string key, string defaultValue = "")
        {
            return parameters.TryGetValue(key, out var value) ? value : defaultValue;
        }

        private PrefabGameObjectInfo SerializePrefabGameObject(GameObject obj, bool isRoot = false)
        {
            var info = new PrefabGameObjectInfo
            {
                name = obj.name,
                active = obj.activeSelf,
                tag = obj.tag,
                layer = obj.layer,
                isRoot = isRoot,
                components = obj.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => new ComponentInfo
                    {
                        type = c.GetType().Name,
                        enabled = c is Behaviour behaviour ? behaviour.enabled : true
                    }).ToArray()
            };

            int childCount = obj.transform.childCount;
            info.childCount = childCount;
            if (childCount > 0)
            {
                info.children = new PrefabGameObjectInfo[childCount];
                for (int i = 0; i < childCount; i++)
                {
                    info.children[i] = SerializePrefabGameObject(obj.transform.GetChild(i).gameObject);
                }
            }

            return info;
        }

        private string GetTransformPath(Transform t)
        {
            if (t.parent == null)
                return t.name;
            return GetTransformPath(t.parent) + "/" + t.name;
        }

        #endregion

        #region Result Classes

        [Serializable]
        public class PrefabHierarchyResult
        {
            public bool success;
            public string name;
            public string path;
            public PrefabGameObjectInfo root;
            public string error;
        }

        [Serializable]
        public class PrefabGameObjectInfo
        {
            public string name;
            public bool active;
            public string tag;
            public int layer;
            public bool isRoot;
            public ComponentInfo[] components;
            public int childCount;
            public PrefabGameObjectInfo[] children;
        }

        [Serializable]
        public class ComponentInfo
        {
            public string type;
            public bool enabled;
        }

        [Serializable]
        public class PrefabComponentsResult
        {
            public bool success;
            public string prefabName;
            public string objectName;
            public string objectPath;
            public ComponentDetail[] components;
            public string error;
        }

        [Serializable]
        public class ComponentDetail
        {
            public string type;
            public string fullTypeName;
            public bool enabled;
        }

        [Serializable]
        public class FindObjectsResult
        {
            public bool success;
            public string prefabName;
            public int foundCount;
            public PrefabObjectInfo[] objects;
            public string error;
        }

        [Serializable]
        public class PrefabObjectInfo
        {
            public string name;
            public string path;
            public int childCount;
            public string[] componentTypes;
        }

        #endregion
    }
}
