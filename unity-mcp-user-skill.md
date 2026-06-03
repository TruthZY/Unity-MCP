---
name: unity-mcp-user
description: 通过 Unity MCP 服务控制 Unity 编辑器。当需要操作场景、预制体、资源、日志、执行脚本时使用此 Skill。
---

# Unity MCP 使用指南

## 通信方式

```
AI 客户端 <-- MCP 协议 --> Node.js 服务端 <-- HTTP --> Unity 编辑器
或（Qoder 直连）
Qoder <-- curl --> HTTP (端口 8090) <-- HTTP --> Unity 编辑器
```

### 直连调用

```bash
curl -X POST http://localhost:8090/McpUnity/ \
  -H "Content-Type: application/json" \
  -d '{"command":"模块_命令","parameters":{"参数名":"值"}}'
```

## 命令一览

初次运行时
使用ping 确保工具连接
使用 help 获取到全部操作

## 错误处理

所有命令返回 JSON：

```json
// 成功
{"success": true, "data": ...}

// 失败
{"success": false, "error": "错误信息"}
```

常见错误：
- `Object not found` — 检查 path 路径拼写（支持隐藏对象）
- `Component 'X' not found` — 检查组件名（用 Unity 类型名，如 `UnityEngine.UI.Text`）
- `Parameter 'code' is required` — 缺少必需参数

## 注意事项

1. **Unity 服务端必须先启动**: 打开 `Window -> MCP Unity -> Server`，点击 Start
2. **path 格式**: 用 `/` 分隔层级，如 `Canvas/Panel/Button`
3. **属性值格式**: Vector3 用逗号分隔 `"100,-50,0"`，Color 用 `"1,0,0,1"`
4. **资源路径**: 都以 `Assets/` 开头

