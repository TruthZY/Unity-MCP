#!/usr/bin/env node
import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { CallToolRequestSchema, ListToolsRequestSchema, } from '@modelcontextprotocol/sdk/types.js';
// Unity HTTP 客户端
class UnityClient {
    baseUrl = 'http://localhost:8090/McpUnity/';
    async sendCommand(command, parameters = {}) {
        const response = await fetch(this.baseUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ command, parameters }),
        });
        if (!response.ok) {
            throw new Error(`Unity request failed: ${response.statusText}`);
        }
        const data = await response.json();
        return data.response;
    }
    async ping() {
        try {
            const result = await this.sendCommand('ping');
            // Unity返回的data是JSON字符串，需要解析
            const data = typeof result?.data === 'string' ? JSON.parse(result.data) : result?.data;
            return data?.message === 'pong';
        }
        catch {
            return false;
        }
    }
}
const unityClient = new UnityClient();
// 定义可用工具
const TOOLS = [
    {
        name: 'unity_ping',
        description: 'Check if Unity editor is connected and responsive',
        inputSchema: {
            type: 'object',
            properties: {},
        },
    },
    {
        name: 'unity_get_hierarchy',
        description: 'Get the current scene hierarchy with all game objects',
        inputSchema: {
            type: 'object',
            properties: {},
        },
    },
    {
        name: 'unity_select_object',
        description: 'Select a game object in the Unity hierarchy',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Full path to the object (e.g., "Parent/Child")',
                },
                name: {
                    type: 'string',
                    description: 'Object name to search for',
                },
            },
        },
    },
    {
        name: 'unity_create_object',
        description: 'Create a new game object in the scene',
        inputSchema: {
            type: 'object',
            properties: {
                name: {
                    type: 'string',
                    description: 'Name for the new object',
                },
                primitiveType: {
                    type: 'string',
                    enum: ['Cube', 'Sphere', 'Cylinder', 'Capsule', 'Plane', 'Quad'],
                    description: 'Optional primitive type to create',
                },
                parent: {
                    type: 'string',
                    description: 'Optional parent object path',
                },
            },
            required: ['name'],
        },
    },
    {
        name: 'unity_delete_object',
        description: 'Delete a game object from the scene',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Full path to the object',
                },
                name: {
                    type: 'string',
                    description: 'Object name to delete',
                },
            },
        },
    },
    {
        name: 'unity_set_property',
        description: 'Set a property value on a component',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Path to the game object',
                },
                component: {
                    type: 'string',
                    description: 'Component type name (e.g., "Transform", "MeshRenderer")',
                },
                property: {
                    type: 'string',
                    description: 'Property or field name to set',
                },
                value: {
                    description: 'Value to set (can be number, string, or object for Vector3/Color)',
                },
            },
            required: ['path', 'component', 'property', 'value'],
        },
    },
    {
        name: 'unity_get_property',
        description: 'Get a property value from a component',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Path to the game object',
                },
                component: {
                    type: 'string',
                    description: 'Component type name',
                },
                property: {
                    type: 'string',
                    description: 'Property or field name to get',
                },
            },
            required: ['path', 'component', 'property'],
        },
    },
    {
        name: 'unity_get_components',
        description: 'Get all components on a game object',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Path to the game object',
                },
            },
            required: ['path'],
        },
    },
    {
        name: 'unity_add_component',
        description: 'Add a component to a game object',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Path to the game object',
                },
                type: {
                    type: 'string',
                    description: 'Full type name of the component to add',
                },
            },
            required: ['path', 'type'],
        },
    },
    {
        name: 'unity_execute_menu',
        description: 'Execute a Unity menu item',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Menu path (e.g., "GameObject/3D Object/Cube")',
                },
            },
            required: ['path'],
        },
    },
    // Asset Management
    {
        name: 'unity_get_assets',
        description: 'Get list of assets in a folder',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Folder path (default: "Assets")',
                },
                searchPattern: {
                    type: 'string',
                    description: 'Search pattern (default: "*")',
                },
                recursive: {
                    type: 'boolean',
                    description: 'Include subfolders (default: false)',
                },
            },
        },
    },
    {
        name: 'unity_load_asset',
        description: 'Load an asset from the Assets folder',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Asset path (e.g., "Assets/Prefabs/MyPrefab.prefab")',
                },
                type: {
                    type: 'string',
                    description: 'Asset type (default: "UnityEngine.Object")',
                },
            },
            required: ['path'],
        },
    },
    {
        name: 'unity_create_folder',
        description: 'Create a new folder in Assets',
        inputSchema: {
            type: 'object',
            properties: {
                parentPath: {
                    type: 'string',
                    description: 'Parent folder path (default: "Assets")',
                },
                folderName: {
                    type: 'string',
                    description: 'Name of the new folder',
                },
            },
            required: ['folderName'],
        },
    },
    {
        name: 'unity_delete_asset',
        description: 'Delete an asset or folder from Assets',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Asset path to delete (e.g., "Assets/mcpTest")',
                },
            },
            required: ['path'],
        },
    },
    // Log Management
    {
        name: 'unity_get_logs',
        description: 'Get Unity console logs with optional filtering',
        inputSchema: {
            type: 'object',
            properties: {
                filter: {
                    type: 'string',
                    enum: ['error', 'warning', 'log'],
                    description: 'Filter by log level',
                },
                logType: {
                    type: 'string',
                    enum: ['Error', 'Warning', 'Log', 'Exception'],
                    description: 'Filter by specific log type',
                },
                search: {
                    type: 'string',
                    description: 'Search keyword in message',
                },
                limit: {
                    type: 'number',
                    description: 'Maximum number of logs (max 50)',
                },
            },
        },
    },
    {
        name: 'unity_clear_logs',
        description: 'Clear all Unity console logs',
        inputSchema: {
            type: 'object',
            properties: {},
        },
    },
    {
        name: 'unity_get_log_count',
        description: 'Get the number of collected logs',
        inputSchema: {
            type: 'object',
            properties: {},
        },
    },
    // Prefab Analysis
    {
        name: 'unity_get_prefab_hierarchy',
        description: 'Get full hierarchy of a prefab asset',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Prefab path (e.g., "Assets/Prefabs/MyPrefab.prefab")',
                },
            },
            required: ['path'],
        },
    },
    {
        name: 'unity_get_prefab_components',
        description: 'Get components of an object in a prefab',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Prefab path',
                },
                objectPath: {
                    type: 'string',
                    description: 'Object path within prefab (optional, defaults to root)',
                },
            },
            required: ['path'],
        },
    },
    {
        name: 'unity_find_prefab_objects',
        description: 'Find objects in a prefab by name or component type',
        inputSchema: {
            type: 'object',
            properties: {
                path: {
                    type: 'string',
                    description: 'Prefab path',
                },
                name: {
                    type: 'string',
                    description: 'Object name to search for (optional)',
                },
                componentType: {
                    type: 'string',
                    description: 'Component type to filter by (optional)',
                },
            },
            required: ['path'],
        },
    },
];
// 创建 MCP 服务器
const server = new Server({
    name: 'unity-mcp-server',
    version: '1.0.0',
}, {
    capabilities: {
        tools: {},
    },
});
// 处理工具列表请求
server.setRequestHandler(ListToolsRequestSchema, async () => {
    return { tools: TOOLS };
});
// 处理工具调用请求
server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;
    try {
        let result;
        switch (name) {
            case 'unity_ping':
                const connected = await unityClient.ping();
                result = { connected, message: connected ? 'Unity is connected' : 'Unity is not responding' };
                break;
            case 'unity_get_hierarchy':
                result = await unityClient.sendCommand('get_hierarchy');
                break;
            case 'unity_select_object':
                result = await unityClient.sendCommand('select_object', {
                    path: args?.path,
                    name: args?.name,
                });
                break;
            case 'unity_create_object':
                result = await unityClient.sendCommand('create_object', {
                    name: args?.name,
                    primitiveType: args?.primitiveType,
                    parent: args?.parent,
                });
                break;
            case 'unity_delete_object':
                result = await unityClient.sendCommand('delete_object', {
                    path: args?.path,
                    name: args?.name,
                });
                break;
            case 'unity_set_property':
                result = await unityClient.sendCommand('set_property', {
                    path: args?.path,
                    component: args?.component,
                    property: args?.property,
                    value: args?.value,
                });
                break;
            case 'unity_get_property':
                result = await unityClient.sendCommand('get_property', {
                    path: args?.path,
                    component: args?.component,
                    property: args?.property,
                });
                break;
            case 'unity_get_components':
                result = await unityClient.sendCommand('get_components', {
                    path: args?.path,
                });
                break;
            case 'unity_add_component':
                result = await unityClient.sendCommand('add_component', {
                    path: args?.path,
                    type: args?.type,
                });
                break;
            case 'unity_execute_menu':
                result = await unityClient.sendCommand('execute_menu', {
                    path: args?.path,
                });
                break;
            // Asset Management
            case 'unity_get_assets':
                result = await unityClient.sendCommand('get_assets', {
                    path: args?.path,
                    searchPattern: args?.searchPattern,
                    recursive: args?.recursive,
                });
                break;
            case 'unity_load_asset':
                result = await unityClient.sendCommand('load_asset', {
                    path: args?.path,
                    type: args?.type,
                });
                break;
            case 'unity_create_folder':
                result = await unityClient.sendCommand('create_folder', {
                    parentPath: args?.parentPath,
                    folderName: args?.folderName,
                });
                break;
            case 'unity_delete_asset':
                result = await unityClient.sendCommand('delete_asset', {
                    path: args?.path,
                });
                break;
            // Log Management
            case 'unity_get_logs':
                result = await unityClient.sendCommand('get_logs', {
                    filter: args?.filter,
                    logType: args?.logType,
                    search: args?.search,
                    limit: args?.limit?.toString(),
                });
                break;
            case 'unity_clear_logs':
                result = await unityClient.sendCommand('clear_logs', {});
                break;
            case 'unity_get_log_count':
                result = await unityClient.sendCommand('get_log_count', {});
                break;
            // Prefab Analysis
            case 'unity_get_prefab_hierarchy':
                result = await unityClient.sendCommand('get_prefab_hierarchy', {
                    path: args?.path,
                });
                break;
            case 'unity_get_prefab_components':
                result = await unityClient.sendCommand('get_components', {
                    path: args?.path,
                    objectPath: args?.objectPath,
                });
                break;
            case 'unity_find_prefab_objects':
                result = await unityClient.sendCommand('find_objects', {
                    path: args?.path,
                    name: args?.name,
                    componentType: args?.componentType,
                });
                break;
            default:
                throw new Error(`Unknown tool: ${name}`);
        }
        return {
            content: [
                {
                    type: 'text',
                    text: JSON.stringify(result, null, 2),
                },
            ],
        };
    }
    catch (error) {
        return {
            content: [
                {
                    type: 'text',
                    text: `Error: ${error instanceof Error ? error.message : String(error)}`,
                },
            ],
            isError: true,
        };
    }
});
// 启动服务器
async function main() {
    const transport = new StdioServerTransport();
    await server.connect(transport);
    console.error('Unity MCP Server running on stdio');
}
main().catch((error) => {
    console.error('Fatal error:', error);
    process.exit(1);
});
//# sourceMappingURL=index.js.map