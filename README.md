# CodeAgent - Demo of creating AI Agents with Microsoft Agent Framework that can use Model Context Protocol (MCP) Tools

## Overview
This project demonstrates how to create AI Agents using the **Microsoft Agent Framework** that can interact with tools exposed by **Model Context Protocol (MCP)** servers.  Integration of **DevUI** provides a user interface for interacting with the agents and monitoring their behavior. The agents leverage OpenTelemetry for observability and can work with various AI models including local and cloud-based solutions.

## Features

### AI Model Support

The project supports multiple AI backends:
- **GitHub Models** (Hosted in Azure OpenAI) for prototyping of cloud-based inference
- **Foundry Local** for local model deployment

**Note**: When using Foundry Local, ensure that the selected model supports tool calling and can properly process the returned results. Models like `qwen2.5-coder-7b-instruct` have been used with the most success so far.

### MCP Server Integration

The project includes integration with two open-source MCP servers, but the code should work with any MCP server that follows the protocol:

#### 1. RoslynMCP
The RoslynMCP server provides Roslyn compiler services through MCP, enabling:
- Code validation and analysis
- Syntax checking
- Use of Roslyn analyzers and code fixes
- For complete details, see the [RoslynMCP repository](https://github.com/egorpavlikhin/roslyn-mcp)

#### 2. SharpTools MCP
The SharpTools MCP server offers additional developer tools for:
- Code complexity analysis
- Code metrics
- For complete details, see the [SharpTools MCP repository](https://github.com/kooshi/SharpToolsMCP)

Both MCP servers are exposed as agents through the `McpClientTools` class, which handles:
- MCP client initialization with STDIO or HTTP transport
- Tool discovery and filtering
- Converting MCP tools to `AIFunction` instances
- Tool execution and result handling

## Configuration

The project uses **DotNetEnv** for configuration management. Copy `.env example` to `.env` and configure the following variables:

```properties
# Model Selection
USE_FOUNDRYLOCAL=false                                    # true for local model, false for GitHub Models

# Foundry Local Configuration - run "foundry service status" to get the correct endpoint
# The model most be loaded and running in Foundry Local using either "foundry model run" or "foundry model load"
FOUNDRYLOCAL_ENDPOINT=http://127.0.0.1:56432/v1
FOUNDRYLOCAL_MODEL_ID=qwen2.5-coder-7b-instruct-generic-cpu:4

# GitHub Models Configuration
GITHUB_ENDPOINT=https://models.inference.ai.azure.com
GITHUB_TOKEN=github_pat_xxxxx                             # Your GitHub PAT token
GITHUB_MODEL_ID=GPT-4o

# MCP Server Paths
ROSLYN_MCP_PATH=\src\WinDev\roslyn-mcp                    # Path to RoslynMCP server
SHARPTOOLS_MCP_PATH=\src\WinDev\SharpToolsMCP-main        # Path to SharpTools MCP server
SHARPTOOLS_LOG_PATH=\src\WinDev\SharpToolsMCP-main\logs   # SharpTools logging directory
```

## Getting Started

1. **Clone the repository** and navigate to the project directory
2. **Copy** `.env example` to `.env` and configure your settings
3. **Ensure MCP servers** are available at the configured paths
4. **Run the application**:
   ```bash
   dotnet run --project CodeAgent/CodeAgent.csproj
   ```
5. **Access the Dev UI** at `http://localhost:8081/devui` (or `https://localhost:7183/devui`)

## Project Structure

```
CodeAgent/
├── Agents/                    # Agent implementations
│   ├── RoslynMcpAgent.cs     # Roslyn compiler agent
│   └── SharpToolsMcpAgent.cs # Developer tools agent
├── Mcp/
│   └── McpClientTools.cs     # MCP client integration
├── Observability/
│   └── OpenTelemetrySetup.cs # Telemetry configuration
└── Program.cs                # Application entry point
```

## OpenTelemetry Integration

The project includes observability through OpenTelemetry:
- **Tracing**: ASP.NET Core, HTTP client, and custom activity sources
- **Metrics**: Runtime, ASP.NET Core, and custom meters
- **Logging**: Structured logging with OTLP export

Configure the OTLP endpoint via environment variable:
```
OTLP_ENDPOINT=http://localhost:4317
```

## Technologies Used

- **.NET 10.0**
- **Microsoft.Agents.AI** (Preview SDK) for agent framework
- **ModelContextProtocol** Tools for MCP server integration
- **OpenTelemetry** for observability
- **DotNetEnv** for configuration
- **ASP.NET Core** for hosting
- **DevUI** for development user interface