var builder = WebApplication.CreateBuilder(args);

// ポート設定（開発環境では 5000、本番環境ではランダムポート）
var port = builder.Environment.IsDevelopment() ? 5000 : 0;
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenLocalhost(port);
});

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

app.UseCors("AllowAll");

app.UseHttpsRedirection();

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
        appVersion = "0.0.14",
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
    if (string.IsNullOrEmpty(request.Message))
        return Results.BadRequest("メッセージが空です");

    var result = await chatService.SendMessageAsync(request.Message, http.RequestAborted);
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
