using System.Text.Json;
using PersonalAI.Web.ModelLab;

namespace PersonalAI.Web.Services;

public sealed class PeftTrainingProvider : ITrainingProvider
{
    public const string ProviderId = "peft-config";

    public string Id => ProviderId;
    public string DisplayName => "PEFT Configuration Provider";

    public TrainingProviderCapabilities GetCapabilities() =>
        new(
            ProviderId,
            DisplayName,
            SupportsFullFineTune: false,
            SupportsLoRa: true,
            SupportsQLoRa: true,
            SupportsResume: false,
            SupportsMetrics: false,
            SupportsCancellation: false,
            SupportsGpu: true,
            ExternalService: false,
            SupportedMethods:
            [
                TrainingMethods.LoRa,
                TrainingMethods.QLoRa,
                TrainingMethods.Adapter
            ]);

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

        if (!TrainingMethods.IsPeft(method))
            errors.Add("Provider PEFT chỉ hỗ trợ lora, qlora hoặc adapter.");

        PeftTrainingConfiguration? config = null;
        if (request.Hyperparameters is null ||
            request.Hyperparameters.Value.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Hyperparameters PEFT phải là JSON object.");
        }
        else
        {
            try
            {
                config = Parse(request.Hyperparameters.Value);
            }
            catch (TrainingProviderValidationException exception)
            {
                errors.Add(exception.Message);
            }
        }

        if (config is not null)
        {
            if (config.Rank is < 1 or > 256)
                errors.Add("rank phải từ 1 đến 256.");
            if (config.Alpha <= 0 || config.Alpha > 1024)
                errors.Add("alpha phải > 0 và <= 1024.");
            if (config.Dropout is < 0 or >= 1)
                errors.Add("dropout phải từ 0 đến nhỏ hơn 1.");
            if (config.TargetModules.Count is < 1 or > 64)
                errors.Add("targetModules phải có từ 1 đến 64 phần tử.");
            if (config.TargetModules.Any(x =>
                string.IsNullOrWhiteSpace(x) || x.Length > 120))
                errors.Add("targetModules chứa phần tử không hợp lệ.");
            if (config.LearningRate <= 0 || config.LearningRate > 1)
                errors.Add("learningRate phải > 0 và <= 1.");
            if (config.Epochs is < 1 or > 100)
                errors.Add("epochs phải từ 1 đến 100.");
            if (config.BatchSize is < 1 or > 1024)
                errors.Add("batchSize phải từ 1 đến 1024.");
            if (config.GradientAccumulationSteps is < 1 or > 1024)
                errors.Add("gradientAccumulationSteps phải từ 1 đến 1024.");
            if (config.MaximumSequenceLength is < 64 or > 131072)
                errors.Add("maximumSequenceLength phải từ 64 đến 131072.");

            if (method == TrainingMethods.QLoRa)
                warnings.Add("QLoRA cần backend hỗ trợ quantization; v2.5.8 mới chỉ chuẩn hóa cấu hình.");
            else
                warnings.Add("v2.5.8 mới chỉ chuẩn hóa cấu hình PEFT; chưa bật fine-tune LLM thật.");
        }

        return new(
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

        var validation = Validate(new(
            ProviderId,
            request.BaseModel,
            request.TrainingMethod,
            request.Hyperparameters));

        if (!validation.Valid)
            throw new TrainingProviderValidationException(
                string.Join(" ", validation.Errors));

        throw new TrainingProviderValidationException(
            "v2.5.8 chưa bật PEFT executor thật. Cấu hình đã hợp lệ nhưng job phải giữ Pending.");
    }

    public Task<TrainingProviderProgress> GetStatusAsync(
        string externalJobId,
        CancellationToken cancellationToken = default) =>
        throw new TrainingProviderValidationException(
            "PEFT executor chưa được bật ở v2.5.8.");

    public Task CancelAsync(
        string externalJobId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private static PeftTrainingConfiguration Parse(JsonElement json)
    {
        int GetInt(string name)
        {
            if (!json.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.Number ||
                !value.TryGetInt32(out var result))
            {
                throw new TrainingProviderValidationException(
                    $"Thiếu hoặc sai kiểu trường '{name}'.");
            }
            return result;
        }

        double GetDouble(string name)
        {
            if (!json.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.Number ||
                !value.TryGetDouble(out var result))
            {
                throw new TrainingProviderValidationException(
                    $"Thiếu hoặc sai kiểu trường '{name}'.");
            }
            return result;
        }

        if (!json.TryGetProperty("targetModules", out var modules) ||
            modules.ValueKind != JsonValueKind.Array)
        {
            throw new TrainingProviderValidationException(
                "Thiếu hoặc sai kiểu trường 'targetModules'.");
        }

        var targetModules = modules.EnumerateArray()
            .Select(x => x.ValueKind == JsonValueKind.String
                ? x.GetString() ?? string.Empty
                : string.Empty)
            .ToArray();

        return new PeftTrainingConfiguration(
            GetInt("rank"),
            GetDouble("alpha"),
            GetDouble("dropout"),
            targetModules,
            GetDouble("learningRate"),
            GetInt("epochs"),
            GetInt("batchSize"),
            GetInt("gradientAccumulationSteps"),
            GetInt("maximumSequenceLength"));
    }
}
