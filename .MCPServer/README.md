# Unity MCP Server

通过 Model Context Protocol (MCP) 控制 Unity 编辑器的完整解决方案。

## 架构

```
AI 客户端 (Claude/Cursor) ←→ MCP Server (Node.js) ←→ Unity Editor (C#)
                                    ↑                      ↑
                              (MCP Protocol)          (HTTP API)
```

## 安装步骤

### 1. Unity 包安装

将 `UnityPackage/Editor` 文件夹复制到你的 Unity 项目中：

```
YourProject/
├── Assets/
│   └── Editor/
│       ├── McpUnityServer.cs
│       ├── CommandProcessor.cs
│       └── McpUnity.asmdef
```

### 2. MCP 服务端安装

```bash
cd McpServer
npm install
npm run build
```

### 3. 配置 AI 客户端

在 Claude Desktop 或 Cursor 的 MCP 配置中添加：

**Claude Desktop** (`%APPDATA%/Claude/claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "unity": {
      "command": "node",
      "args": ["C:/path/to/UnityMCP/McpServer/dist/index.js"]
    }
  }
}
```

**Cursor** (Settings → MCP):
```json
{
  "mcpServers": {
    "unity": {
      "command": "node",
      "args": ["C:/path/to/UnityMCP/McpServer/dist/index.js"]
    }
  }
}
```

## 使用

### 1. 启动 Unity 服务器

在 Unity 中打开 `Window → MCP Unity → Server`，点击 **Start Server**。

### 2. 使用 MCP 工具

现在可以通过 AI 客户端使用以下工具：

| 工具 | 描述 |
|------|------|
| `unity_ping` | 检查 Unity 连接状态 |
| `unity_get_hierarchy` | 获取场景层级 |
| `unity_select_object` | 选择游戏对象 |
| `unity_create_object` | 创建游戏对象 |
| `unity_delete_object` | 删除游戏对象 |
| `unity_set_property` | 设置组件属性 |
| `unity_get_property` | 获取组件属性 |
| `unity_get_components` | 获取对象组件列表 |
| `unity_add_component` | 添加组件 |
| `unity_execute_menu` | 执行菜单命令 |

### 示例对话

**用户**: "在 Unity 中创建一个红色的立方体"

**AI**: 我会帮你创建一个红色的立方体。

```
1. 创建立方体: unity_create_object(name: "Red Cube", primitiveType: "Cube")
2. 设置材质颜色: unity_set_property(path: "Red Cube", component: "MeshRenderer", property: "material.color", value: {r: 1, g: 0, b: 0, a: 1})
```

## 开发

### 添加新命令

1. 在 `CommandProcessor.cs` 的 `Commands` 字典中添加处理函数
2. 在 `index.ts` 的 `TOOLS` 数组中添加工具定义
3. 在 `CallToolRequestSchema` 处理器中添加调用逻辑

## 文件结构

```
UnityMCP/
├── UnityPackage/
│   └── Editor/
│       ├── McpUnityServer.cs      # HTTP 服务器
│       ├── CommandProcessor.cs    # 命令处理
│       └── McpUnity.asmdef        # 程序集定义
├── McpServer/
│   ├── src/
│   │   └── index.ts               # MCP 服务端
│   ├── package.json
│   ├── tsconfig.json
│   └── mcp-config.json            # 配置示例
└── README.md
```

## License

MIT
