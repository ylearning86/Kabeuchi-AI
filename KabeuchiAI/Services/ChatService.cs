using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;

namespace KabeuchiAI.Services;

public interface IChatService
{
    Task<string> SendMessageAsync(string message);
}

public class FoundryChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FoundryChatService> _logger;
    private readonly TokenCredential _credential;
    private readonly IWebHostEnvironment _environment;

    public FoundryChatService(HttpClient httpClient, IConfiguration configuration, ILogger<FoundryChatService> logger, IWebHostEnvironment environment)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
        // 開発環境では AzureCliCredential、本番環境では DefaultAzureCredential（マネージド ID）を使用
        if (environment.IsDevelopment())
        {
            _credential = new AzureCliCredential();
            _logger.LogInformation("Using AzureCliCredential for development");
        }
        else
        {
            _credential = new DefaultAzureCredential();
            _logger.LogInformation("Using DefaultAzureCredential (managed identity) for production");
        }
    }

    public async Task<string> SendMessageAsync(string message)
    {
        try
        {
            var endpoint = _configuration["FoundryConfig:Endpoint"];
            var agentName = _configuration["FoundryConfig:AgentName"];

            if (string.IsNullOrEmpty(endpoint))
            {
                _logger.LogError("Foundry endpoint configuration is missing");
                return "申し訳ありません。エージェント設定がありません。";
            }

            _logger.LogInformation($"Calling Foundry agent via SDK with managed identity: {endpoint}");

            // Foundry v1 SDK 経由で呼び出し（API バージョン指定不要）
            var credential = _credential;
            var projectClient = new AIProjectClient(new Uri(endpoint), credential);

            var conversationResult = projectClient.OpenAI.Conversations.CreateProjectConversation();
            var conversation = conversationResult.Value;
            _logger.LogInformation("Conversation created: {ConversationId}", conversation.Id);

            var responsesClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
                defaultAgent: agentName,
                defaultConversationId: conversation.Id);

            var responseResult = await Task.Run(() => responsesClient.CreateResponse(message), CancellationToken.None);
            var response = responseResult.Value;

            var outputText = response.GetOutputText();
            if (!string.IsNullOrWhiteSpace(outputText))
            {
                return outputText;
            }

            // 念のため raw JSON も返せるように
            var rawJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false });
            return rawJson;
        }
        catch (Azure.Identity.AuthenticationFailedException ex)
        {
            _logger.LogError($"Azure authentication error: {ex.Message}");
            return $"認証エラー: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"Foundry API error: {ex.Message}");
            return $"エージェントに接続できません: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError($"Unexpected error: {ex.Message}");
            return $"エラーが発生しました: {ex.Message}";
        }
    }
}
