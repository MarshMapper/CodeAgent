using ModelContextProtocol.Client;

namespace CodeAgent.Agents;
public class AIAgentOptions
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Instructions { get; set; }
    public required IList<McpClientTool> Tools { get; set; }
    public int MaxOutputTokens { get; set; } = 4000;
}