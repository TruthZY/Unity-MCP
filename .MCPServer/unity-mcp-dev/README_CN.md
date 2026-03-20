# Unity MCP 开发指南

## 项目概述

Unity MCP (Model Context Protocol) 是一个让 AI 能够控制 Unity 编辑器的解决方案。

```
AI 客户端 (Claude/Qoder) <-- MCP 协议 --> Node.js 服务端 <-- HTTP --> Unity 编辑器
```

## 快速开始

### 1. 项目结构

```
MCPEditor/
├── Core/                       # 核心框架
│   ├── IMcpModule.cs          # 模块接口
│   ├── CommandRouter.cs       # 命令路由（自动注册）
│   └── ResponseHelper.cs      # 响应辅助
│
├── Modules/                    # 功能模块
│   ├── SceneModule.cs         # 场景操作
│   ├── AssetModule.cs         # 资源管理
│   ├── LogModule.cs           # 日志管理
│   └── PrefabModule.cs        # 预制体分析
│
├── LogCollector.cs            # 日志收集器
├── McpUnityServer.cs          # HTTP 服务器窗口
│
└── .MCPServer/                # Node.js 服务端
    ├── McpServer/             # 服务端代码
    └── unity-mcp-dev/         # 本开发指南
```

### 2. 添加新命令（3步）

#### 第1步：在 Unity 中添加命令

选择或创建模块文件，例如 `Modules/SceneModule.cs`：

```csharp
[McpModule("scene")]  // 模块名称
public class SceneModule : IMcpModule
{
    public string ModuleName => "scene";

    // 添加新命令
    [McpCommand("my_command")]  // 命令名称
    public object MyCommand(Dictionary<string, string> parameters)
    {
        // 1. 获取参数
        string name = GetParam(parameters, "name");
        int count = int.Parse(GetParam(parameters, "count", "0"));
        
        // 2. 执行操作
        var result = DoSomething(name, count);
        
        // 3. 返回结果（必须是可序列化的对象）
        return new MyResult 
        { 
            success = true, 
            data = result 
        };
    }
    
    // 辅助方法：获取参数
    private string GetParam(Dictionary<string, string> p, string key, string defaultValue = "")
    {
        return p.TryGetValue(key, out var value) ? value : defaultValue;
    }
    
    // 结果类（必须标记 [Serializable]）
    [Serializable]
    public class MyResult
    {
        public bool success;
        public string data;
        public string error;
    }
}
```

#### 第2步：在 Node.js 中添加工具定义

编辑 `.MCPServer/McpServer/src/index.ts`：

```typescript
// 1. 在 TOOLS 数组中添加工具定义
{
  name: 'unity_scene_my_command',  // 工具名称（前缀 + 模块 + 命令）
  description: '命令的功能描述',
  inputSchema: {
    type: 'object',
    properties: {
      name: {
        type: 'string',
        description: '参数说明',
      },
      count: {
        type: 'number',
        description: '参数说明',
      },
    },
    required: ['name'],  // 必需参数
  },
},

// 2. 在 switch 语句中添加调用处理
case 'unity_scene_my_command':
  result = await unityClient.sendCommand('my_command', {
    name: args?.name,
    count: args?.count?.toString(),  // 数字转字符串
  });
  break;
```

#### 第3步：编译并测试

```bash
# 编译 Node.js 服务端
cd .MCPServer/McpServer
npm run build
```

在 Unity 中：
1. 等待代码编译完成
2. 打开 `Window -> MCP Unity -> Server`
3. 点击 "Start Server"

测试命令：
```bash
curl -X POST http://localhost:8090/McpUnity/ \
  -H "Content-Type: application/json" \
  -d '{"command":"scene_my_command","parameters":{"name":"test","count":5}}'
```

## 命名规范

### 命令命名
- 格式：`{模块}_{命令}`
- 示例：`scene_create_object`, `asset_load_asset`, `log_get_logs`
- 使用小写字母和下划线

### 模块命名
- 使用名词，单数形式
- 示例：`scene`, `asset`, `log`, `prefab`

### 参数命名
- 使用 camelCase
- 示例：`parentPath`, `folderName`, `searchPattern`

## 响应格式

### 成功响应
```csharp
return new Result 
{ 
    success = true, 
    data = 你的数据 
};
```

### 错误响应
```csharp
return new Result 
{ 
    success = false, 
    error = "错误信息" 
};
```

## Unity 编辑器安全

修改场景或对象时，必须遵循以下规范：

```csharp
// 1. 记录撤销操作
Undo.RecordObject(targetObject, "操作描述");

// 2. 执行修改
targetObject.property = newValue;

// 3. 标记场景已修改
EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

// 4. 标记对象已修改（可选）
EditorUtility.SetDirty(targetObject);
```

## 现有模块参考

