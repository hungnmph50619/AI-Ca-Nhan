using System.Net;
using Microsoft.AspNetCore.Http.Json;
using PersonalAI.Web.Models;
using PersonalAI.Web.Options;
using PersonalAI.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddHttpClient<IChatService, OpenAiChatService>(client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(90);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (IChatService chatService) => Results.Ok(new
{
    configured = chatService.IsConfigured,
    model = chatService.Model,
    version = "0.1.0"
}));

app.MapPost("/api/chat", async (
    ChatRequest request,
    IChatService chatService,
    CancellationToken cancellationToken) =>
{
    if (request.Messages is null || request.Messages.Count == 0)
    {
        return Results.BadRequest(new ApiError("Hãy nhập một câu hỏi."));
    }

    if (request.Messages.Count > 40)
    {
        return Results.BadRequest(new ApiError(
            "Cuộc trò chuyện quá dài. Hãy tạo cuộc trò chuyện mới."));
    }

    var allowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "user",
        "assistant"
    };

    if (request.Messages.Any(message =>
            !allowedRoles.Contains(message.Role)
            || string.IsNullOrWhiteSpace(message.Content)
            || message.Content.Length > 12_000))
    {
        return Results.BadRequest(new ApiError("Nội dung hội thoại không hợp lệ."));
    }

    try
    {
        var answer = await chatService.ReplyAsync(request.Messages, cancellationToken);
        return Results.Ok(new ChatResponse(answer, chatService.Model));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Json(
            new ApiError(exception.Message),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException exception)
    {
        var statusCode = exception.StatusCode == HttpStatusCode.TooManyRequests
            ? StatusCodes.Status429TooManyRequests
            : StatusCodes.Status502BadGateway;
        return Results.Json(new ApiError(exception.Message), statusCode: statusCode);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Json(
            new ApiError("Yêu cầu AI mất quá nhiều thời gian. Hãy thử lại."),
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapFallbackToFile("index.html");

app.Run();
