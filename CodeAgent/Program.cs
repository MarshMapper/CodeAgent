using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Buffers.Text;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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

        Console.WriteLine("=== Test 3: AIAgent ===");
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
        }
    }
}