### SceneModule (scene_*)
场景操作命令：
- `scene_ping` - 健康检查
- `scene_get_hierarchy` - 获取场景层级
- `scene_create_object` - 创建游戏对象
- `scene_delete_object` - 删除游戏对象
- `scene_select_object` - 选择对象
- `scene_set_property` / `scene_get_property` - 设置/获取属性
- `scene_get_components` / `scene_add_component` - 组件管理
- `scene_execute_menu` - 执行菜单项

### AssetModule (asset_*)
资源管理命令：
- `asset_get_assets` - 获取资源列表
- `asset_load_asset` - 加载资源信息
- `asset_create_folder` - 创建文件夹
- `asset_delete_asset` - 删除资源

### LogModule (log_*)
日志管理命令：
- `log_get_logs` - 获取日志（支持过滤：error/warning/log）
- `log_clear_logs` - 清空日志
- `log_get_log_count` - 获取日志数量

### PrefabModule (prefab_*)
预制体分析命令：
- `prefab_get_hierarchy` - 获取完整层级
- `prefab_get_components` - 获取组件信息
- `prefab_find_objects` - 按名称/组件查找对象

## 调试技巧

### 1. 查看已注册命令
在 Unity 控制台中搜索 `[MCP]` 日志，可以看到所有已注册的命令。

### 2. 测试 HTTP 接口
```bash
# 测试连接
curl http://localhost:8090/McpUnity/ \
  -X POST \
  -H "Content-Type: application/json" \
  -d '{"command":"scene_ping"}'
```

### 3. 查看详细日志
在 `CommandRouter.cs` 中取消注释日志输出，可以看到命令执行详情。

## 常见问题

### Q: 命令找不到？
A: 检查以下几点：
1. `[McpModule]` 和 `[McpCommand]` 特性是否正确添加
2. 类是否实现了 `IMcpModule` 接口
3. Node.js 中的命令名称是否匹配
4. Node.js 服务端是否已重新编译

### Q: 返回数据为空？
A: 检查以下几点：
1. 结果类是否标记了 `[Serializable]`
2. 是否有 null 值导致序列化失败
3. 使用 `JsonUtility.ToJson()` 测试序列化

### Q: Unity 没有响应？
A: 检查以下几点：
1. MCP Server 窗口是否已启动
2. 端口 8090 是否被占用
3. Unity 控制台是否有错误
4. 防火墙是否阻止了连接

## 最佳实践

1. **模块化设计**：相关功能放在同一个模块中
2. **参数验证**：始终验证必需参数，返回清晰的错误信息
3. **错误处理**：使用 try-catch 包裹可能出错的操作
4. **撤销支持**：所有修改操作都要支持撤销
5. **文档注释**：为每个命令添加 XML 文档注释

## 示例：完整的命令实现

```csharp
[McpModule("example")]
public class ExampleModule : IMcpModule
{
    public string ModuleName => "example";

    /// <summary>
    /// 创建多个立方体
    /// </summary>
    /// <param name="parameters">name: 名称前缀, count: 数量</param>
    [McpCommand("create_cubes")]
    public object CreateCubes(Dictionary<string, string> parameters)
    {
        // 获取参数
        string namePrefix = GetParam(parameters, "name", "Cube");
        int count = int.Parse(GetParam(parameters, "count", "1"));
        
        // 验证参数
        if (count <= 0 || count > 100)
        {
            return new CreateCubesResult 
            { 
                success = false, 
                error = "Count must be between 1 and 100" 
            };
        }
        
        // 执行操作
        var createdObjects = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"{namePrefix}_{i}";
            cube.transform.position = new Vector3(i * 2, 0, 0);
            
            Undo.RegisterCreatedObjectUndo(cube, "Create Cubes via MCP");
            createdObjects.Add(cube.name);
        }
        
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        
        // 返回结果
        return new CreateCubesResult
        {
            success = true,
            createdCount = createdObjects.Count,
            objectNames = createdObjects.ToArray()
        };
    }
    
    private string GetParam(Dictionary<string, string> p, string key, string defaultValue = "")
    {
        return p.TryGetValue(key, out var value) ? value : defaultValue;
    }
    
    [Serializable]
    public class CreateCubesResult
    {
        public bool success;
        public int createdCount;
        public string[] objectNames;
        public string error;
    }
}
```

对应的 Node.js 工具定义：

```typescript
{
  name: 'unity_example_create_cubes',
  description: 'Create multiple cube primitives in the scene',
  inputSchema: {
    type: 'object',
    properties: {
      name: {
        type: 'string',
        description: 'Name prefix for created cubes',
      },
      count: {
        type: 'number',
        description: 'Number of cubes to create (1-100)',
      },
    },
    required: ['count'],
  },
},
```

调用处理：
```typescript
case 'unity_example_create_cubes':
  result = await unityClient.sendCommand('create_cubes', {
    name: args?.name,
    count: args?.count?.toString(),
  });
  break;
```
