using System.Security.Cryptography;
using System.Text;
using PersonalAI.Web.Models;
using PersonalAI.Web.SelfImprovement;

namespace PersonalAI.Web.Services;

public interface IImprovementProposalService
{
    ImprovementProposalReport Create(ImprovementProposalRequest request);
}

public sealed class ImprovementProposalService(
    IWorkspaceContextAccessor workspace,
    IAuditRecorder audit) : IImprovementProposalService
{
    public const int DefaultMaximumProposals = 10;
    public const int MaximumProposals = 25;
    public const int MaximumWeaknesses = 100;

    public ImprovementProposalReport Create(ImprovementProposalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Evaluation);

        var evaluation = request.Evaluation;
        if (!string.Equals(
            evaluation.WorkspaceId,
            workspace.CurrentWorkspaceId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ImprovementProposalValidationException(
                "Self Evaluation report không thuộc workspace hiện tại.");
        }

        if (!evaluation.ReadOnly || evaluation.AutomaticChangesEnabled)
        {
            throw new ImprovementProposalValidationException(
                "Chỉ chấp nhận Self Evaluation report ở chế độ read-only, không tự thay đổi.");
        }

        if (evaluation.Weaknesses.Count > MaximumWeaknesses)
        {
            throw new ImprovementProposalValidationException(
                $"Chỉ xử lý tối đa {MaximumWeaknesses} weakness mỗi lần.");
        }

        var maximum = request.MaximumProposals ?? DefaultMaximumProposals;
        if (maximum is < 1 or > MaximumProposals)
        {
            throw new ImprovementProposalValidationException(
                $"MaximumProposals phải từ 1 đến {MaximumProposals}.");
        }

        var normalized = evaluation.Weaknesses
            .Select(NormalizeWeakness)
            .OrderByDescending(item => SeverityRank(item.Severity))
            .ThenByDescending(item => item.EvidenceCount)
            .ThenBy(item => item.Area, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Signal, StringComparer.OrdinalIgnoreCase)
            .Take(maximum)
            .ToArray();

        var proposals = normalized
            .Select(BuildProposal)
            .ToArray();

        audit.Record(
            AuditAgents.System,
            "self-improvement.propose",
            $"improvement-proposals:{workspace.CurrentWorkspaceId}",
            "read-only-proposal-generation",
            AuditResults.Succeeded);

        return new ImprovementProposalReport(
            PersonalAiRelease.Version,
            workspace.CurrentWorkspaceId,
            DateTimeOffset.UtcNow,
            evaluation.Weaknesses.Count,
            proposals.Length,
            proposals,
            ReadOnly: true,
            AutomaticChangesEnabled: false);
    }

    private static SelfEvaluationWeakness NormalizeWeakness(SelfEvaluationWeakness weakness)
    {
        if (weakness is null)
        {
            throw new ImprovementProposalValidationException(
                "Weakness không được null.");
        }

        var area = (weakness.Area ?? string.Empty).Trim();
        var signal = (weakness.Signal ?? string.Empty).Trim();
        var severity = (weakness.Severity ?? string.Empty).Trim().ToLowerInvariant();
        var evidence = (weakness.Evidence ?? string.Empty).Trim();

        if (area.Length is < 2 or > 80 ||
            signal.Length is < 2 or > 120 ||
            evidence.Length is < 2 or > 1000 ||
            weakness.EvidenceCount < 1)
        {
            throw new ImprovementProposalValidationException(
                "Weakness có area/signal/evidence/evidenceCount không hợp lệ.");
        }

        if (severity is not ("low" or "medium" or "high"))
        {
            throw new ImprovementProposalValidationException(
                "Weakness severity chỉ được low, medium hoặc high.");
        }

        return weakness with
        {
            Area = area,
            Signal = signal,
            Severity = severity,
            Evidence = evidence
        };
    }

    private static ImprovementProposal BuildProposal(SelfEvaluationWeakness weakness)
    {
        var template = SelectTemplate(weakness);
        return new ImprovementProposal(
            Id: CreateStableId(weakness),
            Area: weakness.Area,
            Severity: weakness.Severity,
            Problem: template.Problem,
            Hypothesis: template.Hypothesis,
            Change: template.Change,
            ExpectedImprovement: template.ExpectedImprovement,
            Risk: template.Risk,
            Evidence: weakness.Evidence,
            EvidenceCount: weakness.EvidenceCount,
            RequiresHumanApproval: true);
    }

    private static ProposalTemplate SelectTemplate(SelfEvaluationWeakness weakness)
    {
        if (string.Equals(
            weakness.Signal,
            "benchmark-failures",
            StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Problem: "Một phần Agent Benchmark không đạt expected outcome.",
                Hypothesis: "Prompt, context selection hoặc evaluator contract có thể chưa đủ ổn định cho các case đang thất bại.",
                Change: "Khoanh vùng các case fail, phân loại nguyên nhân theo agent/context/evaluator và thử một thay đổi nhỏ có thể đảo ngược trên branch thử nghiệm.",
                ExpectedImprovement: "Giảm số case benchmark thất bại mà không làm giảm các case đang pass.",
                Risk: "Tối ưu quá mức cho regression dataset có thể làm giảm khả năng tổng quát; cần benchmark lại toàn bộ tập trước khi chấp nhận.");
        }

        if (weakness.Signal.StartsWith(
            "failure-",
            StringComparison.OrdinalIgnoreCase))
        {
            var failureKind = weakness.Signal["failure-".Length..];
            return new(
                Problem: $"Benchmark lặp lại lỗi nhóm '{failureKind}'.",
                Hypothesis: "Một lỗi có cùng dạng xuất hiện nhiều lần thường cho thấy contract đầu ra hoặc xử lý context chưa nhất quán.",
                Change: $"Tạo test hồi quy riêng cho lỗi '{failureKind}', xác định lớp chịu trách nhiệm và chỉ thay đổi đúng lớp đó.",
                ExpectedImprovement: $"Giảm tần suất lỗi '{failureKind}' trong benchmark kế tiếp.",
                Risk: "Nếu nguyên nhân thực tế nằm ở dữ liệu đầu vào hoặc expected check, sửa runtime có thể làm sai hướng; cần xác minh case trước.");
        }

        if (string.Equals(
            weakness.Signal,
            "high-agent-latency",
            StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Problem: "Latency benchmark của agent cao.",
                Hypothesis: "Context quá lớn, provider chậm hoặc pipeline có bước tuần tự không cần thiết.",
                Change: "Đo latency theo từng pha, kiểm tra kích thước context và chỉ tối ưu phần chiếm thời gian lớn nhất trước.",
                ExpectedImprovement: "Giảm average latency mà giữ nguyên pass rate và hành vi an toàn.",
                Risk: "Cắt context hoặc timeout quá mạnh có thể làm giảm chất lượng câu trả lời.");
        }

        if (string.Equals(
            weakness.Signal,
            "lower-pass-rate-than-baseline",
            StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Problem: "Một candidate có pass rate thấp hơn baseline trên cùng benchmark.",
                Hypothesis: "Candidate có thể chưa phù hợp với prompt/context hiện tại hoặc có khác biệt hành vi làm expected checks thất bại.",
                Change: "So sánh các case chênh lệch, giữ nguyên dataset và chỉ thử điều chỉnh cấu hình/prompt trên branch thử nghiệm.",
                ExpectedImprovement: "Thu hẹp chênh lệch pass rate so với baseline trên cùng tập case.",
                Risk: "Baseline chỉ là mốc kỹ thuật, không phải ground truth tuyệt đối; không được tự suy ra candidate tốt/xấu ngoài dữ liệu benchmark.");
        }

        if (string.Equals(
            weakness.Signal,
            "higher-latency-than-baseline",
            StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Problem: "Một candidate có latency cao hơn baseline đáng kể.",
                Hypothesis: "Model/provider hoặc cấu hình context có overhead lớn hơn trong cùng bài benchmark.",
                Change: "Đo lại trên cùng case set và tách latency provider khỏi latency xử lý local trước khi tối ưu.",
                ExpectedImprovement: "Giảm độ trễ candidate mà không làm thay đổi expected outcome.",
                Risk: "Latency có thể dao động do mạng/provider; cần nhiều lần đo trước khi thay đổi cấu hình.");
        }

        if (string.Equals(
            weakness.Signal,
            "user-corrections",
            StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Problem: $"Người dùng phải sửa kết quả lặp lại trong khu vực '{weakness.Area}'.",
                Hypothesis: "Hành vi hiện tại chưa khớp kỳ vọng thực tế của người dùng hoặc thiếu regression case đại diện.",
                Change: "Chuyển correction thành regression case có expected rõ ràng, xác minh lại trước khi thay đổi prompt/evaluator/runtime.",
                ExpectedImprovement: "Giảm số lần người dùng phải sửa cùng loại lỗi trong các lần sử dụng sau.",
                Risk: "Correction có thể là trường hợp riêng; không nên tổng quát hóa thành thay đổi hệ thống nếu chưa có đủ bằng chứng.");
        }

        if (weakness.Signal.StartsWith(
            "repeated-",
            StringComparison.OrdinalIgnoreCase))
        {
            return new(
                Problem: $"Audit ghi nhận action '{weakness.Signal["repeated-".Length..]}' thất bại hoặc bị chặn lặp lại.",
                Hypothesis: "Có thể tồn tại lỗi luồng, permission boundary chưa rõ hoặc input validation chưa tốt.",
                Change: "Phân tích audit event tương ứng, tái tạo bằng test an toàn và xác định lỗi nằm ở validation, permission hay runtime trước khi sửa.",
                ExpectedImprovement: "Giảm failure/blocked/interrupted event của action này mà không nới lỏng permission.",
                Risk: "Không được khắc phục bằng cách tắt guardrail hoặc tự động cấp quyền rộng hơn.");
        }

        return new(
            Problem: $"Self Evaluation phát hiện weakness trong khu vực '{weakness.Area}' với signal '{weakness.Signal}'.",
            Hypothesis: "Cần thêm dữ liệu hồi quy để xác định nguyên nhân trước khi thay đổi implementation.",
            Change: "Tạo hoặc bổ sung regression case tái hiện weakness, xác minh nguyên nhân và chuẩn bị một thay đổi nhỏ có thể rollback.",
            ExpectedImprovement: "Weakness giảm trên benchmark tương ứng mà không tạo regression mới.",
            Risk: "Bằng chứng hiện tại có thể chưa đủ để xác định nguyên nhân; không triển khai thay đổi khi chưa tái hiện được.");
    }

    private static string CreateStableId(SelfEvaluationWeakness weakness)
    {
        var source = $"{weakness.Area}|{weakness.Signal}|{weakness.Severity}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return "proposal-" + Convert.ToHexString(digest)[..12].ToLowerInvariant();
    }

    private static int SeverityRank(string severity) => severity switch
    {
        "high" => 3,
        "medium" => 2,
        _ => 1
    };

    private sealed record ProposalTemplate(
        string Problem,
        string Hypothesis,
        string Change,
        string ExpectedImprovement,
        string Risk);
}
