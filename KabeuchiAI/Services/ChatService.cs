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
    Task<ChatServiceResult> SendMessageAsync(string message, CancellationToken cancellationToken = default);
}

public sealed record ChatServiceResult(
    string Text,
    string? Model,
    IReadOnlyList<string> ToolsUsed);

public class FoundryChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FoundryChatService> _logger;
    private readonly TokenCredential _credential;
    private readonly IWebHostEnvironment _environment;

    private static readonly TimeSpan FoundryRequestTimeout = TimeSpan.FromSeconds(30);

    private static string? TryExtractResponseText(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        using var jsonDoc = JsonDocument.Parse(responseContent);
        var root = jsonDoc.RootElement;

        // Some services may return a top-level output_text
        if (root.TryGetProperty("output_text", out var outputTextElement) && outputTextElement.ValueKind == JsonValueKind.String)
        {
            var outputText = outputTextElement.GetString();
            if (!string.IsNullOrWhiteSpace(outputText))
            {
                return outputText;
            }
        }

        // Azure OpenAI / Foundry Responses API commonly returns: output: [ { type: "message", content: [ { type: "output_text", text: "..." } ] } ]
        if (root.TryGetProperty("output", out var outputItems) && outputItems.ValueKind == JsonValueKind.Array)
        {
            var texts = new List<string>();

            foreach (var item in outputItems.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var contentItems) || contentItems.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in contentItems.EnumerateArray())
                {
                    string? partType = null;
                    if (part.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                    {
                        partType = typeElement.GetString();
                    }

                    if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        var text = textElement.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            texts.Add(text);
                        }
                    }
                    else if (string.Equals(partType, "output_text", StringComparison.OrdinalIgnoreCase) && part.TryGetProperty("output_text", out var nestedOutputText) && nestedOutputText.ValueKind == JsonValueKind.String)
                    {
                        var text = nestedOutputText.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            texts.Add(text);
                        }
                    }
                }
            }

            var joined = string.Join("\n", texts).Trim();
            if (!string.IsNullOrWhiteSpace(joined))
            {
                return joined;
            }
        }

        // Legacy/other formats
        if (root.TryGetProperty("choices", out var choicesElement) &&
            choicesElement.ValueKind == JsonValueKind.Array &&
            choicesElement.GetArrayLength() > 0)
        {
            var firstChoice = choicesElement[0];
            if (firstChoice.TryGetProperty("message", out var messageElement) &&
                messageElement.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind == JsonValueKind.String)
            {
                var contentText = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(contentText))
                {
                    return contentText;
                }
            }
        }

        return null;
    }

    private static (string? model, IReadOnlyList<string> toolsUsed) ExtractMetaFromResponse(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return (null, Array.Empty<string>());
        }

        using var jsonDoc = JsonDocument.Parse(responseContent);
        var root = jsonDoc.RootElement;

        string? model = null;
        if (root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String)
        {
            model = modelElement.GetString();
        }

        var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only infer tools from *output* (not from the request's "tools" list), to keep this "actual used".
        if (root.TryGetProperty("output", out var outputItems) && outputItems.ValueKind == JsonValueKind.Array)
        {
            void Walk(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        {
                            // Heuristic 1: explicit tool type/name fields
                            if (element.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                            {
                                var type = typeEl.GetString();
                                if (!string.IsNullOrWhiteSpace(type))
                                {
                                    // Common patterns: "tool_call", "file_search", "openapi", "function_call", "mcp"
                                    if (type.Equals("file_search", StringComparison.OrdinalIgnoreCase) ||
                                        type.Equals("openapi", StringComparison.OrdinalIgnoreCase) ||
                                        type.Equals("mcp", StringComparison.OrdinalIgnoreCase) ||
                                        type.Equals("tool_call", StringComparison.OrdinalIgnoreCase) ||
                                        type.Equals("function_call", StringComparison.OrdinalIgnoreCase) ||
                                        type.EndsWith("_call", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tools.Add(type);
                                    }
                                }
                            }

                            // Heuristic 2: OpenAPI tool often carries a name
                            if (element.TryGetProperty("openapi", out var openapiObj) && openapiObj.ValueKind == JsonValueKind.Object)
                            {
                                if (openapiObj.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                {
                                    var name = nameEl.GetString();
                                    if (!string.IsNullOrWhiteSpace(name))
                                    {
                                        tools.Add($"openapi:{name}");
                                    }
                                }
                            }

                            // Heuristic 3: function tool calls
                            if (element.TryGetProperty("function_name", out var fnEl) && fnEl.ValueKind == JsonValueKind.String)
                            {
                                var fn = fnEl.GetString();
                                if (!string.IsNullOrWhiteSpace(fn))
                                {
                                    tools.Add($"function:{fn}");
                                }
                            }

                            foreach (var prop in element.EnumerateObject())
                            {
                                Walk(prop.Value);
                            }

                            break;
                        }

                    case JsonValueKind.Array:
                        foreach (var item in element.EnumerateArray())
                        {
                            Walk(item);
                        }
                        break;
                }
            }

            Walk(outputItems);
        }

        return (model, tools.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToArray());
    }

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

    public async Task<ChatServiceResult> SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = _configuration["FoundryConfig:Endpoint"];
            var agentName = _configuration["FoundryConfig:AgentName"];
            var apiVersion = _configuration["FoundryConfig:ApiVersion"];

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(agentName))
            {
                _logger.LogError("Foundry endpoint or agent name configuration is missing");
                return new ChatServiceResult(
                    Text: "申し訳ありません。エージェント設定がありません。",
                    Model: null,
                    ToolsUsed: Array.Empty<string>());
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
                request.Headers.UserAgent.ParseAdd("KabeuchiAI/v0.0.13");

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
                return new ChatServiceResult(
                    Text: "エージェントエラー: リクエストに失敗しました。",
                    Model: null,
                    ToolsUsed: Array.Empty<string>());
            }
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Agent response received (api-version={ApiVersion}): {Response}", usedApiVersion, responseContent);

                var (model, toolsUsed) = ExtractMetaFromResponse(responseContent);
                
                // Try to extract text from response
                try
                {
                    var extracted = TryExtractResponseText(responseContent);
                    if (!string.IsNullOrWhiteSpace(extracted))
                    {
                        return new ChatServiceResult(
                            Text: extracted,
                            Model: model,
                            ToolsUsed: toolsUsed);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to parse JSON response: {Error}", ex.Message);
                }

                return new ChatServiceResult(
                    Text: responseContent,
                    Model: model,
                    ToolsUsed: toolsUsed);
            }
            else
            {
                var errorContent = lastErrorContent ?? await response.Content.ReadAsStringAsync();
                _logger.LogError("Agent API error (api-version={ApiVersion}): {StatusCode} - {Error}", usedApiVersion, response.StatusCode, errorContent);
                return new ChatServiceResult(
                    Text: $"エージェントエラー(api-version={usedApiVersion}): {response.StatusCode} - {errorContent}",
                    Model: null,
                    ToolsUsed: Array.Empty<string>());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Chat request was cancelled by the client.");
            return new ChatServiceResult(
                Text: "キャンセルされました。",
                Model: null,
                ToolsUsed: Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Foundry call timed out after {TimeoutSeconds}s", FoundryRequestTimeout.TotalSeconds);
            return new ChatServiceResult(
                Text: $"エージェントがタイムアウトしました（{(int)FoundryRequestTimeout.TotalSeconds}秒）。",
                Model: null,
                ToolsUsed: Array.Empty<string>());
        }
        catch (Azure.Identity.AuthenticationFailedException ex)
        {
            _logger.LogError("Azure authentication error: {Message}", ex.Message);
            return new ChatServiceResult(
                Text: $"認証エラー: {ex.Message}",
                Model: null,
                ToolsUsed: Array.Empty<string>());
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Foundry API error: {Message}", ex.Message);
            return new ChatServiceResult(
                Text: $"エージェントに接続できません: {ex.Message}",
                Model: null,
                ToolsUsed: Array.Empty<string>());
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error: {Message}", ex.Message);
            return new ChatServiceResult(
                Text: $"エラーが発生しました: {ex.Message}",
                Model: null,
                ToolsUsed: Array.Empty<string>());
        }
    }
}
