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

            _logger.LogInformation("Calling Foundry agent via HTTP REST API with managed identity: {Endpoint}", endpoint);

            // Get token directly for https://ai.azure.com scope
            var tokenRequestContext = new TokenRequestContext(new[] { "https://ai.azure.com/.default" });
            var token = _credential.GetToken(tokenRequestContext, default);

            // Create HTTP client with Bearer token
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            client.DefaultRequestHeaders.Add("User-Agent", "KabeuchiAI/v0.0.6");

            // Call REST API endpoint with correct api-version
            // Format: {endpoint}/agent-service/agents/{agentName}/messages?api-version=2024-12-01-preview
            var url = $"{endpoint}/agent-service/agents/{agentName}/messages?api-version=2024-12-01-preview";
            var requestBody = JsonSerializer.Serialize(new { message });
            var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            
            var response = await client.PostAsync(url, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Agent response received: {Response}", responseContent);
                
                // Try to extract text from response
                try
                {
                    using var jsonDoc = JsonDocument.Parse(responseContent);
                    var root = jsonDoc.RootElement;
                    
                    // Try multiple possible field names
                    if (root.TryGetProperty("output", out var outputElement))
                    {
                        if (outputElement.ValueKind == JsonValueKind.String)
                            return outputElement.GetString() ?? responseContent;
                        if (outputElement.TryGetProperty("text", out var textElement))
                            return textElement.GetString() ?? responseContent;
                    }
                    
                    if (root.TryGetProperty("text", out var textEl))
                    {
                        if (textEl.ValueKind == JsonValueKind.String)
                            return textEl.GetString() ?? responseContent;
                    }
                    
                    if (root.TryGetProperty("content", out var contentEl))
                    {
                        if (contentEl.ValueKind == JsonValueKind.String)
                            return contentEl.GetString() ?? responseContent;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to parse JSON response: {Error}", ex.Message);
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
