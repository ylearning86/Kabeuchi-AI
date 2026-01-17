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

    public FoundryChatService(HttpClient httpClient, IConfiguration configuration, ILogger<FoundryChatService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        // マネージドIDで認証
        _credential = new DefaultAzureCredential();
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

            // Foundry APIにメッセージを送信
            var url = $"{endpoint}/agents/{agentName}/run?api-version=2024-05-01-preview";
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

            _logger.LogInformation($"Calling URL: {url}");

            var response = await _httpClient.SendAsync(request);
            
            _logger.LogInformation($"Response status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Foundry API error: {response.StatusCode} - {errorContent}");
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
