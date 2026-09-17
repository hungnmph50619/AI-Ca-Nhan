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
builder.Services.AddSingleton<IKnowledgeDocumentStore, SqliteKnowledgeDocumentStore>();
builder.Services.AddSingleton<IKnowledgeGroundingService, KnowledgeGroundingService>();
builder.Services.AddSingleton<KnowledgeSourceReader>();
builder.Services.AddSingleton<IPersonalMemoryStore, PersonalMemoryStore>();
builder.Services.AddSingleton<IPersonalMemoryGroundingService, PersonalMemoryGroundingService>();
builder.Services.AddHttpClient<GeminiChatService>(client => { client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/"); client.Timeout = TimeSpan.FromSeconds(90); });
builder.Services.AddHttpClient<OpenAiChatService>(client => { client.BaseAddress = new Uri("https://api.openai.com/v1/"); client.Timeout = TimeSpan.FromSeconds(90); });
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
    return Results.Ok(new { configured = aiProvider.IsConfigured, provider = aiProvider.Name, model = aiProvider.Model, teamProfile = teamProfiles.DefaultProfileId, version = "0.6.3" });
});

app.MapGet("/api/knowledge/documents", async (IKnowledgeDocumentStore knowledgeStore, CancellationToken cancellationToken) => Results.Ok(await knowledgeStore.GetAllAsync(cancellationToken)));

app.MapGet("/api/knowledge/search", async (string? query, int? limit, IKnowledgeDocumentStore knowledgeStore, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await knowledgeStore.SearchAsync(query ?? string.Empty, limit ?? 5, cancellationToken)); }
    catch (KnowledgeDocumentValidationException exception) { return Results.BadRequest(new ApiError(exception.Message)); }
});

app.MapGet("/api/knowledge/documents/{documentId:guid}/chunks/{chunkIndex:int}", async (Guid documentId, int chunkIndex, KnowledgeSourceReader sourceReader, CancellationToken cancellationToken) =>
{
    var source = await sourceReader.GetChunkAsync(documentId, chunkIndex, cancellationToken);
    return source is null ? Results.NotFound() : Results.Ok(source);
});

app.MapPost("/api/knowledge/documents", async (HttpRequest request, IKnowledgeDocumentStore knowledgeStore, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new ApiError("Yêu cầu tải tệp không hợp lệ."));
    try
    {
        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || form.Files.Count != 1) return Results.BadRequest(new ApiError("Hãy chọn đúng một tệp để tải lên."));
        var document = await knowledgeStore.AddAsync(file, cancellationToken);
        return Results.Created($"/api/knowledge/documents/{document.Id}", document);
    }
    catch (InvalidDataException) { return Results.Json(new ApiError("Tệp tải lên vượt quá giới hạn 10 MB hoặc không hợp lệ."), statusCode: StatusCodes.Status413PayloadTooLarge); }
    catch (KnowledgeDocumentValidationException exception) { return Results.BadRequest(new ApiError(exception.Message)); }
    catch (DuplicateKnowledgeDocumentException exception) { return Results.Json(new ApiError(exception.Message), statusCode: StatusCodes.Status409Conflict); }
});

app.MapDelete("/api/knowledge/documents/{documentId:guid}", async (Guid documentId, IKnowledgeDocumentStore knowledgeStore, CancellationToken cancellationToken) =>
{
    var deleted = await knowledgeStore.DeleteAsync(documentId, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/memory", async (IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) => Results.Ok(await memoryStore.GetAllAsync(cancellationToken)));

app.MapPost("/api/memory", async (CreatePersonalMemoryRequest request, IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
{
    try
    {
        var memory = await memoryStore.AddAsync(request, cancellationToken);
        return Results.Created($"/api/memory/{memory.Id}", memory);
    }
    catch (ArgumentException exception) { return Results.BadRequest(new ApiError(exception.Message)); }
});

app.MapPut("/api/memory/{memoryId:guid}", async (Guid memoryId, UpdatePersonalMemoryRequest request, IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
{
    try
    {
        var memory = await memoryStore.UpdateAsync(memoryId, request, cancellationToken);
        return memory is null ? Results.NotFound() : Results.Ok(memory);
    }
    catch (DuplicatePersonalMemoryException exception) { return Results.Json(new ApiError(exception.Message), statusCode: StatusCodes.Status409Conflict); }
    catch (ArgumentException exception) { return Results.BadRequest(new ApiError(exception.Message)); }
});

app.MapDelete("/api/memory/{memoryId:guid}", async (Guid memoryId, IPersonalMemoryStore memoryStore, CancellationToken cancellationToken) =>
{
    var deleted = await memoryStore.DeleteAsync(memoryId, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapGet("/api/settings/ai", async (IAiSettingsStore settingsStore, CancellationToken cancellationToken) => Results.Ok(await settingsStore.GetAsync(cancellationToken)));

app.MapPut("/api/settings/ai", async (UpdateAiSettingsRequest request, IAiSettingsStore settingsStore, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await settingsStore.UpdateAsync(request, cancellationToken)); }
    catch (ArgumentException exception) { return Results.BadRequest(new ApiError(exception.Message)); }
});

app.MapPost("/api/chat", async (ChatRequest request, IAiProviderResolver providerResolver, IKnowledgeGroundingService groundingService, IPersonalMemoryGroundingService memoryGroundingService, CancellationToken cancellationToken) =>
{
    if (request.Messages is null || request.Messages.Count == 0)
    {
        return Results.BadRequest(new ApiError("Tin nhắn không được để trống."));
    }

    var provider = providerResolver.GetActive();
    if (!provider.IsConfigured)
    {
        return Results.BadRequest(new ApiError($"Nhà cung cấp {provider.Name} chưa được cấu hình."));
    }

    try
    {
        var knowledgeGrounding = await groundingService.GroundAsync(request.Messages, cancellationToken);
        var memoryGrounding = await memoryGroundingService.GroundAsync(request.Messages, knowledgeGrounding.Messages, cancellationToken);
        var response = await provider.ChatAsync(memoryGrounding.Messages, cancellationToken);
        return Results.Ok(new ChatResponse(response, knowledgeGrounding.Sources));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
    }
    catch (HttpRequestException exception)
    {
        return Results.Json(new ApiError($"Không thể kết nối tới dịch vụ AI: {exception.Message}"), statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception exception)
    {
        return Results.Json(new ApiError($"Đã xảy ra lỗi khi xử lý yêu cầu: {exception.Message}"), statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/team-profiles", (ITeamProfileCatalog teamProfiles) => Results.Ok(teamProfiles.GetAll()));

app.Run();

public sealed record ApiError(string Error);
