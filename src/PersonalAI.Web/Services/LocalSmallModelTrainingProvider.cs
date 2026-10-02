using System.Collections.Concurrent;
using System.Text.Json;
using PersonalAI.Web.ModelLab;

namespace PersonalAI.Web.Services;

public sealed class LocalSmallModelTrainingProvider(
    IModelLabDatasetStore datasets,
    IConfiguration configuration) : ITrainingProvider
{
    public const string ProviderId = "local-small-model";
    public const string Method = "naive-bayes";

    private readonly ConcurrentDictionary<string, TrainingProviderProgress> _jobs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _artifactRoot = ResolveArtifactRoot(configuration);

    public string Id => ProviderId;
    public string DisplayName => "Local Small Model Trainer";

    public TrainingProviderCapabilities GetCapabilities() =>
        new(
            ProviderId,
            DisplayName,
            SupportsFullFineTune: false,
            SupportsLoRa: false,
            SupportsQLoRa: false,
            SupportsResume: false,
            SupportsMetrics: true,
            SupportsCancellation: false,
            SupportsGpu: false,
            ExternalService: false,
            SupportedMethods: [Method]);

    public TrainingProviderValidationResult Validate(TrainingProviderValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        var warnings = new List<string>();
        var method = (request.TrainingMethod ?? string.Empty).Trim().ToLowerInvariant();
        var baseModel = (request.BaseModel ?? string.Empty).Trim();

        if (method != Method)
            errors.Add($"Provider chỉ hỗ trợ trainingMethod='{Method}'.");
        if (baseModel.Length is < 1 or > 200)
            errors.Add("BaseModel phải có từ 1 đến 200 ký tự.");

        warnings.Add("Mô hình này chỉ phù hợp tác vụ phân loại văn bản nhỏ.");
        return new(errors.Count == 0, ProviderId, baseModel, method, errors, warnings);
    }

    public Task<TrainingProviderExecutionHandle> StartAsync(
        TrainingProviderStartRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validation = Validate(new(
            ProviderId,
            request.BaseModel,
            request.TrainingMethod,
            request.Hyperparameters));
        if (!validation.Valid)
            throw new TrainingProviderValidationException(string.Join(" ", validation.Errors));

        var source = datasets.GetVersion(request.DatasetId, request.DatasetVersion)
            ?? throw new KeyNotFoundException("Không tìm thấy dataset version để huấn luyện.");
        if (!string.Equals(source.ContentSha256, request.DatasetSha256, StringComparison.OrdinalIgnoreCase))
            throw new TrainingProviderValidationException("Checksum dataset đã thay đổi.");

        var examples = source.Items
            .Select(ParseExample)
            .Where(x => x is not null)
            .Cast<Example>()
            .ToArray();

        if (examples.Length < 2)
            throw new TrainingProviderValidationException(
                "Naive Bayes cần ít nhất 2 example dạng input + expected.");
        if (examples.Select(x => x.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            throw new TrainingProviderValidationException("Dataset cần ít nhất 2 nhãn khác nhau.");

        var model = Train(examples);
        var correct = examples.Count(x =>
            string.Equals(Predict(model, x.Text), x.Label, StringComparison.OrdinalIgnoreCase));
        var accuracy = (double)correct / examples.Length;

        Directory.CreateDirectory(_artifactRoot);
        var artifactPath = Path.Combine(
            _artifactRoot,
            $"{request.TrainingJobId:D}.naive-bayes.json");
        var artifact = new
        {
            kind = Method,
            request.TrainingJobId,
            request.DatasetId,
            request.DatasetVersion,
            request.DatasetSha256,
            labels = model.LabelDocumentCounts.Keys.OrderBy(x => x).ToArray(),
            documentCounts = model.LabelDocumentCounts,
            tokenCounts = model.TokenCounts,
            totalTokens = model.TotalTokens,
            vocabulary = model.Vocabulary.OrderBy(x => x).ToArray(),
            createdAt = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            artifactPath,
            JsonSerializer.Serialize(artifact, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));

        var id = $"local-{request.TrainingJobId:D}";
        var metrics = JsonSerializer.SerializeToElement(new
        {
            trainingAccuracy = accuracy,
            examples = examples.Length,
            labels = model.LabelDocumentCounts.Count,
            vocabularySize = model.Vocabulary.Count,
            artifactPath,
            candidateOnly = true
        });

        _jobs[id] = new(
            ProviderId,
            id,
            TrainingProviderExecutionStatuses.Completed,
            100,
            metrics,
            null,
            DateTimeOffset.UtcNow);

        return Task.FromResult(new TrainingProviderExecutionHandle(
            ProviderId,
            id,
            TrainingProviderExecutionStatuses.Completed,
            DateTimeOffset.UtcNow));
    }

    public Task<TrainingProviderProgress> GetStatusAsync(
        string externalJobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_jobs.TryGetValue(externalJobId, out var progress))
            throw new KeyNotFoundException("Không tìm thấy local training job.");
        return Task.FromResult(progress with { CheckedAt = DateTimeOffset.UtcNow });
    }

    public Task CancelAsync(string externalJobId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private static Example? ParseExample(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("input", out var input) ||
            input.ValueKind != JsonValueKind.String ||
            !item.TryGetProperty("expected", out var expected))
            return null;

        var text = input.GetString()?.Trim();
        var label = expected.ValueKind == JsonValueKind.String
            ? expected.GetString()?.Trim()
            : expected.GetRawText();

        return string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label)
            ? null
            : new Example(text, label);
    }

    private static NaiveBayesModel Train(IReadOnlyList<Example> examples)
    {
        var docs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tokens = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        var vocab = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var example in examples)
        {
            docs[example.Label] = docs.GetValueOrDefault(example.Label) + 1;
            if (!tokens.TryGetValue(example.Label, out var labelTokens))
                tokens[example.Label] = labelTokens = new(StringComparer.OrdinalIgnoreCase);

            foreach (var token in Tokenize(example.Text))
            {
                vocab.Add(token);
                labelTokens[token] = labelTokens.GetValueOrDefault(token) + 1;
                totals[example.Label] = totals.GetValueOrDefault(example.Label) + 1;
            }
        }

        return new(docs, tokens, totals, vocab, examples.Count);
    }

    private static string Predict(NaiveBayesModel model, string text)
    {
        var bestLabel = string.Empty;
        var bestScore = double.NegativeInfinity;
        foreach (var label in model.LabelDocumentCounts.Keys)
        {
            var score = Math.Log((double)model.LabelDocumentCounts[label] / model.DocumentCount);
            var denominator = model.TotalTokens.GetValueOrDefault(label) + model.Vocabulary.Count;
            foreach (var token in Tokenize(text))
            {
                var count = model.TokenCounts[label].GetValueOrDefault(token);
                score += Math.Log((count + 1d) / denominator);
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestLabel = label;
            }
        }
        return bestLabel;
    }

    private static IEnumerable<string> Tokenize(string text) =>
        new string(text.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray())
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(x => x.Length > 1);

    private static string ResolveArtifactRoot(IConfiguration configuration)
    {
        var root = configuration["ModelLab:Root"];
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalAI",
                "ModelLab");
        root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));
        return Path.Combine(root, "Candidates");
    }

    private sealed record Example(string Text, string Label);

    private sealed record NaiveBayesModel(
        Dictionary<string, int> LabelDocumentCounts,
        Dictionary<string, Dictionary<string, int>> TokenCounts,
        Dictionary<string, int> TotalTokens,
        HashSet<string> Vocabulary,
        int DocumentCount);
}
