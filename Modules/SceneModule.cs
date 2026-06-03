using System;
using System.Collections.Generic;
using System.Linq;
using McpUnity.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace McpUnity.Modules
{
    /// <summary>
    /// 场景操作模块
    /// </summary>
    [McpModule("scene")]
    public class SceneModule : IMcpModule
    {
        public string ModuleName => "scene";

        [McpCommand("ping", "测试服务器连接")]
        public object Ping(Dictionary<string, string> parameters)
        {
            return new PingResult 
            { 
                message = "pong", 
                unityVersion = Application.unityVersion, 
                timestamp = DateTime.Now.ToString("O") 
            };
        }

        [McpCommand("get_hierarchy", "获取当前场景的完整层级结构")]
        [McpParameter("maxDepth", "递归深度: 0=仅根对象(默认), >0=递归展开子对象层数, -1=无限递归", Required = false, DefaultValue = "0", Example = "2")]
        [McpParameter("path", "起始对象路径(可选): 不传=从场景根对象开始, 传值=从指定对象开始往下递归", Required = false, Example = "UIRoot(Clone)")]
        public object GetHierarchy(Dictionary<string, string> parameters)
        {
            int maxDepth = 0;
            if (parameters.TryGetValue("maxDepth", out var depthStr))
            {
                int.TryParse(depthStr, out maxDepth);
            }

            string path = GetParam(parameters, "path");

            if (!string.IsNullOrEmpty(path))
            {
                // 从指定路径开始获取层级（支持未激活对象）
                var obj = FindGameObjectByPath(path);
                if (obj == null)
                    return new HierarchyResult { success = false, error = $"Object not found: {path}" };

                return new HierarchyResult
                {
                    success = true,
                    sceneName = SceneManager.GetActiveScene().name,
                    rootCount = 1,
                    objects = new[] { SerializeGameObject(obj, 0, maxDepth) }
                };
            }

            // 默认行为：获取场景根对象
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            return new HierarchyResult 
            { 
                success = true,
                sceneName = scene.name, 
                rootCount = rootObjects.Length,
                objects = rootObjects.Select(obj => SerializeGameObject(obj, 0, maxDepth)).ToArray()
            };
        }

        [McpCommand("select_object", "在场景中选中指定GameObject")]
        [McpParameter("path", "对象完整路径", Required = false, Example = "Canvas/Panel/Button")]
        [McpParameter("name", "对象名称（path和name二选一）", Required = false, Example = "MainCamera")]
        public object SelectObject(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string name = GetParam(parameters, "name");

            GameObject target = null;
            if (!string.IsNullOrEmpty(path))
            {
                target = FindGameObjectByPath(path);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                target = GameObject.Find(name);
            }

            if (target != null)
            {
                Selection.activeGameObject = target;
                EditorGUIUtility.PingObject(target);
                return new SelectResult { selected = true, name = target.name, path = GetGameObjectPath(target) };
            }

            return new SelectResult { selected = false, error = "Object not found" };
        }

        [McpCommand("create_object", "在场景中创建新GameObject")]
        [McpParameter("name", "对象名称", Required = false, DefaultValue = "New GameObject", Example = "MyButton")]
        [McpParameter("primitiveType", "基础类型，如 Cube, Sphere, Cylinder", Required = false, Example = "Cube")]
        [McpParameter("parent", "父对象路径", Required = false, Example = "Canvas/Panel")]
        public object CreateObject(Dictionary<string, string> parameters)
        {
            string name = GetParam(parameters, "name", "New GameObject");
            string primitiveType = GetParam(parameters, "primitiveType");
            string parentPath = GetParam(parameters, "parent");

            GameObject obj;
            if (!string.IsNullOrEmpty(primitiveType) && System.Enum.TryParse<PrimitiveType>(primitiveType, true, out var pt))
            {
                obj = GameObject.CreatePrimitive(pt);
                obj.name = name;
            }
            else
            {
                obj = new GameObject(name);
            }

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = FindGameObjectByPath(parentPath);
                if (parent != null)
                {
                    obj.transform.SetParent(parent.transform, false);
                }
            }

            Undo.RegisterCreatedObjectUndo(obj, "Create Object via MCP");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            return new CreateResult { created = true, name = obj.name, path = GetGameObjectPath(obj) };
        }

        [McpCommand("delete_object", "删除场景中的GameObject")]
        [McpParameter("path", "对象完整路径", Required = false, Example = "Canvas/Panel/OldButton")]
        [McpParameter("name", "对象名称（path和name二选一）", Required = false, Example = "TempObject")]
        public object DeleteObject(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string name = GetParam(parameters, "name");

            GameObject target = null;
            if (!string.IsNullOrEmpty(path))
            {
                target = FindGameObjectByPath(path);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                target = GameObject.Find(name);
            }

            if (target != null)
            {
                string targetName = target.name; // 先保存名称
                Undo.DestroyObjectImmediate(target);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                return new DeleteResult { deleted = true, name = targetName };
            }

            return new DeleteResult { deleted = false, error = "Object not found" };
        }

        [McpCommand("set_property", "设置GameObject组件的属性值")]
        [McpParameter("path", "对象完整路径", Required = true, Example = "Canvas/Panel/Button")]
        [McpParameter("component", "组件名称", Required = true, Example = "Transform")]
        [McpParameter("property", "属性名称", Required = true, Example = "localPosition")]
        [McpParameter("value", "属性值。简单格式：Vector2/3用逗号分隔如\"100,-50,0\"，Color用逗号分隔如\"1,0,0,1\"，基础类型直接写值", Required = true, Example = "100,-50,0")]
        public object SetProperty(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string componentName = GetParam(parameters, "component");
            string propertyName = GetParam(parameters, "property");

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(componentName) || string.IsNullOrEmpty(propertyName))
            {
                return new SetPropertyResult { success = false, error = "Missing required parameters" };
            }

            var obj = FindGameObjectByPath(path);
            if (obj == null)
            {
                return new SetPropertyResult { success = false, error = "Object not found" };
            }

            var component = obj.GetComponent(componentName);
            if (component == null)
            {
                return new SetPropertyResult { success = false, error = $"Component '{componentName}' not found" };
            }

            var type = component.GetType();
            var field = type.GetField(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var property = type.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            try
            {
                Undo.RecordObject(component, "Set Property via MCP");

                if (parameters.TryGetValue("value", out var valueStr))
                {
                    object value = ParseValue(valueStr);
                    
                    if (field != null)
                    {
                        field.SetValue(component, ConvertValue(value, field.FieldType));
                    }
                    else if (property != null && property.CanWrite)
                    {
                        property.SetValue(component, ConvertValue(value, property.PropertyType));
                    }
                    else
                    {
                        return new SetPropertyResult { success = false, error = $"Property '{propertyName}' not found or not writable" };
                    }
                }

                EditorUtility.SetDirty(component);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

                return new SetPropertyResult { success = true, component = componentName, property = propertyName };
            }
            catch (Exception ex)
            {
                return new SetPropertyResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("get_property", "获取GameObject组件的属性值")]
        [McpParameter("path", "对象完整路径", Required = true, Example = "Canvas/Panel/Button")]
        [McpParameter("component", "组件名称", Required = true, Example = "RectTransform")]
        [McpParameter("property", "属性名称", Required = true, Example = "anchoredPosition")]
        public object GetProperty(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string componentName = GetParam(parameters, "component");
            string propertyName = GetParam(parameters, "property");

            var obj = FindGameObjectByPath(path);
            if (obj == null)
            {
                return new GetPropertyResult { error = "Object not found" };
            }

            var component = obj.GetComponent(componentName);
            if (component == null)
            {
                return new GetPropertyResult { error = $"Component '{componentName}' not found" };
            }

            var type = component.GetType();
            var field = type.GetField(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var property = type.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            object value = null;
            if (field != null)
            {
                value = field.GetValue(component);
            }
            else if (property != null)
            {
                value = property.GetValue(component);
            }

            return new GetPropertyResult { component = componentName, property = propertyName, value = value?.ToString() };
        }

        [McpCommand("execute_menu", "执行Unity编辑器菜单命令")]
        [McpParameter("path", "菜单路径", Required = true, Example = "GameObject/3D Object/Cube")]
        public object ExecuteMenu(Dictionary<string, string> parameters)
        {
            string menuPath = GetParam(parameters, "path");
            if (string.IsNullOrEmpty(menuPath))
            {
                return new MenuResult { executed = false, error = "Menu path is required" };
            }

            EditorApplication.ExecuteMenuItem(menuPath);
            return new MenuResult { executed = true, path = menuPath };
        }

        [McpCommand("get_children", "获取指定GameObject的直接子对象列表")]
        [McpParameter("path", "对象完整路径", Required = true, Example = "UIRoot(Clone)")]
        public object GetChildren(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            if (string.IsNullOrEmpty(path))
                return new GetChildrenResult { success = false, error = "Parameter 'path' is required" };

            // 使用手动遍历，支持查找未激活的对象
            var obj = FindGameObjectByPath(path);
            if (obj == null)
                return new GetChildrenResult { success = false, error = $"Object not found: {path}" };

            // 使用 GetComponentsInChildren(includeInactive:true) 确保包含隐藏的子系统对象
            var allTransforms = obj.GetComponentsInChildren<Transform>(true);
            var childList = new List<ChildInfo>();
            foreach (var t in allTransforms)
            {
                // 只取直接子对象（parent 是当前对象）
                if (t.parent == obj.transform)
                {
                    childList.Add(new ChildInfo
                    {
                        name = t.name,
                        path = GetGameObjectPath(t.gameObject),
                        active = t.gameObject.activeSelf,
                        childCount = t.childCount,
                        components = t.GetComponents<Component>().Select(c => c.GetType().Name).ToArray()
                    });
                }
            }

            return new GetChildrenResult
            {
                success = true,
                parentPath = path,
                childCount = childList.Count,
                children = childList.ToArray()
            };
        }

        [McpCommand("add_component", "给GameObject添加组件")]
        [McpParameter("path", "对象完整路径", Required = true, Example = "Canvas/Panel/Button")]
        [McpParameter("type", "组件类型全称", Required = true, Example = "UnityEngine.UI.Button")]
        public object AddComponent(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string componentType = GetParam(parameters, "type");

            var obj = FindGameObjectByPath(path);
            if (obj == null)
            {
                return new AddComponentResult { success = false, error = "Object not found" };
            }

            var type = System.Type.GetType(componentType) ??
                      System.AppDomain.CurrentDomain.GetAssemblies()
                          .SelectMany(a => a.GetTypes())
                          .FirstOrDefault(t => t.Name == componentType || t.FullName == componentType);

            if (type == null)
            {
                return new AddComponentResult { success = false, error = $"Type '{componentType}' not found" };
            }

            Undo.AddComponent(obj, type);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            return new AddComponentResult { success = true, type = type.Name };
        }

        #region Helper Methods

        private string GetParam(Dictionary<string, string> parameters, string key, string defaultValue = "")
        {
            return parameters.TryGetValue(key, out var value) ? value : defaultValue;
        }

        private object ParseValue(string valueStr)
        {
            valueStr = valueStr.Trim();
            if (valueStr.StartsWith("\"") && valueStr.EndsWith("\""))
                return valueStr.Trim('"');
            if (bool.TryParse(valueStr, out bool b))
                return b;
            if (int.TryParse(valueStr, out int i))
                return i;
            if (float.TryParse(valueStr, out float f))
                return f;
            return valueStr;
        }

        private object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;
            
            // 处理字符串格式的 Vector2/Vector3/Color
            if (value is string strValue)
            {
                // Vector2: "x,y"
                if (targetType == typeof(Vector2))
                {
                    var parts = strValue.Split(',');
                    if (parts.Length >= 2)
                    {
                        return new Vector2(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim())
                        );
                    }
                }
                // Vector3: "x,y,z"
                if (targetType == typeof(Vector3))
                {
                    var parts = strValue.Split(',');
                    if (parts.Length >= 3)
                    {
                        return new Vector3(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim()),
                            float.Parse(parts[2].Trim())
                        );
                    }
                }
                // Color: "r,g,b" or "r,g,b,a"
                if (targetType == typeof(Color))
                {
                    var parts = strValue.Split(',');
                    if (parts.Length >= 3)
                    {
                        return new Color(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim()),
                            float.Parse(parts[2].Trim()),
                            parts.Length >= 4 ? float.Parse(parts[3].Trim()) : 1f
                        );
                    }
                }
            }
            
            return Convert.ChangeType(value, targetType);
        }

        private string GetGameObjectPath(GameObject obj)
        {
            if (obj.transform.parent == null)
                return obj.name;
            return GetGameObjectPath(obj.transform.parent.gameObject) + "/" + obj.name;
        }

        /// <summary>
        /// 通过路径查找 GameObject，支持未激活（隐藏）的对象。
        /// GameObject.Find() 只能找到激活对象，此方法通过手动遍历 Transform 层级来查找。
        /// </summary>
        private GameObject FindGameObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');

            // 在所有场景根对象中查找第一个匹配的部分
            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            Transform current = null;

            for (int i = 0; i < parts.Length; i++)
            {
                if (i == 0)
                {
                    // 根层级：在场景根对象中查找
                    foreach (var root in rootObjects)
                    {
                        if (root.name == parts[0])
                        {
                            current = root.transform;
                            break;
                        }
                    }
                }
                else
                {
                    // 子层级：遍历当前 Transform 的所有子对象（包括未激活的）
                    Transform next = null;
                    for (int j = 0; j < current.childCount; j++)
                    {
                        var child = current.GetChild(j);
                        if (child.name == parts[i])
                        {
                            next = child;
                            break;
                        }
                    }
                    current = next;
                }

                if (current == null)
                    return null;
            }

            return current?.gameObject;
        }

        private GameObjectInfo SerializeGameObject(GameObject obj, int currentDepth = 0, int maxDepth = 0)
        {
            var info = new GameObjectInfo
            {
                name = obj.name,
                active = obj.activeSelf,
                path = GetGameObjectPath(obj),
                components = obj.GetComponents<Component>().Select(c => c.GetType().Name).ToArray()
            };

            int childCount = obj.transform.childCount;
            info.childCount = childCount;

            // maxDepth: 0=不展开子对象, >0=递归到指定深度, -1=无限递归
            bool shouldExpand = maxDepth == -1 || currentDepth < maxDepth;
            if (childCount > 0 && shouldExpand)
            {
                // 使用 GetComponentsInChildren(includeInactive:true) 确保包含隐藏的子对象
                var allTransforms = obj.GetComponentsInChildren<Transform>(true);
                var directChildren = new List<GameObjectInfo>();
                foreach (var t in allTransforms)
                {
                    if (t.parent == obj.transform)
                    {
                        directChildren.Add(SerializeGameObject(t.gameObject, currentDepth + 1, maxDepth));
                    }
                }
                info.children = directChildren.ToArray();
            }

            return info;
        }

        #endregion

        #region Result Classes

        [Serializable]
        public class PingResult
        {
            public string message;
            public string unityVersion;
            public string timestamp;
        }

        [Serializable]
        public class HierarchyResult
        {
            public bool success;
            public string error;
            public string sceneName;
            public int rootCount;
            public GameObjectInfo[] objects;
        }

        [Serializable]
        public class GameObjectInfo
        {
            public string name;
            public bool active;
            public string path;
            public string[] components;
            public int childCount;
            public GameObjectInfo[] children;
        }

        [Serializable]
        public class SelectResult
        {
            public bool selected;
            public string name;
            public string path;
            public string error;
        }

        [Serializable]
        public class CreateResult
        {
            public bool created;
            public string name;
            public string path;
        }

        [Serializable]
        public class DeleteResult
        {
            public bool deleted;
            public string name;
            public string error;
        }

        [Serializable]
        public class SetPropertyResult
        {
            public bool success;
            public string component;
            public string property;
            public string error;
        }

        [Serializable]
        public class GetPropertyResult
        {
            public string component;
            public string property;
            public string value;
            public string error;
        }

        [Serializable]
        public class MenuResult
        {
            public bool executed;
            public string path;
            public string error;
        }

        [Serializable]
        public class ComponentsResult
        {
            public string objectName;
            public string[] components;
            public string error;
        }

        [Serializable]
        public class AddComponentResult
        {
            public bool success;
            public string type;
            public string error;
        }

        [Serializable]
        public class GetChildrenResult
        {
            public bool success;
            public string error;
            public string parentPath;
            public int childCount;
            public ChildInfo[] children;
        }

        [Serializable]
        public class ChildInfo
        {
            public string name;
            public string path;
            public bool active;
            public int childCount;
            public string[] components;
        }

        #endregion
    }
}
