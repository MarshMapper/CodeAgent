using CodeAgent.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using OpenAI;
using OpenAI.Chat;

namespace CodeAgent.Agents;

public abstract class McpAgentBase
{
    static McpAgentBase()
    {
        // Load environment variables from .env file
        DotNetEnv.Env.Load();
    }

    protected abstract string AgentName { get; }
    protected abstract string AgentDescription { get; }
    protected abstract string AgentInstructions { get; }
    // this default allows most code analysis scenarios, but can be overridden for agents that need to return larger outputs (like a full code file),
    // or for models with smaller context windows
    protected virtual int MaxOutputTokens { get; } = 4000;
    // Override this property in subclasses to filter available MCP tools by name
    protected virtual List<string>? ToolFilters { get; } = null;
    protected abstract StdioClientTransportOptions GetTransportOptions();

    protected static string GetEnvironmentVariable(string key, string defaultValue = "")
    {
        return Environment.GetEnvironmentVariable(key) ?? defaultValue;
    }

    public async Task<AIAgent> CreateAsync(IServiceCollection services, OpenAIClient aiClient, string modelId, string sourceName)
    {
        var transportOptions = GetTransportOptions();
        var mcpClientTools = new McpClientTools(transportOptions);

        // Prevent mcpClientTools from being disposed so that the MCP client connection
        // remains open for the lifetime of the agent
        services.AddSingleton(mcpClientTools);

        IList<McpClientTool> tools = await mcpClientTools.GetTools(ToolFilters);

        var chatOptions = new ChatOptions()
        {
            ToolMode = ChatToolMode.RequireAny,
            MaxOutputTokens = MaxOutputTokens,
            Instructions = AgentInstructions,
            Tools = [.. tools.Cast<AITool>()]
        };

        var chatAgentOptions = new ChatClientAgentOptions()
        {
            ChatOptions = chatOptions,
            Description = AgentDescription,
            Name = AgentName
        };

#pragma warning disable OPENAI001
#pragma warning disable MEAI001
        AIAgent agent = aiClient.GetChatClient(modelId).AsIChatClient().AsAIAgent(chatAgentOptions)
            .AsBuilder().UseOpenTelemetry(sourceName, configure: (cfg) => cfg.EnableSensitiveData = true).Build();
#pragma warning restore MEAI001
#pragma warning restore OPENAI001

        return agent;
    }
}