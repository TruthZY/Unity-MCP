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

        [McpCommand("get_hierarchy", "获取Prefab的完整层级结构")]
        [McpParameter("path", "Prefab文件路径", Required = true, Example = "Assets/UI/Prefabs/MainView.prefab")]
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

        [McpCommand("find_objects_or_components", "在Prefab中搜索对象")]
        [McpParameter("path", "Prefab文件路径", Required = true, Example = "Assets/UI/Prefabs/MainView.prefab")]
        [McpParameter("name", "按名称过滤", Required = false, Example = "Button")]
        [McpParameter("componentType", "按组件类型过滤", Required = false, Example = "UnityEngine.UI.Image")]
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

        [McpCommand("add_child", "向预制体添加子物体")]
        [McpParameter("path", "Prefab文件路径", Required = true, Example = "Assets/UI/Prefabs/MainView.prefab")]
        [McpParameter("parent", "父物体路径，空字符串表示根物体", Required = false, DefaultValue = "", Example = "Panel/Bottom")]
        [McpParameter("name", "新物体名称", Required = true, Example = "NewButton")]
        [McpParameter("components", "组件类型列表（逗号分隔）", Required = false, Example = "RectTransform,Image,Button")]
        public object AddChild(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string parentPath = GetParam(parameters, "parent");
            string name = GetParam(parameters, "name");
            string componentsStr = GetParam(parameters, "components");

            if (string.IsNullOrEmpty(path))
                return new AddChildResult { success = false, error = "Prefab path is required" };
            if (string.IsNullOrEmpty(name))
                return new AddChildResult { success = false, error = "Name is required" };

            try
            {
                // 使用 EditPrefabContentsScope 编辑预制体
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    GameObject prefabRoot = editScope.prefabContentsRoot;

                    // 查找父物体
                    Transform parent = prefabRoot.transform;
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        parent = FindTransformInPrefab(prefabRoot, parentPath);
                        if (parent == null)
                            return new AddChildResult { success = false, error = $"Parent '{parentPath}' not found" };
                    }

                    // 创建新物体
                    GameObject newObj = new GameObject(name);
                    newObj.transform.SetParent(parent, false);

                    // 添加组件
                    var addedComponents = new List<string>();
                    if (!string.IsNullOrEmpty(componentsStr))
                    {
                        var componentTypes = componentsStr.Split(',');
                        foreach (var typeName in componentTypes)
                        {
                            var trimmedName = typeName.Trim();
                            if (string.IsNullOrEmpty(trimmedName)) continue;

                            Type componentType = GetComponentType(trimmedName);
                            if (componentType != null && typeof(Component).IsAssignableFrom(componentType))
                            {
                                newObj.AddComponent(componentType);
                                addedComponents.Add(componentType.Name);
                            }
                        }
                    }

                    // 计算完整路径
                    string fullPath = GetTransformPath(newObj.transform);

                    // 保存会自动在 using 结束时执行
                    return new AddChildResult
                    {
                        success = true,
                        name = name,
                        fullPath = fullPath,
                        addedComponents = addedComponents.ToArray()
                    };
                }
            }
            catch (Exception ex)
            {
                return new AddChildResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("add_component", "向预制体内的物体添加组件")]
        [McpParameter("path", "Prefab文件路径", Required = true, Example = "Assets/UI/Prefabs/MainView.prefab")]
        [McpParameter("objectPath", "物体路径，空字符串表示根物体", Required = false, DefaultValue = "", Example = "Panel/Button")]
        [McpParameter("componentType", "组件类型名称", Required = true, Example = "Button")]
        public object AddComponent(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string objectPath = GetParam(parameters, "objectPath");
            string componentTypeName = GetParam(parameters, "componentType");

            if (string.IsNullOrEmpty(path))
                return new AddComponentResult { success = false, error = "Prefab path is required" };
            if (string.IsNullOrEmpty(componentTypeName))
                return new AddComponentResult { success = false, error = "Component type is required" };

            try
            {
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    GameObject prefabRoot = editScope.prefabContentsRoot;

                    Transform target = FindTransformInPrefab(prefabRoot, objectPath);
                    if (target == null)
                        return new AddComponentResult { success = false, error = $"Object '{objectPath}' not found" };

                    Type componentType = GetComponentType(componentTypeName);
                    Debug.Log($"[MCP] Looking for component type: {componentTypeName}, found: {componentType?.FullName ?? "null"}");
                    
                    if (componentType == null)
                        return new AddComponentResult { success = false, error = $"Component type '{componentTypeName}' not found" };

                    if (!typeof(Component).IsAssignableFrom(componentType))
                        return new AddComponentResult { success = false, error = $"Type '{componentTypeName}' is not a Component" };

                    var addedComponent = target.gameObject.AddComponent(componentType);
                    Debug.Log($"[MCP] Added component: {addedComponent?.GetType().Name ?? "null"} to {target.name}");
                    
                    // 强制标记修改
                    EditorUtility.SetDirty(target.gameObject);
                    EditorUtility.SetDirty(prefabRoot);

                    return new AddComponentResult
                    {
                        success = true,
                        objectPath = objectPath,
                        componentType = componentType.Name
                    };
                }
            }
            catch (Exception ex)
            {
                return new AddComponentResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("set_property", "设置预制体内组件的属性")]
        [McpParameter("path", "Prefab文件路径", Required = true, Example = "Assets/UI/Prefabs/MainView.prefab")]
        [McpParameter("objectPath", "物体路径", Required = true, Example = "Panel/Button")]
        [McpParameter("component", "组件类型名称", Required = true, Example = "RectTransform")]
        [McpParameter("property", "属性名称", Required = true, Example = "anchoredPosition")]
        [McpParameter("value", "属性值。简单格式：Vector2/3用逗号分隔如\"100,-50\"，Color用逗号分隔如\"1,0,0,1\"，基础类型直接写值", Required = true, Example = "100,-50")]
        public object SetProperty(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string objectPath = GetParam(parameters, "objectPath");
            string componentName = GetParam(parameters, "component");
            string propertyName = GetParam(parameters, "property");
            string valueStr = GetParam(parameters, "value");

            Debug.Log($"[MCP] SetProperty called with path={path}, objectPath={objectPath}, component={componentName}, property={propertyName}, value={valueStr}");

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(objectPath) || 
                string.IsNullOrEmpty(componentName) || string.IsNullOrEmpty(propertyName))
            {
                return new SetPropertyResult { success = false, error = "Missing required parameters" };
            }

            try
            {
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    GameObject prefabRoot = editScope.prefabContentsRoot;

                    Transform target = FindTransformInPrefab(prefabRoot, objectPath);
                    if (target == null)
                        return new SetPropertyResult { success = false, error = $"Object '{objectPath}' not found" };

                    Component component = target.GetComponent(componentName);
                    if (component == null)
                        return new SetPropertyResult { success = false, error = $"Component '{componentName}' not found" };

                    // 使用 SerializedObject 设置属性
                    var so = new SerializedObject(component);
                    var prop = so.FindProperty(propertyName);
                    
                    if (prop == null)
                    {
                        // 尝试查找字段或属性
                        var type = component.GetType();
                        var field = type.GetField(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        var property = type.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                        if (field != null)
                        {
                            object value = ParseJsonValue(valueStr, field.FieldType);
                            field.SetValue(component, value);
                        }
                        else if (property != null && property.CanWrite)
                        {
                            object value = ParseJsonValue(valueStr, property.PropertyType);
                            property.SetValue(component, value);
                        }
                        else
                        {
                            return new SetPropertyResult { success = false, error = $"Property '{propertyName}' not found" };
                        }
                    }
                    else
                    {
                        // 使用 SerializedProperty 设置
                        SetSerializedPropertyValue(prop, valueStr);
                        so.ApplyModifiedProperties();
                    }

                    return new SetPropertyResult
                    {
                        success = true,
                        objectPath = objectPath,
                        component = componentName,
                        property = propertyName
                    };
                }
            }
            catch (Exception ex)
            {
                return new SetPropertyResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("remove_object", "从预制体中删除物体")]
        [McpParameter("path", "Prefab文件路径", Required = true, Example = "Assets/UI/Prefabs/MainView.prefab")]
        [McpParameter("objectPath", "要删除的物体路径", Required = true, Example = "Panel/OldButton")]
        public object RemoveObject(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string objectPath = GetParam(parameters, "objectPath");

            if (string.IsNullOrEmpty(path))
                return new RemoveObjectResult { success = false, error = "Prefab path is required" };
            if (string.IsNullOrEmpty(objectPath))
                return new RemoveObjectResult { success = false, error = "Object path is required" };

            try
            {
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    GameObject prefabRoot = editScope.prefabContentsRoot;

                    Transform target = FindTransformInPrefab(prefabRoot, objectPath);
                    if (target == null)
                        return new RemoveObjectResult { success = false, error = $"Object '{objectPath}' not found" };

                    // 不能删除根物体
                    if (target == prefabRoot.transform)
                        return new RemoveObjectResult { success = false, error = "Cannot remove root object" };

                    GameObject.DestroyImmediate(target.gameObject);

                    return new RemoveObjectResult
                    {
                        success = true,
                        removedPath = objectPath
                    };
                }
            }
            catch (Exception ex)
            {
                return new RemoveObjectResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("save", "保存预制体修改")]
        [McpParameter("path", "Prefab文件路径", Required = true, Example = "Assets/UI/Prefabs/MainView.prefab")]
        public object Save(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");

            if (string.IsNullOrEmpty(path))
                return new SaveResult { success = false, error = "Prefab path is required" };

            try
            {
                // 使用 EditPrefabContentsScope 打开并立即保存
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    // 不需要修改，直接保存
                    return new SaveResult
                    {
                        success = true,
                        path = path
                    };
                }
            }
            catch (Exception ex)
            {
                return new SaveResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("get_property", "获取预制体内组件的属性值")]
        [McpParameter("path", "Prefab文件路径", Required = true, Example = "Assets/UI/Prefabs/MainView.prefab")]
        [McpParameter("objectPath", "物体路径", Required = true, Example = "Panel/Button")]
        [McpParameter("component", "组件类型名称", Required = true, Example = "RectTransform")]
        [McpParameter("property", "属性名称", Required = true, Example = "anchoredPosition")]
        public object GetProperty(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string objectPath = GetParam(parameters, "objectPath");
            string componentName = GetParam(parameters, "component");
            string propertyName = GetParam(parameters, "property");

            Debug.Log($"[MCP] GetProperty called with path={path}, objectPath={objectPath}, component={componentName}, property={propertyName}");

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(objectPath) ||
                string.IsNullOrEmpty(componentName) || string.IsNullOrEmpty(propertyName))
            {
                return new GetPropertyResult { success = false, error = "Missing required parameters" };
            }

            try
            {
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    GameObject prefabRoot = editScope.prefabContentsRoot;

                    Transform target = FindTransformInPrefab(prefabRoot, objectPath);
                    if (target == null)
                        return new GetPropertyResult { success = false, error = $"Object '{objectPath}' not found" };

                    Component component = target.GetComponent(componentName);
                    if (component == null)
                        return new GetPropertyResult { success = false, error = $"Component '{componentName}' not found" };

                    // 使用 SerializedObject 获取属性
                    var so = new SerializedObject(component);
                    var prop = so.FindProperty(propertyName);

                    object value = null;
                    string valueType = null;

                    if (prop == null)
                    {
                        // 尝试通过反射获取字段或属性
                        var type = component.GetType();
                        var field = type.GetField(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        var property = type.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                        if (field != null)
                        {
                            value = field.GetValue(component);
                            valueType = field.FieldType.Name;
                        }
                        else if (property != null && property.CanRead)
                        {
                            value = property.GetValue(component);
                            valueType = property.PropertyType.Name;
                        }
                        else
                        {
                            return new GetPropertyResult { success = false, error = $"Property '{propertyName}' not found" };
                        }
                    }
                    else
                    {
                        // 使用 SerializedProperty 获取值
                        value = GetSerializedPropertyValue(prop);
                        valueType = prop.propertyType.ToString();
                    }

                    // 将值转换为字符串
                    string valueStr = ConvertValueToString(value);

                    return new GetPropertyResult
                    {
                        success = true,
                        objectPath = objectPath,
                        component = componentName,
                        property = propertyName,
                        value = valueStr,
                        valueType = valueType
                    };
                }
            }
            catch (Exception ex)
            {
                return new GetPropertyResult { success = false, error = ex.Message };
            }
        }

        #region Helper Methods

        private string GetParam(Dictionary<string, string> parameters, string key, string defaultValue = "")
        {
            return parameters.TryGetValue(key, out var value) ? value : defaultValue;
        }

        private Transform FindTransformInPrefab(GameObject prefab, string path)
        {
            if (string.IsNullOrEmpty(path))
                return prefab.transform;

            // 如果路径就是根物体名称，直接返回根物体
            if (path == prefab.name)
                return prefab.transform;

            var parts = path.Split('/');
            
            // 如果路径以根物体名称开头，跳过第一部分
            int startIndex = 0;
            if (parts.Length > 0 && parts[0] == prefab.name)
                startIndex = 1;
            
            Transform current = prefab.transform;

            for (int i = startIndex; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrEmpty(part)) continue;
                
                Transform child = current.Find(part);
                if (child == null)
                    return null;
                current = child;
            }

            return current;
        }

        private Type GetComponentType(string typeName)
        {
            // 常见类型快速匹配
            if (typeName == "RectTransform")
                return typeof(RectTransform);
            if (typeName == "Transform")
                return typeof(Transform);

            // 尝试从 UnityEngine.UI 查找
            Type type = Type.GetType($"UnityEngine.UI.{typeName}, UnityEngine.UI");
            if (type != null) return type;

            // 尝试从 UnityEngine 查找
            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine.CoreModule");
            if (type != null) return type;

            // 遍历所有程序集
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null) return type;

                type = assembly.GetType($"UnityEngine.{typeName}");
                if (type != null) return type;

                type = assembly.GetType($"UnityEngine.UI.{typeName}");
                if (type != null) return type;
            }

            return null;
        }

        private object ParseJsonValue(string valueStr, Type targetType)
        {
            // 处理简单字符串格式
            if (targetType == typeof(string))
                return valueStr;
            if (targetType == typeof(int))
                return int.Parse(valueStr);
            if (targetType == typeof(float))
                return float.Parse(valueStr);
            if (targetType == typeof(bool))
                return bool.Parse(valueStr.ToLower());
            
            // Vector2: "x,y" 格式
            if (targetType == typeof(Vector2))
            {
                var parts = valueStr.Split(',');
                if (parts.Length >= 2)
                {
                    return new Vector2(
                        float.Parse(parts[0].Trim()),
                        float.Parse(parts[1].Trim())
                    );
                }
                // 回退到 JSON 格式
                var vec2 = JsonUtility.FromJson<Vector2Json>(valueStr);
                return new Vector2(vec2.x, vec2.y);
            }
            
            // Vector3: "x,y,z" 格式
            if (targetType == typeof(Vector3))
            {
                var parts = valueStr.Split(',');
                if (parts.Length >= 3)
                {
                    return new Vector3(
                        float.Parse(parts[0].Trim()),
                        float.Parse(parts[1].Trim()),
                        float.Parse(parts[2].Trim())
                    );
                }
                // 回退到 JSON 格式
                var vec3 = JsonUtility.FromJson<Vector3Json>(valueStr);
                return new Vector3(vec3.x, vec3.y, vec3.z);
            }
            
            // Color: "r,g,b" 或 "r,g,b,a" 格式
            if (targetType == typeof(Color))
            {
                var parts = valueStr.Split(',');
                if (parts.Length >= 3)
                {
                    return new Color(
                        float.Parse(parts[0].Trim()),
                        float.Parse(parts[1].Trim()),
                        float.Parse(parts[2].Trim()),
                        parts.Length >= 4 ? float.Parse(parts[3].Trim()) : 1f
                    );
                }
                // 回退到 JSON 格式
                var col = JsonUtility.FromJson<ColorJson>(valueStr);
                return new Color(col.r, col.g, col.b, col.a);
            }

            // 默认使用 JsonUtility
            return JsonUtility.FromJson(valueStr, targetType);
        }

        private void SetSerializedPropertyValue(SerializedProperty prop, string valueStr)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = int.Parse(valueStr);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = bool.Parse(valueStr.ToLower());
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = float.Parse(valueStr);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = valueStr;
                    break;
                case SerializedPropertyType.Color:
                    var parts = valueStr.Split(',');
                    if (parts.Length >= 3)
                    {
                        prop.colorValue = new Color(
                            float.Parse(parts[0].Trim()),
                            float.Parse(parts[1].Trim()),
                            float.Parse(parts[2].Trim()),
                            parts.Length >= 4 ? float.Parse(parts[3].Trim()) : 1f
                        );
                    }
                    else
                    {
                        var col = JsonUtility.FromJson<ColorJson>(valueStr);
                        prop.colorValue = new Color(col.r, col.g, col.b, col.a);
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    var vec2Parts = valueStr.Split(',');
                    if (vec2Parts.Length >= 2)
                    {
                        prop.vector2Value = new Vector2(
                            float.Parse(vec2Parts[0].Trim()),
                            float.Parse(vec2Parts[1].Trim())
                        );
                    }
                    else
                    {
                        var vec2 = JsonUtility.FromJson<Vector2Json>(valueStr);
                        prop.vector2Value = new Vector2(vec2.x, vec2.y);
                    }
                    break;
                case SerializedPropertyType.Vector3:
                    var vec3Parts = valueStr.Split(',');
                    if (vec3Parts.Length >= 3)
                    {
                        prop.vector3Value = new Vector3(
                            float.Parse(vec3Parts[0].Trim()),
                            float.Parse(vec3Parts[1].Trim()),
                            float.Parse(vec3Parts[2].Trim())
                        );
                    }
                    else
                    {
                        var vec3 = JsonUtility.FromJson<Vector3Json>(valueStr);
                        prop.vector3Value = new Vector3(vec3.x, vec3.y, vec3.z);
                    }
                    break;
                case SerializedPropertyType.ObjectReference:
                    // 对象引用需要特殊处理，这里简化处理
                    break;
            }
        }

        private object GetSerializedPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.Float:
                    return prop.floatValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Color:
                    return prop.colorValue;
                case SerializedPropertyType.Vector2:
                    return prop.vector2Value;
                case SerializedPropertyType.Vector3:
                    return prop.vector3Value;
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : null;
                case SerializedPropertyType.Enum:
                    return prop.enumValueIndex;
                default:
                    return prop.boxedValue;
            }
        }

        private string ConvertValueToString(object value)
        {
            if (value == null)
                return "null";

            switch (value)
            {
                case string s:
                    return s;
                case int i:
                    return i.ToString();
                case float f:
                    return f.ToString();
                case bool b:
                    return b.ToString().ToLower();
                case Vector2 v2:
                    return $"{v2.x},{v2.y}";
                case Vector3 v3:
                    return $"{v3.x},{v3.y},{v3.z}";
                case Color c:
                    return $"{c.r},{c.g},{c.b},{c.a}";
                default:
                    return value.ToString();
            }
        }

        [Serializable]
        private class Vector2Json { public float x, y; }

        [Serializable]
        private class Vector3Json { public float x, y, z; }

        [Serializable]
        private class ColorJson { public float r, g, b, a = 1f; }

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

        [Serializable]
        public class AddChildResult
        {
            public bool success;
            public string name;
            public string fullPath;
            public string[] addedComponents;
            public string error;
        }

        [Serializable]
        public class AddComponentResult
        {
            public bool success;
            public string objectPath;
            public string componentType;
            public string error;
        }

        [Serializable]
        public class SetPropertyResult
        {
            public bool success;
            public string objectPath;
            public string component;
            public string property;
            public string error;
        }

        [Serializable]
        public class RemoveObjectResult
        {
            public bool success;
            public string removedPath;
            public string error;
        }

        [Serializable]
        public class SaveResult
        {
            public bool success;
            public string path;
            public string error;
        }

        [Serializable]
        public class GetPropertyResult
        {
            public bool success;
            public string objectPath;
            public string component;
            public string property;
            public string value;
            public string valueType;
            public string error;
        }

        #endregion
    }
}
