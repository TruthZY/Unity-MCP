using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using McpUnity.Core;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Modules
{
    /// <summary>
    /// 资源管理模块
    /// </summary>
    [McpModule("asset")]
    public class AssetModule : IMcpModule
    {
        public string ModuleName => "asset";

        [McpCommand("get_assets", "获取指定路径下的资源列表")]
        [McpParameter("path", "资源路径，相对于Assets文件夹", Required = false, DefaultValue = "Assets", Example = "Assets/UI/Prefabs")]
        [McpParameter("searchPattern", "搜索模式，支持通配符", Required = false, DefaultValue = "*", Example = "*.prefab")]
        [McpParameter("recursive", "是否递归搜索子文件夹", Required = false, DefaultValue = "false", Example = "true")]
        public object GetAssets(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path", "Assets");
            string searchPattern = GetParam(parameters, "searchPattern", "*");
            bool recursive = GetParam(parameters, "recursive") == "true";

            // 安全检查
            if (!path.StartsWith("Assets"))
            {
                path = "Assets/" + path.TrimStart('/', '\\');
            }

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), path);
            if (!Directory.Exists(fullPath))
            {
                return new AssetsResult { success = false, error = $"Directory not found: {path}" };
            }

            try
            {
                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var directories = Directory.GetDirectories(fullPath, searchPattern, searchOption)
                    .Select(d => new AssetInfo
                    {
                        name = Path.GetFileName(d),
                        path = d.Replace(Directory.GetCurrentDirectory() + "\\", "").Replace("\\", "/"),
                        type = "folder",
                        fullPath = d
                    });

                var files = Directory.GetFiles(fullPath, searchPattern, searchOption)
                    .Where(f => !f.EndsWith(".meta"))
                    .Select(f => new AssetInfo
                    {
                        name = Path.GetFileName(f),
                        path = f.Replace(Directory.GetCurrentDirectory() + "\\", "").Replace("\\", "/"),
                        type = "file",
                        extension = Path.GetExtension(f),
                        fullPath = f
                    });

                var allAssets = directories.Concat(files).ToArray();

                return new AssetsResult
                {
                    success = true,
                    path = path,
                    assets = allAssets,
                    totalCount = allAssets.Length
                };
            }
            catch (Exception ex)
            {
                return new AssetsResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("load_asset", "加载指定路径的资源")]
        [McpParameter("path", "资源完整路径，必须以Assets开头", Required = true, Example = "Assets/UI/Prefabs/MyButton.prefab")]
        [McpParameter("type", "资源类型全名", Required = false, DefaultValue = "UnityEngine.Object", Example = "UnityEngine.GameObject")]
        public object LoadAsset(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");
            string typeName = GetParam(parameters, "type", "UnityEngine.Object");

            if (string.IsNullOrEmpty(path))
            {
                return new LoadAssetResult { success = false, error = "Path is required" };
            }

            // 安全检查
            if (!path.StartsWith("Assets"))
            {
                return new LoadAssetResult { success = false, error = "Path must be in Assets folder" };
            }

            try
            {
                var type = System.Type.GetType(typeName) ?? typeof(UnityEngine.Object);
                var asset = AssetDatabase.LoadAssetAtPath(path, type);

                if (asset == null)
                {
                    return new LoadAssetResult { success = false, error = $"Asset not found: {path}" };
                }

                var assetInfo = new AssetDetail
                {
                    name = asset.name,
                    path = path,
                    type = asset.GetType().Name,
                    instanceId = asset.GetInstanceID()
                };

                if (asset is GameObject go)
                {
                    assetInfo.components = go.GetComponents<Component>().Select(c => c.GetType().Name).ToArray();
                }

                return new LoadAssetResult
                {
                    success = true,
                    asset = assetInfo
                };
            }
            catch (Exception ex)
            {
                return new LoadAssetResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("create_folder", "在Assets下创建新文件夹")]
        [McpParameter("parentPath", "父文件夹路径", Required = false, DefaultValue = "Assets", Example = "Assets/UI")]
        [McpParameter("folderName", "新文件夹名称", Required = true, Example = "NewFolder")]
        public object CreateFolder(Dictionary<string, string> parameters)
        {
            string parentPath = GetParam(parameters, "parentPath", "Assets");
            string folderName = GetParam(parameters, "folderName");

            if (string.IsNullOrEmpty(folderName))
            {
                return new CreateFolderResult { success = false, error = "Folder name is required" };
            }

            // 安全检查
            if (!parentPath.StartsWith("Assets"))
            {
                parentPath = "Assets/" + parentPath.TrimStart('/', '\\');
            }

            string newFolderPath = $"{parentPath}/{folderName}";

            try
            {
                string guid = AssetDatabase.CreateFolder(parentPath, folderName);
                if (string.IsNullOrEmpty(guid))
                {
                    return new CreateFolderResult { success = false, error = "Failed to create folder (it may already exist)" };
                }

                return new CreateFolderResult
                {
                    success = true,
                    path = newFolderPath,
                    guid = guid
                };
            }
            catch (Exception ex)
            {
                return new CreateFolderResult { success = false, error = ex.Message };
            }
        }

        [McpCommand("delete_asset", "删除指定路径的资源")]
        [McpParameter("path", "要删除的资源路径", Required = true, Example = "Assets/UI/OldPrefab.prefab")]
        public object DeleteAsset(Dictionary<string, string> parameters)
        {
            string path = GetParam(parameters, "path");

            if (string.IsNullOrEmpty(path))
            {
                return new DeleteAssetResult { success = false, error = "Path is required" };
            }

            // 安全检查
            if (!path.StartsWith("Assets"))
            {
                return new DeleteAssetResult { success = false, error = "Path must be in Assets folder" };
            }

            // 检查资源是否存在
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                return new DeleteAssetResult { success = false, error = $"Asset not found: {path}" };
            }

            string assetName = asset.name;

            try
            {
                bool deleted = AssetDatabase.DeleteAsset(path);
                if (deleted)
                {
                    return new DeleteAssetResult
                    {
                        success = true,
                        path = path,
                        name = assetName,
                        message = $"Successfully deleted: {path}"
                    };
                }
                else
                {
                    return new DeleteAssetResult { success = false, error = "Failed to delete asset" };
                }
            }
            catch (Exception ex)
            {
                return new DeleteAssetResult { success = false, error = ex.Message };
            }
        }

        #region Helper Methods

        private string GetParam(Dictionary<string, string> parameters, string key, string defaultValue = "")
        {
            return parameters.TryGetValue(key, out var value) ? value : defaultValue;
        }

        #endregion

        #region Result Classes

        [Serializable]
        public class AssetsResult
        {
            public bool success;
            public string path;
            public AssetInfo[] assets;
            public int totalCount;
            public string error;
        }

        [Serializable]
        public class AssetInfo
        {
            public string name;
            public string path;
            public string type;
            public string extension;
            public string fullPath;
        }

        [Serializable]
        public class LoadAssetResult
        {
            public bool success;
            public AssetDetail asset;
            public string error;
        }

        [Serializable]
        public class AssetDetail
        {
            public string name;
            public string path;
            public string type;
            public int instanceId;
            public string[] components;
        }

        [Serializable]
        public class CreateFolderResult
        {
            public bool success;
            public string path;
            public string guid;
            public string error;
        }

        [Serializable]
        public class DeleteAssetResult
        {
            public bool success;
            public string path;
            public string name;
            public string message;
            public string error;
        }

        #endregion
    }
}
