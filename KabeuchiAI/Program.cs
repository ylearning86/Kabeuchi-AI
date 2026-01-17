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
builder.Services.AddHttpClient<KabeuchiAI.Services.IChatService, KabeuchiAI.Services.FoundryChatService>();
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

// Chat API endpoint
app.MapPost("/api/chat", async (ChatRequest request, KabeuchiAI.Services.IChatService chatService) =>
{
    if (string.IsNullOrEmpty(request.Message))
        return Results.BadRequest("メッセージが空です");

    var response = await chatService.SendMessageAsync(request.Message);
    return Results.Ok(new ChatResponse { Response = response });
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
}
