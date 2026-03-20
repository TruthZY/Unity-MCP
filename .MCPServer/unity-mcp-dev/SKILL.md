---
name: unity-mcp-dev
description: Develop and maintain Unity MCP (Model Context Protocol) server for AI-powered Unity Editor automation. Use when adding new MCP commands, creating Unity Editor tools, or extending the MCP integration.
---

# Unity MCP Development

## Architecture Overview

```
AI Client (Claude/Qoder) <-- MCP Protocol --> Node.js Server <-- HTTP --> Unity Editor
```

### File Structure
```
MCPEditor/
├── Core/
│   ├── IMcpModule.cs         # Module interface & attributes
│   ├── CommandRouter.cs      # Auto-registration router
│   └── ResponseHelper.cs     # Response serialization
├── Modules/
│   ├── SceneModule.cs        # Scene operations
│   ├── AssetModule.cs        # Asset management
│   ├── LogModule.cs          # Log collection
│   └── PrefabModule.cs       # Prefab analysis
├── LogCollector.cs           # Log collector (singleton)
└── McpUnityServer.cs         # HTTP server window
```

## Quick Start: Adding a New Command

### 1. Choose or Create Module

**Existing module?** Add method to existing file in `Modules/`
**New category?** Create new module file

### 2. Implement Command

```csharp
// Modules/YourModule.cs
[McpModule("yourmodule")]
public class YourModule : IMcpModule
{
    public string ModuleName => "yourmodule";

    [McpCommand("your_command")]
    public object YourCommand(Dictionary<string, string> parameters)
    {
        // Parse parameters
        string param1 = GetParam(parameters, "param1");
        int param2 = int.Parse(GetParam(parameters, "param2", "0"));
        
        // Implement logic
        var result = DoSomething(param1, param2);
        
        // Return serializable object
        return new YourResult 
        { 
            success = true, 
            data = result 
        };
    }
    
    private string GetParam(Dictionary<string, string> p, string key, string defaultValue = "")
    {
        return p.TryGetValue(key, out var value) ? value : defaultValue;
    }
    
    [Serializable]
    public class YourResult
    {
        public bool success;
        public string data;
        public string error;
    }
}
```

### 3. Add Node.js Tool Definition

Edit `.MCPServer/McpServer/src/index.ts`:

```typescript
// Add to TOOLS array
{
  name: 'unity_yourmodule_your_command',
  description: 'Description of what it does',
  inputSchema: {
    type: 'object',
    properties: {
      param1: {
        type: 'string',
        description: 'Parameter description',
      },
      param2: {
        type: 'number',
        description: 'Another parameter',
      },
    },
    required: ['param1'],
  },
},

// Add to switch statement
case 'unity_yourmodule_your_command':
  result = await unityClient.sendCommand('your_command', {
    param1: args?.param1,
    param2: args?.param2?.toString(),
  });
  break;
```

### 4. Build and Test

```bash
cd .MCPServer/McpServer
npm run build
```

Test in Unity:
```bash
curl -X POST http://localhost:8090/McpUnity/ \
  -H "Content-Type: application/json" \
  -d '{"command":"yourmodule_your_command","parameters":{"param1":"value"}}'
```

## Module Guidelines

### Command Naming
- Format: `{module}_{command}`
- Examples: `scene_create_object`, `asset_load_asset`, `log_get_logs`

### Parameter Handling
- Always use `GetParam()` helper with defaults
- Validate required parameters
- Return error object for invalid input

### Response Format
```csharp
// Success
return new Result { success = true, data = ... };

// Error
return new Result { success = false, error = "Message" };
```

### Unity Editor Safety
- Use `Undo.RecordObject()` for changes
- Call `EditorSceneManager.MarkSceneDirty()` after modifications
- Check for nulls before accessing objects

## Existing Modules Reference

### SceneModule (`scene_*`)
- `ping` - Health check
- `get_hierarchy` - Scene tree
- `create_object` - Create GameObject
- `delete_object` - Delete GameObject
- `select_object` - Select in hierarchy
- `set_property` / `get_property` - Component properties
- `get_components` / `add_component` - Component management
- `execute_menu` - Execute menu item

### AssetModule (`asset_*`)
- `get_assets` - List assets in folder
- `load_asset` - Load asset info
- `create_folder` - Create folder
- `delete_asset` - Delete asset

### LogModule (`log_*`)
- `get_logs` - Get console logs (filter: error/warning/log)
- `clear_logs` - Clear logs
- `get_log_count` - Get log count

### PrefabModule (`prefab_*`)
- `get_hierarchy` - Full prefab hierarchy
- `get_components` - Components of object
- `find_objects` - Search by name/component

## Testing Checklist

- [ ] Unity compiles without errors
- [ ] MCP Server starts successfully
- [ ] Command returns expected JSON
- [ ] Error cases handled gracefully
- [ ] Node.js server rebuilt (`npm run build`)

## Common Issues

### Command not found
- Check `[McpModule]` and `[McpCommand]` attributes
- Verify command name matches in Node.js switch
- Rebuild Node.js server

### Empty response data
- Ensure result class is `[Serializable]`
- Check `JsonUtility.ToJson()` works on result
- Verify no null references in result

### Unity not responding
- Check MCP Server window is running
- Verify port 8090 not blocked
- Check Unity Console for errors
