using System.Text.Json;

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

    public FoundryChatService(HttpClient httpClient, IConfiguration configuration, ILogger<FoundryChatService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> SendMessageAsync(string message)
    {
        try
        {
            var endpoint = _configuration["FoundryConfig:Endpoint"];
            var apiKey = _configuration["FoundryConfig:ApiKey"];
            var agentName = _configuration["FoundryConfig:AgentName"];

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("Foundry configuration is missing");
                return "申し訳ありません。エージェント設定がありません。";
            }

            _logger.LogInformation($"Calling Foundry API: {endpoint}/agents/{agentName}/run");

            // Foundry APIにメッセージを送信
            var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/agents/{agentName}/run")
            {
                Content = new StringContent(
                    $$"""{"userInput":"{{message}}", "sessionId":"{{Guid.NewGuid()}}"}""",
                    System.Text.Encoding.UTF8,
                    "application/json"
                )
            };

            request.Headers.Add("api-key", apiKey);
            request.Headers.Add("Accept", "application/json");

            _logger.LogInformation($"API Key configured: {!string.IsNullOrEmpty(apiKey)}");

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

            return jsonResponse;
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
