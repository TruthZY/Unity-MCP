---
name: unity-mcp-user
description: Use Unity MCP (Model Context Protocol) service to control Unity Editor via AI. Use when you need to operate Unity scenes, prefabs, assets, logs, or LuaBehaviour bindings through natural language commands.
---

# Unity MCP User Guide

## Overview

This skill allows AI to control Unity Editor through the MCP (Model Context Protocol) HTTP service.

```
AI Client <-- MCP Protocol --> Node.js Server <-- HTTP (Port 8090) --> Unity Editor
```

## Prerequisites

1. Unity project with MCPEditor installed
2. MCP Server running (Node.js)
3. Unity Editor with MCP window open

## Available Commands

### Scene Operations (`scene_*`)

| Command | Description | Example Parameters |
|---------|-------------|-------------------|
| `scene_ping` | Health check | - |
| `scene_get_hierarchy` | Get scene tree | - |
| `scene_create_object` | Create GameObject | `name`, `parent` (optional) |
| `scene_delete_object` | Delete GameObject | `name` or `path` |
| `scene_select_object` | Select object | `name` or `path` |
| `scene_get_property` | Get component property | `path`, `component`, `property` |
| `scene_set_property` | Set component property | `path`, `component`, `property`, `value` |
| `scene_get_components` | List components | `path` |
| `scene_add_component` | Add component | `path`, `type` |
| `scene_execute_menu` | Execute menu item | `path` (e.g., "GameObject/3D Object/Cube") |

### Asset Operations (`asset_*`)

| Command | Description | Example Parameters |
|---------|-------------|-------------------|
| `asset_get_assets` | List assets | `path`, `searchPattern` (optional) |
| `asset_load_asset` | Load asset info | `path` |
| `asset_create_folder` | Create folder | `folderName`, `parentPath` (optional) |
| `asset_delete_asset` | Delete asset | `path` |

### Prefab Operations (`prefab_*`)

| Command | Description | Example Parameters |
|---------|-------------|-------------------|
| `prefab_get_hierarchy` | Get prefab structure | `prefabPath` |
| `prefab_get_components` | Get object components | `prefabPath`, `objectPath` |
| `prefab_find_objects` | Find by name/component | `prefabPath`, `name` or `componentType` |

### Log Operations (`log_*`)

| Command | Description | Example Parameters |
|---------|-------------|-------------------|
| `log_get_logs` | Get console logs | `filter` (error/warning/log), `limit` |
| `log_clear_logs` | Clear logs | - |
| `log_get_log_count` | Get log count | - |

### Lua Operations (`lua_*`)

| Command | Description | Example Parameters |
|---------|-------------|-------------------|
| `lua_get_prefab_lua_params` | Read LuaBehaviour params | `prefabPath` |
| `lua_set_prefab_lua_param` | Set LuaBehaviour param | `prefabPath`, `gameObjectName`, `paramType` (object/value), `key`, `newValue` |
| `lua_create_script` | Create Lua script | `filePath`, `templateType` (NewView/NewLuaBehaviour) |
| `lua_attach_script` | Attach script to prefab | `prefabPath`, `scriptPath`, `gameObjectName` (optional) |

## Usage Examples

### Example 1: Get Scene Hierarchy

```json
{
  "command": "scene_get_hierarchy",
  "parameters": {}
}
```

### Example 2: Create a Cube

```json
{
  "command": "scene_create_object",
  "parameters": {
    "name": "MyCube",
    "parent": "ParentObject"
  }
}
```

### Example 3: Read Prefab Lua Parameters

```json
{
  "command": "lua_get_prefab_lua_params",
  "parameters": {
    "prefabPath": "Assets/Prefabs/MyPrefab.prefab"
  }
}
```

### Example 4: Bind Lua Parameter

```json
{
  "command": "lua_set_prefab_lua_param",
  "parameters": {
    "prefabPath": "Assets/Prefabs/MyPrefab.prefab",
    "gameObjectName": "MyPrefab",
    "paramType": "object",
    "key": "TitleText",
    "newValue": "TxtTitle"
  }
}
```

### Example 5: Create and Attach Lua Script

```json
// Step 1: Create script
{
  "command": "lua_create_script",
  "parameters": {
    "filePath": "Assets/Game/Lua/Module/MyModule/MyView.lua.txt",
    "templateType": "NewView"
  }
}

// Step 2: Attach to prefab
{
  "command": "lua_attach_script",
  "parameters": {
    "prefabPath": "Assets/Prefabs/MyPrefab.prefab",
    "scriptPath": "Assets/Game/Lua/Module/MyModule/MyView.lua.txt"
  }
}
```

## Common Workflows

### Workflow 1: Inspect Prefab Structure

1. `prefab_get_hierarchy` - Get full structure
2. `prefab_find_objects` - Find specific components
3. `lua_get_prefab_lua_params` - Check existing Lua bindings

### Workflow 2: Setup Lua UI

1. `lua_create_script` - Create Lua script with DefineList
2. `lua_attach_script` - Attach to prefab root
3. `lua_set_prefab_lua_param` - Bind each UI component

### Workflow 3: Debug Issues

1. `log_clear_logs` - Clear old logs
2. Execute operation
3. `log_get_logs` - Check for errors

## Error Handling

All commands return JSON with `success` field:

```json
// Success
{
  "success": true,
  "data": {...}
}

// Error
{
  "success": false,
  "error": "Error message"
}
```

Common errors:
- `Prefab not found at path` - Check prefabPath
- `GameObject 'X' not found` - Check object name
- `Target object 'X' not found` - Check component name
- `Lua script file not found` - Check scriptPath

## Tips

1. **Use McpCommandTester in Unity** for quick testing without AI
2. **Check logs** (`log_get_logs`) when something fails
3. **Verify paths** - All paths should start with `Assets/`
4. **GameObject names** - Use simple names or full paths (e.g., "Parent/Child")
5. **Component types** - Use Unity type names (e.g., "UnityEngine.UI.Text")

## MCP Server Endpoint

```
POST http://localhost:8090/McpUnity/
Content-Type: application/json

{
  "command": "module_command",
  "parameters": {...}
}
```
