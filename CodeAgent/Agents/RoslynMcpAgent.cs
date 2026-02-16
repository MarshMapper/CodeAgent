using ModelContextProtocol.Client;

namespace CodeAgent.Agents;

public class RoslynMcpAgent : McpAgentBase
{
    protected override string AgentName => "RoslynAgent";

    protected override string AgentDescription =>
        "An expert code analyst agent that uses Roslyn analyzers to analyze code files.";

    protected override string AgentInstructions =>
        @"You are an expert code analyst.  Run ValidateFile tool to validate the specified file and return any issues found.  Pass true for the runAnalyzers parameter to get code analysis results.";

    protected override StdioClientTransportOptions GetTransportOptions()
    {
        var basePath = GetEnvironmentVariable("ROSLYN_MCP_PATH", "/src/roslyn-mcp");
        
        return new()
        {
            Name = "RoslynMCP",
            Command = "dotnet",
            Arguments = ["run", "--project", $"{basePath}\\RoslynMCP\\RoslynMCP.csproj"],
        };
    }
}