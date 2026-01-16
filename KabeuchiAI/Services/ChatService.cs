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

            // Foundry APIにメッセージを送信
            var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/agents/{agentName}/run")
            {
                Content = JsonContent.Create(new
                {
                    userInput = message,
                    sessionId = Guid.NewGuid().ToString()
                })
            };

            request.Headers.Add("api-key", apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var jsonDocument = JsonDocument.Parse(jsonResponse);
            var root = jsonDocument.RootElement;

            // レスポンスから応答テキストを抽出
            if (root.TryGetProperty("output", out var output))
            {
                return output.GetString() ?? "応答を処理できませんでした。";
            }

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
