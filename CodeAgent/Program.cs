using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Buffers.Text;
using System.ClientModel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Responses;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using CodeAgent.Mcp;

namespace CodeAgent;

public class AIAgentOptions
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Instructions { get; set; }
    public IList<McpClientTool> Tools { get; set; }
    public int MaxOutputTokens { get; set; } = 4000;
}
class Program
{
    const string SourceName = "OpenTelemetryAspire.ConsoleApp";
    const string ServiceName = "CodeAgent";

    static async Task Main(string[] args)
    {
        var (endpoint, modelId, apiKey) = GetModelConfiguration();

        var openAIOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(20)
        };
#pragma warning disable OPENAI001
#pragma warning disable MEAI001
        OpenAIClient aiClient;

        var otlpEndpoint = Environment.GetEnvironmentVariable("OTLP_ENDPOINT") ?? "http://localhost:4317";
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            aiClient = new OpenAIClient(new ApiKeyCredential(apiKey), openAIOptions);

            OpenTelemetrySetup.Configure(builder, ServiceName, SourceName, otlpEndpoint);

            builder.Services.AddOpenAIResponses();
            builder.Services.AddOpenAIConversations();

            var roslynAgent = await GetRoslynMcpAgent(builder.Services, aiClient, modelId);

            builder.AddAIAgent("RoslynAgent", (serviceProvider, key) => roslynAgent);

            var sharpToolsAgent = await GetSharpToolsMcpAgent(builder.Services, aiClient, modelId);
            builder.AddAIAgent("SharpToolsAgent", (serviceProvider, key) => sharpToolsAgent);

            WebApplication app = builder.Build();

            var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
            appLogger.LogInformation("Starting {ServiceName}", ServiceName);

