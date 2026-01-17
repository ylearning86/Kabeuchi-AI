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
    Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default);
}

public class FoundryChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FoundryChatService> _logger;
    private readonly TokenCredential _credential;
    private readonly IWebHostEnvironment _environment;

    private static readonly TimeSpan FoundryRequestTimeout = TimeSpan.FromSeconds(30);

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

    public async Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = _configuration["FoundryConfig:Endpoint"];
            var agentName = _configuration["FoundryConfig:AgentName"];
            var apiVersion = _configuration["FoundryConfig:ApiVersion"];

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(agentName))
            {
                _logger.LogError("Foundry endpoint or agent name configuration is missing");
                return "申し訳ありません。エージェント設定がありません。";
            }

            if (string.IsNullOrWhiteSpace(apiVersion))
            {
                // Default to a current Azure AI Foundry Agents API version.
                apiVersion = "2025-11-15-preview";
            }

            // Try configured version first, then fall back to other known/suspected versions.
            // (We keep this list small and deterministic for troubleshooting.)
            var apiVersionsToTry = new List<string>
            {
                apiVersion,
                "2025-11-15-preview",
                "2025-04-01-preview",
                "preview",
            };

            apiVersionsToTry = apiVersionsToTry
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("Calling Foundry agent via OpenAI-compatible responses API: {Endpoint}", endpoint);

            // Get token directly for https://ai.azure.com scope
            var tokenRequestContext = new TokenRequestContext(new[] { "https://ai.azure.com/.default" });
            var token = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);

            var endpointBase = endpoint.TrimEnd('/');

            // Use OpenAI-compatible responses API endpoint (requires api-version)
            // Format: {PROJECT_ENDPOINT}/openai/responses?api-version=YYYY-MM-DD
            var requestBody = new
            {
                input = message,
                agent = new
                {
                    name = agentName,
                    type = "agent_reference"
                }
            };
            
            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            HttpResponseMessage? response = null;
            string? lastErrorContent = null;
            string? usedApiVersion = null;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FoundryRequestTimeout);

            foreach (var candidateApiVersion in apiVersionsToTry)
            {
                usedApiVersion = candidateApiVersion;
                var url = $"{endpointBase}/openai/responses?api-version={Uri.EscapeDataString(candidateApiVersion)}";

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
                request.Headers.UserAgent.ParseAdd("KabeuchiAI/v0.0.12");

                _logger.LogInformation("POST {Url}", url);

                response = await _httpClient.SendAsync(request, timeoutCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    break;
                }

                lastErrorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Foundry call failed (api-version={ApiVersion}) Status={StatusCode} Body={Body}", candidateApiVersion, response.StatusCode, lastErrorContent);

                // Only retry on version-related errors.
                if (lastErrorContent is null ||
                    (!lastErrorContent.Contains("API version not supported", StringComparison.OrdinalIgnoreCase) &&
                     !lastErrorContent.Contains("Missing required query parameter: api-version", StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }
            }

            if (response is null)
            {
                return "エージェントエラー: リクエストに失敗しました。";
            }
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Agent response received (api-version={ApiVersion}): {Response}", usedApiVersion, responseContent);
                
                // Try to extract text from response
                try
                {
                    using var jsonDoc = JsonDocument.Parse(responseContent);
                    var root = jsonDoc.RootElement;
                    
                    // Check for output_text field (Foundry API format)
                    if (root.TryGetProperty("output_text", out var outputTextElement))
                    {
                        var outputText = outputTextElement.GetString();
                        if (!string.IsNullOrEmpty(outputText))
                            return outputText;
                    }
                    
                    // Check for choices array (OpenAI format)
                    if (root.TryGetProperty("choices", out var choicesElement) && 
                        choicesElement.ValueKind == JsonValueKind.Array &&
                        choicesElement.GetArrayLength() > 0)
                    {
                        var firstChoice = choicesElement[0];
                        if (firstChoice.TryGetProperty("message", out var messageElement))
                        {
                            if (messageElement.TryGetProperty("content", out var contentElement))
                            {
                                var contentText = contentElement.GetString();
                                if (!string.IsNullOrEmpty(contentText))
                                    return contentText;
                            }
                        }
                    }
                    
                    // Fallback: try multiple field names
                    if (root.TryGetProperty("output", out var outputElement))
                    {
                        if (outputElement.ValueKind == JsonValueKind.String)
                            return outputElement.GetString() ?? responseContent;
                        if (outputElement.TryGetProperty("text", out var textElement))
                            return textElement.GetString() ?? responseContent;
                    }
                    
                    if (root.TryGetProperty("text", out var textEl))
                    {
                        return textEl.GetString() ?? responseContent;
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
                var errorContent = lastErrorContent ?? await response.Content.ReadAsStringAsync();
                _logger.LogError("Agent API error (api-version={ApiVersion}): {StatusCode} - {Error}", usedApiVersion, response.StatusCode, errorContent);
                return $"エージェントエラー(api-version={usedApiVersion}): {response.StatusCode} - {errorContent}";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Chat request was cancelled by the client.");
            return "キャンセルされました。";
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Foundry call timed out after {TimeoutSeconds}s", FoundryRequestTimeout.TotalSeconds);
            return $"エージェントがタイムアウトしました（{(int)FoundryRequestTimeout.TotalSeconds}秒）。";
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
