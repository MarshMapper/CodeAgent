

using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace CodeAgent.Mcp;
public class McpClientTools : IAsyncDisposable
{
    private readonly StdioClientTransportOptions? _stdioClientTransportOptions;
    private readonly HttpClientTransportOptions? _httpClientTransportOptions;
    private McpClient _mcpClient;

    public McpClientTools(StdioClientTransportOptions stdioClientTransportOptions)
    {
        _stdioClientTransportOptions = stdioClientTransportOptions;
    }
    public McpClientTools(HttpClientTransportOptions httpClientTransportOptions)
    {
        _httpClientTransportOptions = httpClientTransportOptions;
    }
    public async ValueTask DisposeAsync()
    {
        if (_mcpClient is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
        else if (_mcpClient is IDisposable syncDisposable)
        {
            syncDisposable.Dispose();
        }
        _mcpClient = null;
    }
    internal async Task<McpClient> GetMcpClient()
    {
        if (_mcpClient != null)
        {
            return _mcpClient;
        }
        if (_stdioClientTransportOptions == null && _httpClientTransportOptions == null)
        {
            throw new InvalidOperationException("At least one transport option must be configured.");
        }
        if (_stdioClientTransportOptions != null && _httpClientTransportOptions != null)
        {
            throw new InvalidOperationException("Only one transport option can be configured at a time.");
        }
        if (_stdioClientTransportOptions != null)        
        {
            _mcpClient = await McpClient.CreateAsync(new StdioClientTransport(_stdioClientTransportOptions));
        }
        else
        {
            _mcpClient = await McpClient.CreateAsync(new HttpClientTransport(_httpClientTransportOptions));
        }
        return _mcpClient;
    }
    public async Task<IList<McpClientTool>> GetTools()
    {
        var mcpClient = await GetMcpClient();
        return await mcpClient.ListToolsAsync();
    }
    private async Task<List<AIFunction>> GetAIFunctionsFromTools(IList<McpClientTool> tools)
    {
        var mcpClient = await GetMcpClient();
        List<AIFunction> aiFunctions = new();

        foreach (var tool in tools)
        {
            if (true) // (tool.Name.Contains("Load") || tool.Name.Contains("Analyze"))
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

    public async Task<IList<Microsoft.Extensions.AI.ChatMessage>> CallTool(List<Microsoft.Extensions.AI.ChatMessage> chatHistory, ChatOptions? options = null)
    {
        var mcpClient = await GetMcpClient();
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
    private async Task<List<AIFunction>> GetAIFunctionsFromMCPServer()
    {
        var mcpClient = await GetMcpClient();
        var tools = await mcpClient.ListToolsAsync();
        return await GetAIFunctionsFromTools(tools);
    }   
    public async static Task ShowTools(IList<McpClientTool> tools)
    {
        Console.WriteLine("Available tools:");
        foreach (var tool in tools)
        {
            Console.WriteLine($"  {tool.Name}: {tool.Description}");
        }
    }
}