using ModelContextProtocol.Client;

namespace CodeAgent.Agents;

public class SharpToolsMcpAgent : McpAgentBase
{
    protected override string AgentName => "SharpToolsAgent";

    protected override string AgentDescription =>
        "An expert code analyst agent that uses SharpTools analyzers to analyze code files.";

    protected override string AgentInstructions =>
        @"You are an expert code analyst. Follow these steps one at a time:
    1. Load the solution file first using SharpTool_LoadSolution.  Newer projects use the .slnx extension for solution files.
    2. Then load the specific project using SharpTool_LoadProject
    3. Finally analyze the requested file using SharpTool_AnalyzeComplexity
    Execute each tool in sequence and use the results to provide your analysis.";
    
    protected override List<string>? ToolFilters => new() { "Load", "Analyze" };

    protected override StdioClientTransportOptions GetTransportOptions()
    {
        var basePath = GetEnvironmentVariable("SHARPTOOLS_MCP_PATH", "/src/SharpToolsMCP-main");
        var logPath = GetEnvironmentVariable("SHARPTOOLS_LOG_PATH", $"{basePath}\\logs");
        
        return new()
        {
            Name = "SharpTools",
            Command = $"{basePath}\\SharpTools.StdioServer\\bin\\Debug\\net8.0\\SharpTools.StdioServer.exe",
            Arguments = ["--log-directory", logPath, "--log-level", "Debug"]
        };
    }
}