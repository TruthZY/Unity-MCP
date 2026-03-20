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
        public object GetHierarchy(Dictionary<string, string> parameters)
        {
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            return new HierarchyResult 
            { 
                sceneName = scene.name, 
                rootCount = rootObjects.Length,
                objects = rootObjects.Select(SerializeGameObject).ToArray()
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
                target = GameObject.Find(path);
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
                var parent = GameObject.Find(parentPath);
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
                target = GameObject.Find(path);
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
        [McpParameter("value", "属性值（JSON格式）", Required = true, Example = "{\"x\":0,\"y\":100,\"z\":0}")]
        public object SetProperty(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string componentName = GetParam(parameters, "component");
            string propertyName = GetParam(parameters, "property");

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(componentName) || string.IsNullOrEmpty(propertyName))
            {
                return new SetPropertyResult { success = false, error = "Missing required parameters" };
            }

            var obj = GameObject.Find(path);
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

            var obj = GameObject.Find(path);
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

        [McpCommand("add_component", "给GameObject添加组件")]
        [McpParameter("path", "对象完整路径", Required = true, Example = "Canvas/Panel/Button")]
        [McpParameter("type", "组件类型全称", Required = true, Example = "UnityEngine.UI.Button")]
        public object AddComponent(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string componentType = GetParam(parameters, "type");

            var obj = GameObject.Find(path);
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
            return Convert.ChangeType(value, targetType);
        }

        private string GetGameObjectPath(GameObject obj)
        {
            if (obj.transform.parent == null)
                return obj.name;
            return GetGameObjectPath(obj.transform.parent.gameObject) + "/" + obj.name;
        }

        private GameObjectInfo SerializeGameObject(GameObject obj)
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
            if (childCount > 0 && childCount <= 10)
            {
                info.children = new GameObjectInfo[childCount];
                for (int i = 0; i < childCount; i++)
                {
                    info.children[i] = SerializeGameObject(obj.transform.GetChild(i).gameObject);
                }
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

        #endregion
    }
}
