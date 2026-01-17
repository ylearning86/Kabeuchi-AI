using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.AI.Projects;

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
            var subscriptionId = _configuration["FoundryConfig:SubscriptionId"];
            var resourceGroup = _configuration["FoundryConfig:ResourceGroup"];
            var projectName = _configuration["FoundryConfig:ProjectName"];
            var agentName = _configuration["FoundryConfig:AgentName"];

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(subscriptionId) || 
                string.IsNullOrEmpty(resourceGroup) || string.IsNullOrEmpty(projectName) || 
                string.IsNullOrEmpty(agentName))
            {
                _logger.LogError("Foundry configuration is missing required parameters");
                return "申し訳ありません。エージェント設定がありません。";
            }

            _logger.LogInformation("Calling Foundry agent via AgentsClient with managed identity: {Endpoint}", endpoint);

            var credential = _credential;
            var projectClient = new AIProjectClient(new Uri(endpoint), subscriptionId, resourceGroup, projectName, credential);
            var agentsClient = projectClient.GetAgentsClient();

            var threadOptions = new AgentThreadCreationOptions
            {
                Messages =
                {
                    new ThreadMessageOptions(MessageRole.User, message)
                }
            };

            // Use the public overload: CreateThreadAndRunAsync(agentId, threadOptions, ...)
            var run = await agentsClient.CreateThreadAndRunAsync(agentName, threadOptions);
            var runValue = run.Value;

            while (runValue.Status == RunStatus.Queued || runValue.Status == RunStatus.InProgress)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                runValue = (await agentsClient.GetRunAsync(runValue.ThreadId, runValue.Id)).Value;
            }

            var messages = (await agentsClient.GetMessagesAsync(runValue.ThreadId)).Value;
            var latest = messages.Data.LastOrDefault();

            // Extract text from ContentItems: cast to MessageTextContent to access the Text property
            string? text = null;
            if (latest?.ContentItems != null)
            {
                foreach (var content in latest.ContentItems)
                {
                    if (content is MessageTextContent textContent)
                    {
                        text = textContent.Text;
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return JsonSerializer.Serialize(latest, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Azure.Identity.AuthenticationFailedException ex)
        {
            _logger.LogError("Azure authentication error: {Message}", ex.Message);
            return $"認証エラー: {ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Foundry API error: {Message}", ex.Message);
            return $"エージェントに接続できません: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error: {Message}", ex.Message);
            return $"エラーが発生しました: {ex.Message}";
        }
    }
}
