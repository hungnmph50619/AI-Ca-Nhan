namespace PersonalAI.Web.Services;

public sealed record LocalVisualSensorBudget(
    int MaximumTotalMilliseconds,
    int MaximumOcrMilliseconds,
    int MaximumTemplateMilliseconds,
    bool AllowWindowsOcr,
    bool AllowPaddleOcr,
    bool AllowOpenCv,
    string Reason);

public interface ILocalVisualSensorBudgetPolicy
{
    LocalVisualSensorBudget ForPlanning(
        ComputerOperatorDesktopState state,
        bool structuredTargetResolved,
        bool explicitTextIntent);

    bool CanUseProvider(
        string provider,
        LocalVisualSensorBudget budget,
        long elapsedMilliseconds,
        LocalVisualProviderHealth health,
        out string reason);
}

public sealed class LocalVisualSensorBudgetPolicy
    : ILocalVisualSensorBudgetPolicy
{
    public LocalVisualSensorBudget ForPlanning(
        ComputerOperatorDesktopState state,
        bool structuredTargetResolved,
        bool explicitTextIntent)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (structuredTargetResolved)
        {
            return new(
                MaximumTotalMilliseconds: 0,
                MaximumOcrMilliseconds: 0,
                MaximumTemplateMilliseconds: 0,
                AllowWindowsOcr: false,
                AllowPaddleOcr: false,
                AllowOpenCv: false,
                Reason:
                    "Structured resolver đã có target chắc chắn; không tiêu ngân sách visual.");
        }

        var structuredCoverage =
            state.StructuredGraph is null ||
            state.StructuredGraph.NodeCount == 0
                ? 0.0
                : state.StructuredGraph.InteractiveNodeCount /
                  (double)Math.Max(
                      1,
                      state.StructuredGraph.NodeCount);

        if (explicitTextIntent)
        {
            var weakStructured =
                structuredCoverage < 0.10;

            return new(
                MaximumTotalMilliseconds:
                    weakStructured
                        ? 4500
                        : 3000,
                MaximumOcrMilliseconds:
                    weakStructured
                        ? 3500
                        : 2200,
                MaximumTemplateMilliseconds: 800,
                AllowWindowsOcr: true,
                AllowPaddleOcr: true,
                AllowOpenCv: true,
                Reason:
                    weakStructured
                        ? "Intent text rõ và structured coverage yếu; cho phép OCR fallback đầy đủ."
                        : "Intent text rõ; ưu tiên Windows OCR và chỉ fallback provider nặng khi cần.");
        }

        return new(
            MaximumTotalMilliseconds: 1200,
            MaximumOcrMilliseconds: 700,
            MaximumTemplateMilliseconds: 500,
            AllowWindowsOcr: true,
            AllowPaddleOcr: false,
            AllowOpenCv: true,
            Reason:
                "Không có explicit text intent; giới hạn visual budget để tránh tăng latency.");
    }

    public bool CanUseProvider(
        string provider,
        LocalVisualSensorBudget budget,
        long elapsedMilliseconds,
        LocalVisualProviderHealth health,
        out string reason)
    {
        provider =
            (provider ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

        if (elapsedMilliseconds >=
            budget.MaximumTotalMilliseconds)
        {
            reason =
                $"Đã dùng hết visual budget {budget.MaximumTotalMilliseconds}ms.";
            return false;
        }

        if (health.State ==
            LocalVisualProviderHealthState.Unavailable)
        {
            reason =
                $"{provider} đang unavailable theo health registry.";
            return false;
        }

        var allowed =
            provider switch
            {
                "windows-ocr" =>
                    budget.AllowWindowsOcr,
                "paddleocr-onnx" =>
                    budget.AllowPaddleOcr,
                "opencv-template" =>
                    budget.AllowOpenCv,
                _ => false
            };

        if (!allowed)
        {
            reason =
                $"{provider} bị policy tắt cho lượt planning này.";
            return false;
        }

        var providerLimit =
            provider switch
            {
                "windows-ocr" or
                "paddleocr-onnx" =>
                    budget.MaximumOcrMilliseconds,
                "opencv-template" =>
                    budget.MaximumTemplateMilliseconds,
                _ => 0
            };

        if (providerLimit <= 0)
        {
            reason =
                $"{provider} không có latency budget.";
            return false;
        }

        if (elapsedMilliseconds >=
            providerLimit)
        {
            reason =
                $"{provider} vượt budget riêng {providerLimit}ms.";
            return false;
        }

        if (health.LastLatencyMilliseconds > 0 &&
            health.LastLatencyMilliseconds >
                providerLimit * 2L)
        {
            reason =
                $"{provider} có latency lịch sử {health.LastLatencyMilliseconds}ms quá cao so với budget {providerLimit}ms.";
            return false;
        }

        reason =
            $"Cho phép {provider}; elapsed={elapsedMilliseconds}ms; providerBudget={providerLimit}ms.";
        return true;
    }
}
