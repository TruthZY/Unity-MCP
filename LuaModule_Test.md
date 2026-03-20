# LuaModule 功能测试指南

## 测试准备

1. 确保Unity MCP Server正在运行（菜单: Window > MCP Unity Server）
2. 确保Node.js MCP Server已构建（在.MCPServer/McpServer目录运行 `npm run build`）

## 测试命令

### 1. 加载Prefab到场景

首先需要在Unity中实例化这个prefab才能测试：

```bash
# 加载prefab
curl -X POST http://localhost:8090/McpUnity/ \
  -H "Content-Type: application/json" \
  -d '{"command":"load_asset","parameters":{"path":"Assets/Game/ResourcesAB/UI2/G/Act99/Prefab/Act99_AFK_MyTips.prefab"}}'
```

### 2. 获取Prefab根节点的Lua参数

prefab的根节点GameObject名称是 `Act99_AFK_MyTips`：

```bash
curl -X POST http://localhost:8090/McpUnity/ \
  -H "Content-Type: application/json" \
  -d '{"command":"get_lua_params","parameters":{"name":"Act99_AFK_MyTips"}}'
```

**预期结果**:
```json
{
  "success": true,
  "gameObjectName": "Act99_AFK_MyTips",
  "luaScriptPath": "Module.Activity99.View.Act99_AFK_MyTips",
  "objectParams": [
    {"key": "CloseBtn", "type": "Transform", "value": "CloseBtn"},
    {"key": "PowerText", "type": "Transform", "value": "PowerText"},
    {"key": "HpBar", "type": "Transform", "value": "HpBar"},
    ...
  ],
  "valueParams": []
}
```

### 3. 查找场景中所有LuaBehaviour

```bash
curl -X POST http://localhost:8090/McpUnity/ \
  -H "Content-Type: application/json" \
  -d '{"command":"find_lua_behaviours","parameters":{}}'
```

### 4. 按Lua脚本路径过滤查找

```bash
curl -X POST http://localhost:8090/McpUnity/ \
  -H "Content-Type: application/json" \
  -d '{"command":"find_lua_behaviours","parameters":{"luaScript":"Act99_AFK"}}'
```

### 5. 修改Lua对象参数

例如，修改CloseBtn的引用（假设场景中有一个新的按钮）：

```bash
curl -X POST http://localhost:8090/McpUnity/ \
  -H "Content-Type: application/json" \
  -d '{"command":"set_lua_param","parameters":{"name":"Act99_AFK_MyTips","key":"CloseBtn","type":"object","value":"NewCloseButton"}}'
```

### 6. 添加新的值参数

```bash
curl -X POST http://localhost:8090/McpUnity/ \
  -H "Content-Type: application/json" \
  -d '{"command":"set_lua_param","parameters":{"name":"Act99_AFK_MyTips","key":"TestParam","type":"value","value":"{\"obj\":123}"}}'
```

## 已知问题

1. **Prefab模式限制**: LuaModule目前只能操作场景中的GameObject，不能直接操作Prefab资源。需要先实例化prefab到场景中。

2. **对象引用设置**: 设置object类型参数时，目标对象需要在场景中可找到（通过GameObject.Find）。

## 建议的改进

1. 添加 `get_prefab_lua_params` 命令，直接读取prefab资源中的LuaBehaviour配置
2. 添加 `set_prefab_lua_param` 命令，修改prefab资源中的Lua参数
3. 支持通过fileID引用对象（用于prefab内部引用）
