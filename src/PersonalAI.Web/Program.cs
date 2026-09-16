using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using PersonalAI.Web.Models;
using PersonalAI.Web.Options;
using PersonalAI.Web.Services;
using PersonalAI.Web.Teams;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AiOptions>(
    builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<GeminiOptions>(
    builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<OpenAiOptions>(
    builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = SqliteKnowledgeDocumentStore.MaximumFileSize + (64 * 1024));
builder.Services.AddDataProtection().SetApplicationName("PersonalAI");
builder.Services.AddSingleton<IAiSettingsStore, AiSettingsStore>();
builder.Services.AddSingleton<IKnowledgeDocumentStore, SqliteKnowledgeDocumentStore>();
builder.Services.AddSingleton<IKnowledgeGroundingService, KnowledgeGroundingService>();
builder.Services.AddHttpClient<GeminiChatService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddHttpClient<OpenAiChatService>(client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddScoped<IAiProviderResolver, AiProviderResolver>();
builder.Services.AddSingleton<ITeamProfileCatalog, TeamProfileCatalog>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (
    IAiProviderResolver providerResolver,
    ITeamProfileCatalog teamProfiles) =>
{
    var aiProvider = providerResolver.GetActive();
    return Results.Ok(new
    {
        configured = aiProvider.IsConfigured,
        provider = aiProvider.Name,
        model = aiProvider.Model,
        teamProfile = teamProfiles.DefaultProfileId,
        version = "0.5.3"
    });
});

app.MapGet("/api/knowledge/documents", async (
    IKnowledgeDocumentStore knowledgeStore,
    CancellationToken cancellationToken) =>
{
    var documents = await knowledgeStore.GetAllAsync(cancellationToken);
    return Results.Ok(documents);
});

app.MapGet("/api/knowledge/search", async (
    string? query,
    int? limit,
    IKnowledgeDocumentStore knowledgeStore,
    CancellationToken cancellationToken) =>
{
    try
    {
        var results = await knowledgeStore.SearchAsync(
            query ?? string.Empty,
            limit ?? 5,
            cancellationToken);
        return Results.Ok(results);
    }
    catch (KnowledgeDocumentValidationException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
});

app.MapPost("/api/knowledge/documents", async (
    HttpRequest request,
    IKnowledgeDocumentStore knowledgeStore,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new ApiError("Yêu cầu tải tệp không hợp lệ."));
    }

    try
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || form.Files.Count != 1)
        {
            return Results.BadRequest(new ApiError("Hãy chọn đúng một tệp để tải lên."));
        }

        var document = await knowledgeStore.AddAsync(file, cancellationToken);
        return Results.Created($"/api/knowledge/documents/{document.Id}", document);
    }
    catch (InvalidDataException)
    {
        return Results.Json(
            new ApiError("Tệp tải lên vượt quá giới hạn 10 MB hoặc không hợp lệ."),
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    catch (KnowledgeDocumentValidationException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
    catch (DuplicateKnowledgeDocumentException exception)
    {
        return Results.Json(
            new ApiError(exception.Message),
            statusCode: StatusCodes.Status409Conflict);
    }
});

app.MapDelete("/api/knowledge/documents/{documentId:guid}", async (
    Guid documentId,
    IKnowledgeDocumentStore knowledgeStore,
    CancellationToken cancellationToken) =>
{
    var deleted = await knowledgeStore.DeleteAsync(documentId, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/settings/ai", (IAiSettingsStore settingsStore) =>
    Results.Ok(settingsStore.GetPublicSettings()));

app.MapPost("/api/settings/ai", async (
    UpdateAiSettingsRequest request,
    IAiSettingsStore settingsStore,
    CancellationToken cancellationToken) =>
{
    try
    {
        await settingsStore.SaveAsync(request, cancellationToken);
        return Results.Ok(settingsStore.GetPublicSettings());
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
});

app.MapPost("/api/settings/ai/test", async (
    IAiProviderResolver providerResolver,
    CancellationToken cancellationToken) =>
{
    try
    {
        var provider = providerResolver.GetActive();
        await provider.ReplyAsync(
            [new ChatMessage("user", "Chỉ trả lời đúng một từ: OK")],
            cancellationToken);
        return Results.Ok(new AiConnectionTestResponse(true, "Kết nối AI thành công."));
    }
    catch (Exception exception) when (
        exception is InvalidOperationException or HttpRequestException or TaskCanceledException)
    {
        return Results.Json(
            new AiConnectionTestResponse(false, exception.Message),
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/team-profiles", (ITeamProfileCatalog teamProfiles) =>
    Results.Ok(new
    {
        active = teamProfiles.DefaultProfileId,
        profiles = teamProfiles.GetAll()
    }));

app.MapPost("/api/chat", async (
    ChatRequest request,
    IAiProviderResolver providerResolver,
    IKnowledgeGroundingService groundingService,
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
        var aiProvider = providerResolver.GetActive();
        var grounded = await groundingService.GroundAsync(request.Messages, cancellationToken);
        var answer = await aiProvider.ReplyAsync(grounded.Messages, cancellationToken);
        return Results.Ok(new ChatResponse(
            answer,
            aiProvider.Model,
            aiProvider.Name,
            grounded.Sources));
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
