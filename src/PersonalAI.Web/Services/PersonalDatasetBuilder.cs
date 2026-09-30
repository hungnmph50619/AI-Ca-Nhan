using System.Text.Json;
using PersonalAI.Web.Evaluation.Regression;
using PersonalAI.Web.ModelLab;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public interface IPersonalDatasetBuilder
{
    Task<PersonalDatasetBuildResult> BuildAsync(
        BuildPersonalDatasetRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PersonalDatasetBuilder(
    IModelLabDatasetStore datasets,
    IPersonalMemoryStore memories,
    IRegressionDatasetStore regression,
    KnowledgeSourceReader knowledge,
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IPersonalDatasetBuilder
{
    public const int MaximumSelections = 200;

    public async Task<PersonalDatasetBuildResult> BuildAsync(
        BuildPersonalDatasetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.ConfirmUseSelectedPersonalData)
        {
            throw new PersonalDatasetBuilderValidationException(
                "Cần xác nhận rõ việc dùng các dữ liệu cá nhân đã chọn để tạo dataset.");
        }

        if (request.Sources is null ||
            request.Sources.Count is < 1 or > MaximumSelections)
        {
            throw new PersonalDatasetBuilderValidationException(
                $"Phải chọn từ 1 đến {MaximumSelections} nguồn dữ liệu.");
        }

        var normalized = request.Sources
            .Select(NormalizeSelection)
            .ToArray();

        var duplicateKey = normalized
            .GroupBy(
                item => $"{item.Kind}|{item.Id}|{item.ChunkIndex}",
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateKey is not null)
        {
            throw new PersonalDatasetBuilderValidationException(
                "Danh sách nguồn có selection bị trùng.");
        }

        var items = new List<JsonElement>(normalized.Length);
        var sourceCounts = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        var memoryMap = (await memories.GetAllAsync(cancellationToken))
            .ToDictionary(
                item => item.Id.ToString("D"),
                StringComparer.OrdinalIgnoreCase);

        foreach (var selection in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JsonElement item = selection.Kind switch
            {
                PersonalDatasetSourceKinds.Memory =>
                    BuildMemoryItem(selection, memoryMap),
                PersonalDatasetSourceKinds.RegressionCase =>
                    BuildRegressionItem(selection),
                PersonalDatasetSourceKinds.KnowledgeChunk =>
                    await BuildKnowledgeChunkItemAsync(
                        selection,
                        cancellationToken),
                _ => throw new PersonalDatasetBuilderValidationException(
                    $"Source kind '{selection.Kind}' không được hỗ trợ.")
            };

            items.Add(item);
            sourceCounts[selection.Kind] =
                sourceCounts.TryGetValue(selection.Kind, out var count)
                    ? count + 1
                    : 1;
        }

        if (items.Count == 0)
            throw new PersonalDatasetBuilderValidationException(
                "Không tạo được dataset item nào từ các nguồn đã chọn.");

        ModelLabDatasetVersion version;
        if (request.CreateDataset)
        {
            if (datasets.Get(request.DatasetId) is not null)
            {
                throw new ModelLabDatasetConflictException(
                    $"Dataset '{request.DatasetId}' đã tồn tại.");
            }

            var summary = datasets.Create(new CreateModelLabDatasetRequest(
                request.DatasetId,
                request.DatasetName ?? request.DatasetId,
                request.Description,
                items,
                request.VersionNote));

            version = datasets.GetVersion(
                    summary.Id,
                    summary.LatestVersion)
                ?? throw new InvalidOperationException(
                    "Không đọc lại được dataset version vừa tạo.");
        }
        else
        {
            if (datasets.Get(request.DatasetId) is null)
                throw new KeyNotFoundException(
                    "Không tìm thấy Model Lab dataset đích.");

            version = datasets.CreateVersion(
                request.DatasetId,
                new CreateModelLabDatasetVersionRequest(
                    items,
                    request.VersionNote,
                    request.ExpectedLatestVersion));
        }

        audit.Record(
            AuditAgents.User,
            "model-lab.personal-dataset.build",
            $"model-lab-dataset:{version.DatasetId}:v{version.Version}",
            $"explicit-selection:{items.Count}",
            AuditResults.Succeeded);

        return new PersonalDatasetBuildResult(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            version.DatasetId,
            version.Version,
            normalized.Length,
            items.Count,
            sourceCounts,
            version.ContentSha256,
            ExplicitSelectionOnly: true,
            version.CreatedAt);
    }

    private static PersonalDatasetSourceSelection NormalizeSelection(
        PersonalDatasetSourceSelection selection)
    {
        if (selection is null)
            throw new PersonalDatasetBuilderValidationException(
                "Source selection không được null.");

        var kind = (selection.Kind ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        var id = (selection.Id ?? string.Empty).Trim();

        if (kind is not (
            PersonalDatasetSourceKinds.Memory or
            PersonalDatasetSourceKinds.RegressionCase or
            PersonalDatasetSourceKinds.KnowledgeChunk))
        {
            throw new PersonalDatasetBuilderValidationException(
                $"Source kind '{kind}' không hợp lệ.");
        }

        if (id.Length is < 1 or > 120)
            throw new PersonalDatasetBuilderValidationException(
                "Source ID phải có từ 1 đến 120 ký tự.");

        if (kind == PersonalDatasetSourceKinds.KnowledgeChunk)
        {
            if (selection.ChunkIndex is null or <= 0)
                throw new PersonalDatasetBuilderValidationException(
                    "Knowledge chunk bắt buộc có chunkIndex > 0.");
        }
        else if (selection.ChunkIndex is not null)
        {
            throw new PersonalDatasetBuilderValidationException(
                "chunkIndex chỉ dùng cho knowledge-chunk.");
        }

        return new PersonalDatasetSourceSelection(
            kind,
            id,
            selection.ChunkIndex);
    }

    private static JsonElement BuildMemoryItem(
        PersonalDatasetSourceSelection selection,
        IReadOnlyDictionary<string, PersonalMemory> memoryMap)
    {
        if (!Guid.TryParse(selection.Id, out var memoryId) ||
            !memoryMap.TryGetValue(memoryId.ToString("D"), out var memory))
        {
            throw new PersonalDatasetBuilderValidationException(
                $"Không tìm thấy memory '{selection.Id}'.");
        }

        if (!memory.IsEnabled || memory.IsExpired)
        {
            throw new PersonalDatasetBuilderValidationException(
                $"Memory '{selection.Id}' đang disabled hoặc expired.");
        }

        return JsonSerializer.SerializeToElement(new
        {
            source = PersonalDatasetSourceKinds.Memory,
            sourceId = memory.Id,
            memory.Kind,
            memory.Content,
            memory.CreatedAt,
            memory.UpdatedAt
        });
    }

    private JsonElement BuildRegressionItem(
        PersonalDatasetSourceSelection selection)
    {
        RegressionDatasetItem item;
        try
        {
            item = regression.Get(selection.Id)
                ?? throw new PersonalDatasetBuilderValidationException(
                    $"Không tìm thấy regression case '{selection.Id}'.");
        }
        catch (RegressionDatasetValidationException exception)
        {
            throw new PersonalDatasetBuilderValidationException(
                exception.Message);
        }

        if (!item.Enabled)
        {
            throw new PersonalDatasetBuilderValidationException(
                $"Regression case '{selection.Id}' đang disabled.");
        }

        return JsonSerializer.SerializeToElement(new
        {
            source = PersonalDatasetSourceKinds.RegressionCase,
            sourceId = item.Id,
            item.Title,
            item.Category,
            input = item.Input,
            expected = item.Expected,
            metadata = item.Metadata
        });
    }

    private async Task<JsonElement> BuildKnowledgeChunkItemAsync(
        PersonalDatasetSourceSelection selection,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(selection.Id, out var documentId))
        {
            throw new PersonalDatasetBuilderValidationException(
                $"Knowledge document ID '{selection.Id}' không hợp lệ.");
        }

        var chunk = await knowledge.GetChunkAsync(
            documentId,
            selection.ChunkIndex!.Value,
            cancellationToken);

        if (chunk is null)
        {
            throw new PersonalDatasetBuilderValidationException(
                $"Không tìm thấy knowledge chunk '{selection.Id}:{selection.ChunkIndex}'.");
        }

        return JsonSerializer.SerializeToElement(new
        {
            source = PersonalDatasetSourceKinds.KnowledgeChunk,
            sourceId = chunk.DocumentId,
            chunkIndex = chunk.ChunkIndex,
            chunk.FileName,
            chunk.Content,
            chunk.PageNumber,
            chunk.Heading,
            chunk.Section
        });
    }
}
