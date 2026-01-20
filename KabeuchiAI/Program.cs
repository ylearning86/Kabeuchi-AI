var builder = WebApplication.CreateBuilder(args);

static string GetAppVersion()
{
    var asm = typeof(Program).Assembly;
    var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()
        ?.InformationalVersion;
    if (!string.IsNullOrWhiteSpace(info))
    {
        return info;
    }

    var v = asm.GetName().Version;
    return v?.ToString() ?? "unknown";
}

var appVersion = GetAppVersion();

// ポート設定（開発環境では既定で 5000、ただし --urls / ASPNETCORE_URLS を優先する）
var urlsConfigured = !string.IsNullOrWhiteSpace(builder.Configuration[Microsoft.AspNetCore.Hosting.WebHostDefaults.ServerUrlsKey])
    || !string.IsNullOrWhiteSpace(builder.Configuration["urls"]);
if (builder.Environment.IsDevelopment() && !urlsConfigured)
{
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.ListenLocalhost(5000);
    });
}

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Chat Service
builder.Services.AddHttpClient<KabeuchiAI.Services.IChatService, KabeuchiAI.Services.FoundryChatService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(35);
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable static files
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowAll");
}

// 開発環境で https ポート未設定の場合、警告が出るため本番のみ有効化
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Diagnostics endpoint (no secrets) to verify what the running app is configured to call.
app.MapGet("/api/diag", (IConfiguration config, IHostEnvironment env) =>
{
    var endpoint = (config["FoundryConfig:Endpoint"] ?? string.Empty).TrimEnd('/');
    var apiVersion = config["FoundryConfig:ApiVersion"];
    if (string.IsNullOrWhiteSpace(apiVersion))
    {
        apiVersion = "2025-11-15-preview";
    }

    var agentName = config["FoundryConfig:AgentName"] ?? string.Empty;
    var computedUrl = string.IsNullOrWhiteSpace(endpoint)
        ? string.Empty
        : $"{endpoint}/openai/responses?api-version={Uri.EscapeDataString(apiVersion)}";

    return Results.Ok(new
    {
        appVersion,
        environment = env.EnvironmentName,
        foundry = new
        {
            endpoint,
            apiVersion,
            agentName,
            computedUrl,
        }
    });
})
.WithName("Diagnostics")
.WithOpenApi();

// Chat API endpoint
app.MapPost("/api/chat", async (ChatRequest request, KabeuchiAI.Services.IChatService chatService, HttpContext http) =>
{
    var message = request.Message?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(message))
        return Results.BadRequest("メッセージが空です");

    if (message.Length > 4000)
        return Results.BadRequest("メッセージが長すぎます（最大 4000 文字）");

    var result = await chatService.SendMessageAsync(message, http.RequestAborted);
    return Results.Ok(new ChatResponse
    {
        Response = result.Text,
        Meta = new ChatResponseMeta
        {
            Model = result.Model,
            ToolsUsed = result.ToolsUsed.ToArray(),
        }
    });
})
.WithName("SendChat")
.WithOpenApi();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC * 1.8);
}

// Chat Models
public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
}

public class ChatResponse
{
    public string Response { get; set; } = string.Empty;

    public ChatResponseMeta? Meta { get; set; }
}

public class ChatResponseMeta
{
    public string? Model { get; set; }

    public string[] ToolsUsed { get; set; } = Array.Empty<string>();
}
