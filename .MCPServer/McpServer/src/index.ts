#!/usr/bin/env node

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  Tool,
} from '@modelcontextprotocol/sdk/types.js';
import * as fs from 'fs';
import * as path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Unity HTTP 客户端
class UnityClient {
  private baseUrl: string = 'http://localhost:8090/McpUnity/';

  async sendCommand(command: string, parameters: Record<string, any> = {}): Promise<any> {
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

  async ping(): Promise<boolean> {
    try {
      const result = await this.sendCommand('ping');
      const data = typeof result?.data === 'string' ? JSON.parse(result.data) : result?.data;
      return data?.message === 'pong';
    } catch {
      return false;
    }
  }
}

const unityClient = new UnityClient();

// 配置文件路径（相对于当前工作目录）
const CONFIG_PATH = process.env.MCP_TOOLS_CONFIG || './tools.json';

// 配置数据结构
interface ToolsConfig {
  generatedAt: string;
  unityVersion: string;
  commands: CommandConfig[];
}

interface CommandConfig {
  module: string;
  command: string;
  toolName: string;
  description: string;
  parameters: ParameterConfig[];
}

interface ParameterConfig {
  name: string;
  description: string;
  required: boolean;
  defaultValue?: string;
  example?: string;
}

// 加载配置文件
function loadToolsConfig(): ToolsConfig | null {
  try {
    // 尝试多个路径
    const paths = [
      CONFIG_PATH,
      path.join(__dirname, '../tools.json'),
      path.join(process.cwd(), 'tools.json'),
      './tools.json',
    ];

    for (const configPath of paths) {
      if (fs.existsSync(configPath)) {
        console.error(`[MCP] Loading config from: ${configPath}`);
        const content = fs.readFileSync(configPath, 'utf-8');
        const config: ToolsConfig = JSON.parse(content);
        console.error(`[MCP] Loaded ${config.commands.length} commands (generated at ${config.generatedAt})`);
        return config;
      }
    }

    console.error('[MCP] Warning: tools.json not found, using fallback tools');
    return null;
  } catch (error) {
    console.error(`[MCP] Error loading config: ${error}`);
    return null;
  }
}

// 从配置生成工具定义
function generateToolsFromConfig(config: ToolsConfig | null): Tool[] {
  if (!config) {
    // 如果没有配置，使用默认的 ping 工具
    return [
      {
        name: 'unity_ping',
        description: 'Check if Unity editor is connected and responsive',
        inputSchema: {
          type: 'object',
          properties: {},
        },
      },
    ];
  }

  const tools: Tool[] = [
    {
      name: 'unity_ping',
      description: 'Check if Unity editor is connected and responsive',
      inputSchema: {
        type: 'object',
        properties: {},
      },
    },
  ];

  for (const cmd of config.commands) {
    const properties: Record<string, any> = {};
    const required: string[] = [];

    for (const param of cmd.parameters) {
      properties[param.name] = {
        type: 'string',
        description: param.description + (param.example ? ` (example: ${param.example})` : ''),
      };

      if (param.required) {
        required.push(param.name);
      }
    }

    tools.push({
      name: cmd.toolName,
      description: cmd.description,
      inputSchema: {
        type: 'object',
        properties,
        required: required.length > 0 ? required : undefined,
      },
    });
  }

  return tools;
}

// 加载配置
const toolsConfig = loadToolsConfig();
const TOOLS: Tool[] = generateToolsFromConfig(toolsConfig);

// 创建工具名称到命令的映射
const toolCommandMap: Map<string, CommandConfig> = new Map();
if (toolsConfig) {
  for (const cmd of toolsConfig.commands) {
    toolCommandMap.set(cmd.toolName, cmd);
  }
}

// 创建 MCP 服务器
const server = new Server(
  {
    name: 'unity-mcp-server',
    version: '1.0.0',
  },
  {
    capabilities: {
      tools: {},
    },
  }
);

// 处理工具列表请求
server.setRequestHandler(ListToolsRequestSchema, async () => {
  // 每次请求时重新加载配置（支持热更新）
  const freshConfig = loadToolsConfig();
  const freshTools = generateToolsFromConfig(freshConfig);
  return { tools: freshTools };
});

// 处理工具调用请求
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const { name, arguments: args } = request.params;

  try {
    let result: any;

    // 特殊处理 ping
    if (name === 'unity_ping') {
      const connected = await unityClient.ping();
      result = { connected, message: connected ? 'Unity is connected' : 'Unity is not responding' };
      return {
        content: [{ type: 'text', text: JSON.stringify(result, null, 2) }],
      };
    }

    // 从映射中获取命令配置
    const cmdConfig = toolCommandMap.get(name);
    if (!cmdConfig) {
      throw new Error(`Unknown tool: ${name}`);
    }

    // 构建参数
    const parameters: Record<string, any> = {};
    if (args) {
      for (const [key, value] of Object.entries(args)) {
        if (value !== undefined && value !== null) {
          parameters[key] = value;
        }
      }
    }

    // 调用 Unity 命令
    result = await unityClient.sendCommand(`${cmdConfig.module}_${cmdConfig.command}`, parameters);

    return {
      content: [
        {
          type: 'text',
          text: JSON.stringify(result, null, 2),
        },
      ],
    };
  } catch (error) {
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
  console.error('[MCP] Unity MCP Server starting...');
  console.error(`[MCP] Loaded ${TOOLS.length - 1} custom tools (+ ping)`);

  const transport = new StdioServerTransport();
  await server.connect(transport);

  console.error('[MCP] Server connected and ready');
}

main().catch((error) => {
  console.error('[MCP] Fatal error:', error);
  process.exit(1);
});
