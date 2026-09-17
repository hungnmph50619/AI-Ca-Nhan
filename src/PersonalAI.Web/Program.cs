using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using PersonalAI.Web.Models;
using PersonalAI.Web.Options;
using PersonalAI.Web.Services;
using PersonalAI.Web.Teams;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = SqliteKnowledgeDocumentStore.MaximumFileSize + (64 * 1024));
builder.Services.AddDataProtection().SetApplicationName("PersonalAI");
builder.Services.AddSingleton<IAiSettingsStore, AiSettingsStore>();
builder.Services.AddSingleton<KnowledgeDocumentExtractor>();
builder.Services.AddSingleton<IKnowledgeDocumentStore, SqliteKnowledgeDocumentStore>();
builder.Services.AddSingleton<IKnowledgeGroundingService, KnowledgeGroundingService>();
builder.Services.AddSingleton<KnowledgeSourceReader>();
builder.Services.AddSingleton<IPersonalMemoryStore, PersonalMemoryStore>();
builder.Services.AddSingleton<IPersonalMemoryGroundingService, PersonalMemoryGroundingService>();
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

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path is "/" or "/index.html" || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (IAiProviderResolver providerResolver, ITeamProfileCatalog teamProfiles) =>
{
    var aiProvider = providerResolver.GetActive();
    return Results.Ok(new
    {
        configured = aiProvider.IsConfigured,
        provider = aiProvider.Name,
        model = aiProvider.Model,
        teamProfile = teamProfiles.DefaultProfileId,
        version = "0.7.1"
    });
});

app.MapGet("/api/knowledge/documents", async (IKnowledgeDocumentStore knowledgeStore, CancellationToken cancellationToken) =>
    Results.Ok(await knowledgeStore.GetAllAsync(cancellationToken)));

app.MapGet("/api/knowledge/search", async (string? query, int? limit, IKnowledgeDocumentStore knowledgeStore, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await knowledgeStore.SearchAsync(query ?? string.Empty, limit ?? 5, cancellationToken));
    }
    catch (KnowledgeDocumentValidationException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
});

app.MapGet("/api/knowledge/documents/{documentId:guid}/chunks/{chunkIndex:int}", async (Guid documentId, int chunkIndex, KnowledgeSourceReader sourceReader, CancellationToken cancellationToken) =>
{
    var source = await sourceReader.GetChunkAsync(documentId, chunkIndex, cancellationToken);
    return source is null ? Results.NotFound() : Results.Ok(source);
});

