using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.AI.Projects;
using System.Threading;

namespace KabeuchiAI.Services;

// Custom TokenCredential wrapper that ensures correct scope for Azure AI
public class AzureAITokenCredential : TokenCredential
{
    private readonly TokenCredential _innerCredential;
    private static readonly string[] AzureAIScopes = new[] { "https://ai.azure.com/.default" };

    public AzureAITokenCredential(TokenCredential innerCredential)
    {
        _innerCredential = innerCredential;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var context = new TokenRequestContext(AzureAIScopes);
        return _innerCredential.GetToken(context, cancellationToken);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var context = new TokenRequestContext(AzureAIScopes);
        return _innerCredential.GetTokenAsync(context, cancellationToken);
    }
}

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

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(agentName))
            {
                _logger.LogError("Foundry endpoint or agent name configuration is missing");
                return "申し訳ありません。エージェント設定がありません。";
            }

            _logger.LogInformation("Calling Foundry agent via direct HTTP with managed identity: {Endpoint}", endpoint);

            // Get token directly for https://ai.azure.com scope
            var tokenRequestContext = new TokenRequestContext(new[] { "https://ai.azure.com/.default" });
            var token = _credential.GetToken(tokenRequestContext, default);

            // Create HTTP client with Bearer token
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "KabeuchiAI/v0.0.3");

            // Call direct HTTP endpoint
            var url = $"{endpoint}/ai/agents/{agentName}/messages";
            var requestBody = new { message };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(url, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Agent response received: {Response}", responseContent);
                
                // Try to extract text from response
                using var jsonDoc = JsonDocument.Parse(responseContent);
                var root = jsonDoc.RootElement;
                
                if (root.TryGetProperty("content", out var contentElement) || 
                    root.TryGetProperty("message", out contentElement) ||
                    root.TryGetProperty("text", out contentElement))
                {
                    if (contentElement.ValueKind == JsonValueKind.String)
                    {
                        return contentElement.GetString() ?? responseContent;
                    }
                }
                
                return responseContent;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Agent API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return $"エージェントエラー: {response.StatusCode} - {errorContent}";
            }
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