            if (builder.Environment.IsDevelopment())
            {
                app.MapOpenAIResponses();
                app.MapOpenAIConversations();
                app.MapDevUI();
            }

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Agent Error: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            Console.WriteLine($"Note: The agent may have additional requirements. Error details: {ex.GetType().Name}");
        }
    }
    private static async Task<AIAgent> GetSharpToolsMcpAgent(IServiceCollection services, OpenAIClient aiClient, string modelId)
    {
        StdioClientTransportOptions sharpToolsMcpTransportOptions = new()
        {
            Name = "SharpTools",
            Command = "C:\\src\\WinDev\\SharpToolsMCP-main\\SharpTools.StdioServer\\bin\\Debug\\net8.0\\SharpTools.StdioServer.exe",
            Arguments = ["--log-directory", "C:\\src\\WinDev\\SharpToolsMCP-main\\logs", "--log-level", "Debug"]
        };
        McpClientTools mcpClientTools = new McpClientTools(sharpToolsMcpTransportOptions);
        // prevent mcpClientTools from being disposed so that the MCP client connection remains open for the lifetime of the agent,
        // otherwise the tools cannot be used by the agent
        services.AddSingleton(mcpClientTools);
        
        IList<McpClientTool> tools = await mcpClientTools.GetTools();

        AIAgentOptions aIAgentOptions = new AIAgentOptions()
        {
            Name = "SharpToolsAgent",
            Description = "An expert code analyst agent that uses SharpTools analyzers to analyze code files.",
            Instructions = @"You are an expert code analyst. Follow these steps one at a time:
    1. Load the solution file first using SharpTool_LoadSolution.  Newer projects use the .slnx extension for solution files.
    2. Then load the specific project using SharpTool_LoadProject
    3. Finally analyze the requested file using SharpTool_AnalyzeComplexity
    Execute each tool in sequence and use the results to provide your analysis.",
            Tools = tools,
            MaxOutputTokens = 4000
        };

        return await GetAIAgent(aiClient, modelId, aIAgentOptions);
    }
    private static async Task<AIAgent> GetRoslynMcpAgent(IServiceCollection services, OpenAIClient aiClient, string modelId)
    {
        StdioClientTransportOptions rosylnMcpTransportOptions = new()
        {
            Name = "RoslynMCP",
            Command = "dotnet",
            Arguments = ["run", "--project", "\\src\\WinDev\\roslyn-mcp\\RoslynMCP\\RoslynMCP.csproj"],
        };
        McpClientTools mcpClientTools = new McpClientTools(rosylnMcpTransportOptions);

        // prevent mcpClientTools from being disposed so that the MCP client connection remains open for the lifetime of the agent,
        // otherwise the tools cannot be used by the agent
        services.AddSingleton(mcpClientTools);

        IList<McpClientTool> tools = await mcpClientTools.GetTools();

        AIAgentOptions aIAgentOptions = new AIAgentOptions()
        {
            Name = "RoslynAgent",
            Description = "An expert code analyst agent that uses Roslyn analyzers to analyze code files.",
            Instructions = @"You are an expert code analyst.  Run ValidateFile tool to validate the specified file and return any issues found.  Pass true for the runAnalyzers parameter to get code analysis results.",
            Tools = tools,
            MaxOutputTokens = 4000
        };

        return await GetAIAgent(aiClient, modelId, aIAgentOptions);
    }
    private static async Task<AIAgent> GetAIAgent(OpenAIClient aiClient, string modelId, AIAgentOptions agentOptions)
    {
        var chatOptions = new ChatOptions()
        {
            ToolMode = ChatToolMode.RequireAny,
            MaxOutputTokens = agentOptions.MaxOutputTokens,
            Instructions = agentOptions.Instructions,
            Tools = [.. agentOptions.Tools.Cast<AITool>()]
        };

        var chatAgentOptions = new ChatClientAgentOptions()
        {
            ChatOptions = chatOptions,
            Description = agentOptions.Description,
            Name = agentOptions.Name
        };

        AIAgent aIAgent = aiClient.GetChatClient(modelId).AsIChatClient().AsAIAgent(chatAgentOptions)
            .AsBuilder().UseOpenTelemetry(SourceName, configure: (cfg) => cfg.EnableSensitiveData = true).Build();
        return aIAgent;
    }
    private static async Task<AIAgent> GetAIAgent(OpenAIClient aiClient, string modelId, string agentName)
    {
        StdioClientTransportOptions rosylnMcpTransportOptions = new()
        {
            Name = "RoslynMCP",
            Command = "dotnet",
            Arguments = ["run", "--project", "\\src\\WinDev\\roslyn-mcp\\RoslynMCP\\RoslynMCP.csproj"],
        };
        var mcpClient = await McpClient.CreateAsync(new StdioClientTransport(rosylnMcpTransportOptions));
        var tools = await mcpClient.ListToolsAsync();
        var chatOptions = new ChatOptions()
        {
            ToolMode = ChatToolMode.RequireAny,
            MaxOutputTokens = 4000,
            Instructions = @"You are an expert code analyst.  Run ValidateFile tool to validate the specified file and return any issues found.  Pass true for the runAnalyzers parameter to get code analysis results.",
            Tools = [.. tools.Cast<AITool>()]
        };

        var chatAgentOptions = new ChatClientAgentOptions()
        {
            ChatOptions = chatOptions,
            Description = "Rosyln MCP Agent",
            Name = agentName
        };

        AIAgent aIAgent = aiClient.GetChatClient(modelId).AsIChatClient().AsAIAgent(chatAgentOptions)
            .AsBuilder().UseOpenTelemetry(SourceName, configure: (cfg) => cfg.EnableSensitiveData = true).Build();
        return aIAgent;
    }

    private static (string endpoint, string modelId, string apiKey) GetModelConfiguration()
    {
        Env.TraversePath().Load();

        bool use_foundrylocal = Env.GetBool("USE_FOUNDRYLOCAL");

        var foundrylocal_endpoint = Env.GetString("FOUNDRYLOCAL_ENDPOINT") ?? "http://127.0.0.1:57687/v1";
        var foundrylocal_model_id = Env.GetString("FOUNDRYLOCAL_MODEL_ID") ?? "qwen2.5-coder-7b-instruct-generic-cpu:4";
        var github_endpoint = Environment.GetEnvironmentVariable("GITHUB_ENDPOINT") ?? throw new InvalidOperationException("GITHUB_ENDPOINT is not set.");
        var github_model_id = Environment.GetEnvironmentVariable("GITHUB_MODEL_ID") ?? throw new InvalidOperationException("GITHUB_MODEL_ID is not set.");
        var github_token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? throw new InvalidOperationException("GITHUB_TOKEN is not set.");

        // Conditionally set endpoint and model based on use_foundrylocal flag
        var endpoint = use_foundrylocal ? foundrylocal_endpoint : github_endpoint;
        var modelId = use_foundrylocal ? foundrylocal_model_id : github_model_id;
        var apiKey = use_foundrylocal ? "nokey" : github_token;

        return (endpoint, modelId, apiKey);
    }

    private static async Task<AgentResponse?> GetResponseUsingRunAsync(IChatClient chatClient, McpClient mcpClient, IList<McpClientTool> tools,
        string agentInstructions)
    {
        AgentResponse? agentResponse = null;
        try
        {
            // Create agent with properly configured tools
            var agent = chatClient.AsAIAgent(
                name: "CodeAnalyst",
                instructions: agentInstructions,
                tools: [.. tools.Cast<AITool>()]
            );

            // passing the tools in here doesn't seem to make a difference when using AIAgent
            var chatOptions = new ChatOptions()
            {
                ToolMode = ChatToolMode.RequireAny,
                Tools = [.. tools.Cast<AITool>()],
                MaxOutputTokens = 4000
            };

            agentResponse = await agent.RunAsync(
                "Analyze the file /src/WinDev/CodeAgent/CodeAgent/Program.cs.  Return the results as markdown."
                ,
                options: new ChatClientAgentRunOptions(chatOptions));


            var functionApprovalRequests = agentResponse.Messages
                .SelectMany(x => x.Contents)
                .OfType<FunctionApprovalRequestContent>()
                .ToList();

            foreach (var requestContent in functionApprovalRequests)
            {
                var approvalMessage = new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, [requestContent.CreateResponse(true)]);
                Console.WriteLine(await agent.RunAsync(approvalMessage));
            }
#pragma warning restore MEAI001

            Console.WriteLine($"\n=== Agent Final Response ===");
            Console.WriteLine(agentResponse);
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetResponseUsingRunAsync Error: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            throw;
        }
        return agentResponse;
    }

    public static async Task<IList<Microsoft.Extensions.AI.ChatMessage>> GetResponse(IChatClient chatClient, List<Microsoft.Extensions.AI.ChatMessage> chatHistory, ChatOptions? options = null)
    {
        var response = await chatClient.GetResponseAsync(chatHistory, options);
        Console.WriteLine(response.Text);
        foreach (var message in response.Messages)
        {
            Console.WriteLine($"{message.Role}: {message.Text}");
        }
        Console.WriteLine(response.Text);

        return response.Messages;
    }
}