app.MapPost("/api/knowledge/documents", async (HttpRequest request, IKnowledgeDocumentStore knowledgeStore, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new ApiError("Yêu cầu tải tệp không hợp lệ."));

    try
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || form.Files.Count != 1)
            return Results.BadRequest(new ApiError("Hãy chọn đúng một tệp để tải lên."));

        var document = await knowledgeStore.AddAsync(file, cancellationToken);
        return Results.Created($"/api/knowledge/documents/{document.Id}", document);
    }
    catch (InvalidDataException)
    {
        return Results.Json(new ApiError("Tệp tải lên vượt quá giới hạn 10 MB hoặc không hợp lệ."), statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    catch (KnowledgeDocumentValidationException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
    catch (DuplicateKnowledgeDocumentException exception)
    {
        return Results.Json(new ApiError(exception.Message), statusCode: StatusCodes.Status409Conflict);
    }
});

app.MapDelete("/api/knowledge/documents/{documentId:guid}", async (Guid documentId, IKnowledgeDocumentStore knowledgeStore, CancellationToken cancellationToken) =>
{
    var deleted = await knowledgeStore.DeleteAsync(documentId, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/memory", async (IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
    Results.Ok(await memoryStore.GetAllAsync(cancellationToken)));

app.MapGet("/api/memory/stats", async (IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
    Results.Ok(await memoryStore.GetStatsAsync(cancellationToken)));

app.MapGet("/api/memory/export", async (IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
    Results.Ok(new PersonalMemoryExport(
        2,
        DateTimeOffset.UtcNow,
        await memoryStore.GetAllAsync(cancellationToken))));

app.MapPost("/api/memory/import", async (ImportPersonalMemoriesRequest request, IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await memoryStore.ImportAsync(request, cancellationToken));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
});

app.MapPost("/api/memory", async (CreatePersonalMemoryRequest request, IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
{
    try
    {
        var memory = await memoryStore.AddAsync(request, cancellationToken);
        return Results.Created($"/api/memory/{memory.Id}", memory);
    }
    catch (MemoryUpdateSuggestionException exception)
    {
        return Results.Json(new
        {
            error = exception.Message,
            suggestion = true,
            candidateMemoryId = exception.CandidateMemoryId,
            candidateContent = exception.CandidateContent
        }, statusCode: StatusCodes.Status409Conflict);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
});

app.MapPut("/api/memory/{memoryId:guid}", async (Guid memoryId, UpdatePersonalMemoryRequest request, IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
{
    try
    {
        var memory = await memoryStore.UpdateAsync(memoryId, request, cancellationToken);
        return memory is null ? Results.NotFound() : Results.Ok(memory);
    }
    catch (MemoryOverwriteConfirmationException exception)
    {
        return Results.Json(new { error = exception.Message, duplicateMemoryId = exception.DuplicateMemoryId }, statusCode: StatusCodes.Status409Conflict);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
});

app.MapPatch("/api/memory/{memoryId:guid}/enabled", async (Guid memoryId, SetPersonalMemoryEnabledRequest request, IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
{
    var memory = await memoryStore.SetEnabledAsync(memoryId, request.IsEnabled, cancellationToken);
    return memory is null ? Results.NotFound() : Results.Ok(memory);
});

app.MapDelete("/api/memory/{memoryId:guid}", async (Guid memoryId, IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
{
    var deleted = await memoryStore.DeleteAsync(memoryId, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapDelete("/api/memory", async (IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
    Results.Ok(new { deleted = await memoryStore.DeleteAllAsync(cancellationToken) }));

app.MapGet("/api/settings/ai", (IAiSettingsStore settingsStore) =>
    Results.Ok(settingsStore.GetPublicSettings()));

app.MapPost("/api/settings/ai", async (UpdateAiSettingsRequest request, IAiSettingsStore settingsStore, CancellationToken cancellationToken) =>
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

app.MapPost("/api/settings/ai/test", async (IAiProviderResolver providerResolver, CancellationToken cancellationToken) =>
{
    try
    {
        var provider = providerResolver.GetActive();
        await provider.ReplyAsync([new ChatMessage("user", "Chỉ trả lời đúng một từ: OK")], cancellationToken);
        return Results.Ok(new AiConnectionTestResponse(true, "Kết nối AI thành công."));
    }
    catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException)
    {
        return Results.Json(new AiConnectionTestResponse(false, exception.Message), statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/team-profiles", (ITeamProfileCatalog teamProfiles) =>
    Results.Ok(new { active = teamProfiles.DefaultProfileId, profiles = teamProfiles.GetAll() }));

app.MapPost("/api/chat", async (ChatRequest request, IAiProviderResolver providerResolver, IKnowledgeGroundingService groundingService, IPersonalMemoryGroundingService memoryGroundingService, CancellationToken cancellationToken) =>
{
    if (request.Messages is null || request.Messages.Count == 0)
        return Results.BadRequest(new ApiError("Hãy nhập một câu hỏi."));

    if (request.Messages.Count > 40)
        return Results.BadRequest(new ApiError("Cuộc trò chuyện quá dài. Hãy tạo cuộc trò chuyện mới."));

    var allowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "user", "assistant" };
    if (request.Messages.Any(message => !allowedRoles.Contains(message.Role) || string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 12_000))
        return Results.BadRequest(new ApiError("Nội dung hội thoại không hợp lệ."));

    var knowledgeMode = string.IsNullOrWhiteSpace(request.KnowledgeMode) ? "normal" : request.KnowledgeMode.Trim().ToLowerInvariant();
    if (knowledgeMode is not ("normal" or "documents-only"))
        return Results.BadRequest(new ApiError("Chế độ trả lời theo dữ liệu không hợp lệ."));

    if (!request.UseKnowledge && knowledgeMode == "documents-only")
        return Results.BadRequest(new ApiError("Hãy bật dữ liệu riêng để sử dụng chế độ chỉ trả lời theo tài liệu."));

    try
    {
        var aiProvider = providerResolver.GetActive();
        var grounded = request.UseKnowledge
            ? await groundingService.GroundAsync(request.Messages, knowledgeMode, cancellationToken)
            : new KnowledgeGroundingResult(request.Messages, []);

        if (knowledgeMode == "documents-only" && grounded.Sources.Count == 0)
        {
            return Results.Ok(new ChatResponse("Tôi chưa tìm thấy thông tin này trong kho dữ liệu.", aiProvider.Model, aiProvider.Name, []));
        }

        var chatMessages = grounded.Messages;
        if (request.UseMemory && knowledgeMode == "normal")
        {
            var memoryGrounded = await memoryGroundingService.GroundAsync(request.Messages, chatMessages, cancellationToken);
            chatMessages = memoryGrounded.Messages;
        }

        var answer = await aiProvider.ReplyAsync(chatMessages, cancellationToken);
        return Results.Ok(new ChatResponse(answer, aiProvider.Model, aiProvider.Name, grounded.Sources));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Json(new ApiError(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException exception)
    {
        var statusCode = exception.StatusCode == HttpStatusCode.TooManyRequests ? StatusCodes.Status429TooManyRequests : StatusCodes.Status502BadGateway;
        return Results.Json(new ApiError(exception.Message), statusCode: statusCode);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Json(new ApiError("Yêu cầu AI mất quá nhiều thời gian. Hãy thử lại."), statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapFallbackToFile("index.html");
app.Run();
