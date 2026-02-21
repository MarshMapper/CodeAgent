using DotNetEnv;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using CodeAgent.Agents;
using CodeAgent.Observability;

namespace CodeAgent;

static class Program
{
    const string SourceName = "CodeAgent";
    const string ServiceName = "CodeAgent.AgentService";

    static async Task Main(string[] args)
    {
        var (endpoint, modelId, apiKey) = GetModelConfiguration();

        var openAIOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(20)
        };

        OpenAIClient aiClient;

        var otlpEndpoint = Environment.GetEnvironmentVariable("OTLP_ENDPOINT") ?? "http://localhost:4317";
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            aiClient = new OpenAIClient(new ApiKeyCredential(apiKey), openAIOptions);

            OpenTelemetrySetup.Configure(builder, ServiceName, SourceName, otlpEndpoint);

            builder.Services.AddOpenAIResponses();
            builder.Services.AddOpenAIConversations();

            var roslynAgentFactory = new RoslynMcpAgent();
            var roslynAgent = await roslynAgentFactory.CreateAsync(builder.Services, aiClient, modelId, SourceName);
            builder.AddAIAgent("RoslynAgent", (serviceProvider, key) => roslynAgent);

            var sharpToolsAgentFactory = new SharpToolsMcpAgent();
            var sharpToolsAgent = await sharpToolsAgentFactory.CreateAsync(builder.Services, aiClient, modelId, SourceName);
            builder.AddAIAgent("SharpToolsAgent", (serviceProvider, key) => sharpToolsAgent);

            WebApplication app = builder.Build();

            var appLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CodeAgent.Program");

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
}
