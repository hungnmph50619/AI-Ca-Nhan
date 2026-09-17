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

app.Use(async (context, next) => { var path = context.Request.Path.Value ?? string.Empty; if (path is "/" or "/index.html" || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) { context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0"; context.Response.Headers.Pragma = "no-cache"; context.Response.Headers.Expires = "0"; } await next(); });
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (IAiProviderResolver providerResolver, ITeamProfileCatalog teamProfiles) => { var aiProvider = providerResolver.GetActive(); return Results.Ok(new { configured = aiProvider.IsConfigured, provider = aiProvider.Name, model = aiProvider.Model, teamProfile = teamProfiles.DefaultProfileId, version = "0.6.4" }); });

app.MapGet("/api/knowledge/documents", async (IKnowledgeDocumentStore store, CancellationToken ct) => Results.Ok(await store.GetAllAsync(ct)));
app.MapGet("/api/knowledge/search", async (string? query, int? limit, IKnowledgeDocumentStore store, CancellationToken ct) => { try { return Results.Ok(await store.SearchAsync(query ?? string.Empty, limit ?? 5, ct)); } catch (KnowledgeDocumentValidationException ex) { return Results.BadRequest(new ApiError(ex.Message)); } });
app.MapGet("/api/knowledge/documents/{documentId:guid}/chunks/{chunkIndex:int}", async (Guid documentId, int chunkIndex, KnowledgeSourceReader reader, CancellationToken ct) => { var source = await reader.GetChunkAsync(documentId, chunkIndex, ct); return source is null ? Results.NotFound() : Results.Ok(source); });
app.MapPost("/api/knowledge/documents", async (HttpRequest request, IKnowledgeDocumentStore store, CancellationToken ct) => { if (!request.HasFormContentType) return Results.BadRequest(new ApiError("Yêu cầu tải tệp không hợp lệ.")); try { var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file"); if (file is null || form.Files.Count != 1) return Results.BadRequest(new ApiError("Hãy chọn đúng một tệp để tải lên.")); var document = await store.AddAsync(file, ct); return Results.Created($"/api/knowledge/documents/{document.Id}", document); } catch (InvalidDataException) { return Results.Json(new ApiError("Tệp tải lên vượt quá giới hạn 10 MB hoặc không hợp lệ."), statusCode: 413); } catch (KnowledgeDocumentValidationException ex) { return Results.BadRequest(new ApiError(ex.Message)); } catch (DuplicateKnowledgeDocumentException ex) { return Results.Json(new ApiError(ex.Message), statusCode: 409); } });
app.MapDelete("/api/knowledge/documents/{documentId:guid}", async (Guid documentId, IKnowledgeDocumentStore store, CancellationToken ct) => await store.DeleteAsync(documentId, ct) ? Results.NoContent() : Results.NotFound());

app.MapGet("/api/memory", async (IPersonalMemoryStore store, CancellationToken ct) => Results.Ok(await store.GetAllAsync(ct)));
app.MapGet("/api/memory/stats", async (IPersonalMemoryStore store, CancellationToken ct) => Results.Ok(await store.GetStatsAsync(ct)));
app.MapGet("/api/memory/export", async (IPersonalMemoryStore store, CancellationToken ct) => Results.Ok(new PersonalMemoryExport(1, DateTimeOffset.UtcNow, await store.GetAllAsync(ct))));
app.MapPost("/api/memory/import", async (ImportPersonalMemoriesRequest request, IPersonalMemoryStore store, CancellationToken ct) => { try { return Results.Ok(await store.ImportAsync(request, ct)); } catch (ArgumentException ex) { return Results.BadRequest(new ApiError(ex.Message)); } });
app.MapPost("/api/memory", async (CreatePersonalMemoryRequest request, IPersonalMemoryStore store, CancellationToken ct) => { try { var memory = await store.AddAsync(request, ct); return Results.Created($"/api/memory/{memory.Id}", memory); } catch (ArgumentException ex) { return Results.BadRequest(new ApiError(ex.Message)); } });
app.MapPut("/api/memory/{memoryId:guid}", async (Guid memoryId, UpdatePersonalMemoryRequest request, IPersonalMemoryStore store, CancellationToken ct) => { try { var memory = await store.UpdateAsync(memoryId, request, ct); return memory is null ? Results.NotFound() : Results.Ok(memory); } catch (MemoryOverwriteConfirmationException ex) { return Results.Json(new { error = ex.Message, duplicateMemoryId = ex.DuplicateMemoryId }, statusCode: 409); } catch (ArgumentException ex) { return Results.BadRequest(new ApiError(ex.Message)); } });
app.MapPatch("/api/memory/{memoryId:guid}/enabled", async (Guid memoryId, SetPersonalMemoryEnabledRequest request, IPersonalMemoryStore store, CancellationToken ct) => { var memory = await store.SetEnabledAsync(memoryId, request.IsEnabled, ct); return memory is null ? Results.NotFound() : Results.Ok(memory); });
app.MapDelete("/api/memory/{memoryId:guid}", async (Guid memoryId, IPersonalMemoryStore store, CancellationToken ct) => await store.DeleteAsync(memoryId, ct) ? Results.NoContent() : Results.NotFound());
app.MapDelete("/api/memory", async (IPersonalMemoryStore store, CancellationToken ct) => Results.Ok(new { deleted = await store.DeleteAllAsync(ct) }));

app.MapGet("/api/settings/ai", (IAiSettingsStore store) => Results.Ok(store.GetPublicSettings()));
app.MapPost("/api/settings/ai", async (UpdateAiSettingsRequest request, IAiSettingsStore store, CancellationToken ct) => { try { await store.SaveAsync(request, ct); return Results.Ok(store.GetPublicSettings()); } catch (ArgumentException ex) { return Results.BadRequest(new ApiError(ex.Message)); } });
app.MapPost("/api/settings/ai/test", async (IAiProviderResolver resolver, CancellationToken ct) => { try { var provider = resolver.GetActive(); await provider.ReplyAsync([new ChatMessage("user", "Chỉ trả lời đúng một từ: OK")], ct); return Results.Ok(new AiConnectionTestResponse(true, "Kết nối AI thành công.")); } catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException) { return Results.Json(new AiConnectionTestResponse(false, ex.Message), statusCode: 502); } });
app.MapGet("/api/team-profiles", (ITeamProfileCatalog profiles) => Results.Ok(new { active = profiles.DefaultProfileId, profiles = profiles.GetAll() }));

app.MapPost("/api/chat", async (ChatRequest request, IAiProviderResolver resolver, IKnowledgeGroundingService grounding, IPersonalMemoryGroundingService memoryGrounding, CancellationToken ct) =>
{
    if (request.Messages is null || request.Messages.Count == 0) return Results.BadRequest(new ApiError("Hãy nhập một câu hỏi."));
    if (request.Messages.Count > 40) return Results.BadRequest(new ApiError("Cuộc trò chuyện quá dài. Hãy tạo cuộc trò chuyện mới."));
    var allowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "user", "assistant" };
    if (request.Messages.Any(message => !allowedRoles.Contains(message.Role) || string.IsNullOrWhiteSpace(message.Content) || message.Content.Length > 12_000)) return Results.BadRequest(new ApiError("Nội dung hội thoại không hợp lệ."));
    var knowledgeMode = string.IsNullOrWhiteSpace(request.KnowledgeMode) ? "normal" : request.KnowledgeMode.Trim().ToLowerInvariant();
    if (knowledgeMode is not ("normal" or "documents-only")) return Results.BadRequest(new ApiError("Chế độ trả lời theo dữ liệu không hợp lệ."));
    if (!request.UseKnowledge && knowledgeMode == "documents-only") return Results.BadRequest(new ApiError("Hãy bật dữ liệu riêng để sử dụng chế độ chỉ trả lời theo tài liệu."));
    try
    {
        var provider = resolver.GetActive();
        var grounded = request.UseKnowledge ? await grounding.GroundAsync(request.Messages, knowledgeMode, ct) : new KnowledgeGroundingResult(request.Messages, []);
        if (knowledgeMode == "documents-only" && grounded.Sources.Count == 0) return Results.Ok(new ChatResponse("Tôi chưa tìm thấy thông tin này trong kho dữ liệu.", provider.Model, provider.Name, []));
        var chatMessages = grounded.Messages;
        if (request.UseMemory && knowledgeMode == "normal") chatMessages = (await memoryGrounding.GroundAsync(request.Messages, chatMessages, ct)).Messages;
        var answer = await provider.ReplyAsync(chatMessages, ct);
        return Results.Ok(new ChatResponse(answer, provider.Model, provider.Name, grounded.Sources));
    }
    catch (InvalidOperationException ex) { return Results.Json(new ApiError(ex.Message), statusCode: 503); }
    catch (HttpRequestException ex) { return Results.Json(new ApiError(ex.Message), statusCode: ex.StatusCode == HttpStatusCode.TooManyRequests ? 429 : 502); }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Json(new ApiError("Yêu cầu AI mất quá nhiều thời gian. Hãy thử lại."), statusCode: 504); }
});

app.MapFallbackToFile("index.html");
app.Run();
