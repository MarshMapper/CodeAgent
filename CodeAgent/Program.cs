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

namespace CodeAgent;

class Program
{
    static async Task Main(string[] args)
    {
        Env.TraversePath().Load();

        var foundrylocal_endpoint = Env.GetString("FOUNDRYLOCAL_ENDPOINT") ?? "http://127.0.0.1:57687/v1";
        var foundrylocal_model_id = Env.GetString("FOUNDRYLOCAL_MODEL_ID") ?? "qwen2.5-coder-7b-instruct-generic-cpu:4";

        var openAIOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(foundrylocal_endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(5)
        };
        var client = new OpenAIClient(new ApiKeyCredential("nokey"), openAIOptions);
        var chatClient = client.GetChatClient(foundrylocal_model_id);
        IChatClient mcpChatClient =
            new ChatClientBuilder(chatClient.AsIChatClient())
                .UseFunctionInvocation()
                .Build();
/*        Console.WriteLine("=== Test 3: AIAgent ===");
        try
        {
            AIAgent agent = chatClient.CreateAIAgent(
                name: "Assistant",
                instructions: "You are a helpful assistant. Keep responses concise."
            );

            var agentResponse = await agent.RunAsync("What is 2+2?");
            Console.WriteLine($"Agent Response: {agentResponse}\n");

            Console.Write("Agent Streaming Response: ");
            await foreach (var update in agent.RunStreamingAsync("What is the capital of France?"))
            {
                Console.Write(update);
            }
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Agent Error: {ex.Message}");
            Console.WriteLine($"Note: The agent may have additional requirements. Error details: {ex.GetType().Name}");
        } */
        try
        {
            Console.WriteLine("=== Test 4: MCP Client ===");
            await using var mcpClient = await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Name = "RoslynMCP",
                Command = "dotnet",
                Arguments = ["run", "--project", "\\src\\WinDev\\roslyn-mcp-main\\RoslynMCP\\RoslynMCP.csproj"],
            }));
            var tools = await mcpClient.ListToolsAsync();
            Console.WriteLine("Available tools:");
            foreach (var tool in tools)
            {
                Console.WriteLine($"  {tool.Name}: {tool.Description}");
            }
            var chatOptions = new ChatOptions
            {
                Tools = [..tools]
            };
            var chatHistory = new List<Microsoft.Extensions.AI.ChatMessage>();
            while (true)
            {
                Console.WriteLine("Enter a prompt for the MCP client:");
                var userPrompt = Console.ReadLine();
                if (string.IsNullOrEmpty(userPrompt)) break;

                chatHistory.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, userPrompt));


                // using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)); // adjust timeout as needed
                // var serverResponse = await GetResponse(mcpChatClient, chatHistory, chatOptions);
                var toolResponse = await CallTool(mcpClient, chatHistory, chatOptions);

                /* await foreach (var item in mcpChatClient.GetStreamingResponseAsync(chatHistory, chatOptions))
                {

                    chatHistory.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Tool, item.Text));

                    // var usage = item.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;
                    // if (usage != null) usageDetails = usage;
                } */

                 Console.WriteLine();
            }
       }
        catch (Exception ex)
        {
            Console.WriteLine($"MCP Client Error: {ex.Message}");
            Console.WriteLine($"Note: Ensure that Node.js and the required MCP server package are installed. Error details: {ex.GetType().Name}");
        }
    }

    public static async Task<IList<Microsoft.Extensions.AI.ChatMessage>> CallTool(McpClient mcpClient, List<Microsoft.Extensions.AI.ChatMessage> chatHistory, ChatOptions? options = null)
    {
        Dictionary<string, object> parameters = new Dictionary<string, object>
        {
            { "filePath", "\\src\\windev\\codeagent\\codeagent\\Program.cs" },
            { "runAnalyzers", true }
        };
        // chatHistory.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, userPrompt));
        var result = mcpClient.CallToolAsync("ValidateFile", parameters).Result;
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