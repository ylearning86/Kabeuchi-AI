using System.Text.Json;
using Azure.Core;
using Azure.Identity;

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

            _logger.LogInformation($"Calling Foundry API with managed identity: {endpoint}");

            // マネージドIDを使用してアクセストークンを取得
            var tokenRequestContext = new TokenRequestContext(new[] { "https://ai.azure.com/.default" });
            var token = await _credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);
            // サポートされている可能性のある URL パターンを試す
            var urlPatterns = new[] {
                // パターン1: /agents/{name}/run?api-version=...
                $"{endpoint}/agents/{agentName}/run?api-version={{0}}",
                // パターン2: シンプル版（バージョンなし）
                $"{endpoint}/agents/{agentName}/run"
            };
            
            // サポートされている可能性のある API バージョンを試す（新旧プレビューも含め広めに網羅）
            var apiVersions = new[] {
                "2025-07-01-preview", "2025-04-01-preview", "2025-02-15-preview",
                "2024-12-01-preview", "2024-10-01-preview", "2024-09-01-preview", "2024-08-01-preview",
                "2024-07-01-preview", "2024-06-01-preview", "2024-05-01-preview", "2024-02-15-preview",
                "2024-10-01", "2024-09-01", "2024-08-01", "2024-07-01", "2024-06-01", "2024-05-01"
            };
            
            // URL パターンとバージョンを組み合わせて試す
            foreach (var pattern in urlPatterns)
            {
                if (pattern.Contains("{0}"))
                {
                    // バージョンパラメータが必要
                    foreach (var apiVersion in apiVersions)
                    {
                        var url = string.Format(pattern, apiVersion);
            
                        var request = new HttpRequestMessage(HttpMethod.Post, url)
                        {
                            Content = new StringContent(
                                $$"""{"userInput":"{{message}}", "sessionId":"{{Guid.NewGuid()}}"}""",
                                System.Text.Encoding.UTF8,
                                "application/json"
                            )
                        };

                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
                        request.Headers.Add("Accept", "application/json");

                        _logger.LogInformation($"Trying URL with api-version={apiVersion}: {url}");

                        var response = await _httpClient.SendAsync(request);
                        
                        _logger.LogInformation($"Response status: {response.StatusCode} for api-version={apiVersion}");

                        if (response.IsSuccessStatusCode)
                        {
                            var jsonResponse = await response.Content.ReadAsStringAsync();
                            _logger.LogInformation($"Success with api-version={apiVersion}!");
                            
                            var jsonDocument = JsonDocument.Parse(jsonResponse);
                            var root = jsonDocument.RootElement;

                            if (root.TryGetProperty("output", out var output))
                            {
                                return output.GetString() ?? "応答を処理できませんでした。";
                            }

                            if (root.TryGetProperty("response", out var responseProperty))
                            {
                                return responseProperty.GetString() ?? "応答を処理できませんでした。";
                            }

                            return jsonResponse;
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            _logger.LogWarning($"API version {apiVersion} failed: {response.StatusCode} - {errorContent}");
                        }
                    }
                }
                else
                {
                    // バージョンパラメータなし（シンプル版）
                    var request = new HttpRequestMessage(HttpMethod.Post, pattern)
                    {
                        Content = new StringContent(
                            $$"""{"userInput":"{{message}}", "sessionId":"{{Guid.NewGuid()}}"}""",
                            System.Text.Encoding.UTF8,
                            "application/json"
                        )
                    };

                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
                    request.Headers.Add("Accept", "application/json");

                    _logger.LogInformation($"Trying URL (no api-version): {pattern}");

                    var response = await _httpClient.SendAsync(request);
                    
                    _logger.LogInformation($"Response status: {response.StatusCode}");

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation($"Success (no api-version)!");
                        
                        var jsonDocument = JsonDocument.Parse(jsonResponse);
                        var root = jsonDocument.RootElement;

                        if (root.TryGetProperty("output", out var output))
                        {
                            return output.GetString() ?? "応答を処理できませんでした。";
                        }

                        if (root.TryGetProperty("response", out var responseProperty))
                        {
                            return responseProperty.GetString() ?? "応答を処理できませんでした。";
                        }

                        return jsonResponse;
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning($"No api-version pattern failed: {response.StatusCode} - {errorContent}");
                    }
                }
            }
            
            foreach (var apiVersion in apiVersions)
            {
                var url = $"{endpoint}/agents/{agentName}/run?api-version={apiVersion}";
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(
                        $$"""{"userInput":"{{message}}", "sessionId":"{{Guid.NewGuid()}}"}""",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    )
                };

                // Bearer tokenを使用
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("User-Agent", "KabeuchiAI/1.0");

                _logger.LogInformation($"Trying API version {apiVersion}: {url}");

                var response = await _httpClient.SendAsync(request);
                
                _logger.LogInformation($"Response status: {response.StatusCode} for api-version={apiVersion}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning($"API version {apiVersion} failed: {response.StatusCode} - {errorContent}");
                    
                    // API version not supported/Bad request エラーの場合は次のバージョンを試す
                    if ((response.StatusCode == System.Net.HttpStatusCode.BadRequest || response.StatusCode == System.Net.HttpStatusCode.NotFound) 
                        && (errorContent.Contains("api-version") || errorContent.Contains("not supported")))
                    {
                        continue;
                    }
                    
                    // それ以外のエラーはここで返す
                    return $"エージェントエラー: {response.StatusCode} {errorContent}";
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Response: {jsonResponse}");

                var jsonDocument = JsonDocument.Parse(jsonResponse);
                var root = jsonDocument.RootElement;

                // レスポンスから応答テキストを抽出
                if (root.TryGetProperty("output", out var output))
                {
                    return output.GetString() ?? "応答を処理できませんでした。";
                }

                if (root.TryGetProperty("response", out var responseProperty))
                {
                    return responseProperty.GetString() ?? "応答を処理できませんでした。";
                }

                return jsonResponse;
            }

            return "申し訳ありません。サポートされているAPIバージョンが見つかりません。";
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
