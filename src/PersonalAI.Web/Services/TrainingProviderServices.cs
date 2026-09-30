using System.Collections.Concurrent;
using System.Text.Json;
using PersonalAI.Web.ModelLab;

namespace PersonalAI.Web.Services;

public interface ITrainingProvider
{
    string Id { get; }
    string DisplayName { get; }
    TrainingProviderCapabilities GetCapabilities();
    TrainingProviderValidationResult Validate(
        TrainingProviderValidationRequest request);
    Task<TrainingProviderExecutionHandle> StartAsync(
        TrainingProviderStartRequest request,
        CancellationToken cancellationToken = default);
    Task<TrainingProviderProgress> GetStatusAsync(
        string externalJobId,
        CancellationToken cancellationToken = default);
    Task CancelAsync(
        string externalJobId,
        CancellationToken cancellationToken = default);
}

public interface ITrainingProviderRegistry
{
    IReadOnlyList<TrainingProviderCapabilities> GetAll();
    ITrainingProvider Get(string providerId);
}

public sealed class TrainingProviderRegistry(
    IEnumerable<ITrainingProvider> providers) : ITrainingProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ITrainingProvider> _providers =
        providers.ToDictionary(
            provider => provider.Id,
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<TrainingProviderCapabilities> GetAll() =>
        _providers.Values
            .Select(provider => provider.GetCapabilities())
            .OrderBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ITrainingProvider Get(string providerId)
    {
        var id = (providerId ?? string.Empty).Trim();
        if (id.Length == 0)
            throw new TrainingProviderValidationException(
                "ProviderId không được để trống.");

        if (!_providers.TryGetValue(id, out var provider))
            throw new KeyNotFoundException(
                $"Không tìm thấy training provider '{id}'.");

        return provider;
    }
}

public sealed class MockTrainingProvider : ITrainingProvider
{
    public const string ProviderId = "mock";
    private readonly ConcurrentDictionary<string, TrainingProviderProgress> _jobs =
        new(StringComparer.OrdinalIgnoreCase);

    public string Id => ProviderId;
    public string DisplayName => "Mock Training Provider";

    public TrainingProviderCapabilities GetCapabilities() =>
        new(
            ProviderId,
            DisplayName,
            SupportsFullFineTune: true,
            SupportsLoRa: false,
            SupportsQLoRa: false,
            SupportsResume: false,
            SupportsMetrics: true,
            SupportsCancellation: true,
            SupportsGpu: false,
            ExternalService: false,
            SupportedMethods: ["full"]);

    public TrainingProviderValidationResult Validate(
        TrainingProviderValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        var warnings = new List<string>();
        var baseModel = (request.BaseModel ?? string.Empty).Trim();
        var method = (request.TrainingMethod ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (baseModel.Length is < 1 or > 200)
            errors.Add("BaseModel phải có từ 1 đến 200 ký tự.");

        if (method != "full")
            errors.Add("Mock provider v2.5.5 chỉ hỗ trợ trainingMethod='full'.");

        if (request.Hyperparameters is not null &&
            request.Hyperparameters.Value.ValueKind is not (
                JsonValueKind.Object or
                JsonValueKind.Null or
                JsonValueKind.Undefined))
        {
            errors.Add("Hyperparameters phải là JSON object.");
        }

        warnings.Add(
            "Mock provider chỉ dùng để kiểm thử orchestration; không huấn luyện mô hình thật.");

        return new TrainingProviderValidationResult(
            errors.Count == 0,
            ProviderId,
            baseModel,
            method,
            errors,
            warnings);
    }

    public Task<TrainingProviderExecutionHandle> StartAsync(
        TrainingProviderStartRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var validation = Validate(new TrainingProviderValidationRequest(
            ProviderId,
            request.BaseModel,
            request.TrainingMethod,
            request.Hyperparameters));

        if (!validation.Valid)
            throw new TrainingProviderValidationException(
                string.Join(" ", validation.Errors));

        var externalJobId = $"mock-{request.TrainingJobId:D}";
        var now = DateTimeOffset.UtcNow;
        _jobs[externalJobId] = new TrainingProviderProgress(
            ProviderId,
            externalJobId,
            TrainingProviderExecutionStatuses.Queued,
            0,
            JsonSerializer.SerializeToElement(new
            {
                simulated = true,
                trainingJobId = request.TrainingJobId
            }),
            null,
            now);

        return Task.FromResult(new TrainingProviderExecutionHandle(
            ProviderId,
            externalJobId,
            TrainingProviderExecutionStatuses.Queued,
            now));
    }

    public Task<TrainingProviderProgress> GetStatusAsync(
        string externalJobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = NormalizeExternalJobId(externalJobId);

        if (!_jobs.TryGetValue(id, out var progress))
            throw new KeyNotFoundException(
                "Không tìm thấy mock training job.");

        return Task.FromResult(progress with
        {
            CheckedAt = DateTimeOffset.UtcNow
        });
    }

    public Task CancelAsync(
        string externalJobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = NormalizeExternalJobId(externalJobId);

        if (!_jobs.TryGetValue(id, out var progress))
            throw new KeyNotFoundException(
                "Không tìm thấy mock training job.");

        if (progress.Status == TrainingProviderExecutionStatuses.Completed)
            throw new TrainingProviderValidationException(
                "Không thể hủy mock training job đã hoàn thành.");

        _jobs[id] = progress with
        {
            Status = TrainingProviderExecutionStatuses.Cancelled,
            CheckedAt = DateTimeOffset.UtcNow
        };

        return Task.CompletedTask;
    }

    private static string NormalizeExternalJobId(string externalJobId)
    {
        var id = (externalJobId ?? string.Empty).Trim();
        if (id.Length is < 1 or > 200)
            throw new TrainingProviderValidationException(
                "ExternalJobId phải có từ 1 đến 200 ký tự.");
        return id;
    }
}
