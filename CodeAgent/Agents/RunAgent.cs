
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ModelContextProtocol.Client;
using OpenAI.Responses;

namespace CodeAgent.Agents;
/// <summary>
/// This class demonstrates how to run an agent using the RunAsync method, which provides more control over the execution of the agent, including handling function/tool calls and approvals.
/// It's currently unused in the main program that's been converted to use AIAgent.
/// </summary>
public static class RunAgent
{
    public static async Task<AgentResponse?> GetResponseUsingRunAsync(IChatClient chatClient, McpClient mcpClient, IList<McpClientTool> tools,
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
                "Analyze the file Program.cs.  Return the results as markdown."
                ,
                options: new ChatClientAgentRunOptions(chatOptions));


#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
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