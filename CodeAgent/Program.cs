using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Buffers.Text;
using System.ClientModel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Responses;

namespace CodeAgent;

class Program
{
    static string _sharptoolInstructions = @"You are an expert code analyst. Follow these steps one at a time:
    1. Load the solution file first using SharpTool_LoadSolution
    2. Then load the specific project using SharpTool_LoadProject
    3. Finally analyze the requested file using SharpTool_AnalyzeComplexity
    Execute each tool in sequence and use the results to provide your analysis.";
    static string _roslynNcpInstructions = @"You are an expert code analyst.  Run ValidateFile tool to validate the specified file and return any issues found.";

    static async Task Main(string[] args)
    {
        var (endpoint, modelId, apiKey) = GetModelConfiguration();

        var openAIOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(20)
        };
#pragma warning disable OPENAI001
        OpenAIClient aiClient;
        IChatClient chatClient;

        try
        {
            var useRoslynMcp = true;

            aiClient = new OpenAIClient(new ApiKeyCredential(apiKey), openAIOptions);

            chatClient = aiClient.GetChatClient(modelId).AsIChatClient().AsBuilder().UseFunctionInvocation().Build();

            StdioClientTransportOptions rosylnMcpTransportOptions = new()
            {
                Name = "RoslynMCP",
                Command = "dotnet",
                Arguments = ["run", "--project", "\\src\\WinDev\\roslyn-mcp\\RoslynMCP\\RoslynMCP.csproj"],
            };
            StdioClientTransportOptions sharpToolsMcpTransportOptions = new()
            {
                Name = "SharpTools",
                Command = "C:\\src\\WinDev\\SharpToolsMCP-main\\SharpTools.StdioServer\\bin\\Debug\\net8.0\\SharpTools.StdioServer.exe",
                Arguments = ["--log-directory", "C:\\src\\WinDev\\SharpToolsMCP-main\\logs", "--log-level", "Debug"]
            };
            await using var mcpClient = await McpClient.CreateAsync(new StdioClientTransport(
                useRoslynMcp ? rosylnMcpTransportOptions : sharpToolsMcpTransportOptions));

            var tools = await mcpClient.ListToolsAsync();
            Console.WriteLine("Available tools:");
            foreach (var tool in tools)
            {
                Console.WriteLine($"  {tool.Name}: {tool.Description}");
            }
            await CallTool(mcpClient, new List<Microsoft.Extensions.AI.ChatMessage>());
            AgentResponse? agentResponse = await GetResponseUsingRunAsync(chatClient, mcpClient, tools, 
                useRoslynMcp ? _roslynNcpInstructions : _sharptoolInstructions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Agent Error: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            Console.WriteLine($"Note: The agent may have additional requirements. Error details: {ex.GetType().Name}");
        }
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
            // Create AIFunction wrappers for MCP tools with proper execution handlers
            List<AIFunction> aiFunctions = GetAIFunctionsFromTools(mcpClient, tools);

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
                MaxOutputTokens = 4000
            };

            agentResponse = await agent.RunAsync(
                "Analyze the file /src/WinDev/CodeAgent/CodeAgent/Program.cs.  Return the results as markdown."
                ,
                options: new ChatClientAgentRunOptions(chatOptions));
                
    #pragma warning disable MEAI001
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
    private static List<AIFunction> GetAIFunctionsFromTools(McpClient mcpClient, IList<McpClientTool> tools)
    {
        List<AIFunction> aiFunctions = new();

        foreach (var tool in tools)
        {
            if (tool.Name.Contains("Load") || tool.Name.Contains("Analyze"))
            {
                // Capture the tool in a local variable to avoid closure issues
                var currentTool = tool;

                // Create an AIFunction that properly executes the MCP tool
                var aiFunction = AIFunctionFactory.Create(
                    async (Dictionary<string, object?> arguments) =>
                    {
                        Console.WriteLine($"Executing MCP tool: {currentTool.Name}");
                        Console.WriteLine($"Arguments: {string.Join(", ", arguments.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

                        try
                        {
                            var result = await mcpClient.CallToolAsync(currentTool.Name, arguments);
                            var resultText = string.Join("\n", result.Content.Select(c => c.ToString()));
                            Console.WriteLine($"Tool result: {resultText}");
                            return resultText;
                        }
                        catch (Exception ex)
                        {
                            var errorMsg = $"Error executing {currentTool.Name}: {ex.Message}";
                            Console.WriteLine(errorMsg);
                            return errorMsg;
                        }
                    },
                    currentTool.Name,
                    currentTool.Description
                );

                aiFunctions.Add(aiFunction);
            }
        }
        return aiFunctions;
    }

    public static async Task<IList<Microsoft.Extensions.AI.ChatMessage>> CallTool(McpClient mcpClient, List<Microsoft.Extensions.AI.ChatMessage> chatHistory, ChatOptions? options = null)
    {
        Console.WriteLine("Calling ValidateFile tool via MCP Client...");
        Dictionary<string, object> parameters = new Dictionary<string, object>
        {
            { "filePath", "\\src\\windev\\codeagent\\codeagent\\Program.cs" },
            { "runAnalyzers", true }
        };
        var result = await mcpClient.CallToolAsync("ValidateFile", parameters);
        Console.WriteLine("Tool Result:");
        foreach (var contentBlock in result.Content)
        {
            Console.WriteLine($"Content Type: {contentBlock.Type}");
            Console.WriteLine($"Content block: {contentBlock.ToString()}");
        }
        return chatHistory;
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