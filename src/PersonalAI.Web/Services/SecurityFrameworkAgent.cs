using System.Text;
using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

public static class SecurityAgentLimits
{
    public const int MaximumInputCharacters = AgentFrameworkLimits.MaximumGoalCharacters;
    public const int MaximumFindings = 8;
    private static readonly Lazy<bool> SelfTest = new(SecurityPatternReview.RunSelfTest);

    public static SecurityAgentStatusResponse GetStatus() =>
        new(
            PersonalAiRelease.Version,
            SecurityFrameworkAgent.AgentId,
            MaximumInputCharacters,
            MaximumFindings,
            DeterministicPatternChecks: true,
            SelfTestPassed: SelfTest.Value,
            LocalOnly: true,
            EchoesInput: false,
            IndependentlyAuditsSystems: false,
            CertifiesSafety: false,
            EnforcesPolicy: false,
            ChangesPermissions: false,
            ExecutesTools: false,
            DispatchesAgents: false,
            RequiresExplicitInvocation: true,
            NextStage: "v2.2-agent-orchestration");
}

public sealed class SecurityFrameworkAgent : IAgent
{
    public const string AgentId = "security.security-reviewer";

    public AgentDefinition Definition { get; } = new(
        AgentId,
        "Security Agent",
        "security",
        "Rà soát mẫu rủi ro trong mô tả thao tác do người dùng nhập. Chạy cục bộ, không tự đọc dữ liệu, thực thi, chặn thao tác hoặc chứng nhận an toàn.",
        [
            "local-pattern-review",
            "credential-exposure-warning",
            "destructive-command-warning",
            "privilege-expansion-warning",
            "manual-verification-guidance"
        ],
        [],
        [],
        UsesAiProvider: false,
        ToolExecutionEnabled: false,
        RequiresExplicitInvocation: true);

    public Task<AgentResult> ExecuteAsync(
        AgentExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Only the current goal is scanned. Conversation, workspace, context,
        // credentials, files and external services are not accessed.
        var report = SecurityPatternReview.Review(
            context.Request.Goal ?? string.Empty, DateTimeOffset.UtcNow);

        return Task.FromResult(new AgentResult(
            SecurityPatternReview.Render(report),
            "local",
            "deterministic-patterns",
            [],
            null,
            Security: report));
    }
}

public static class SecurityPatternReview
{
    private sealed record PatternRule(
        string Id, string Severity, string Title,
        string Explanation, string SuggestedCheck,
        Regex Pattern);

    private static Regex Pattern(string pattern) => new(
        pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase
            | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    // Rule messages are constant, never assembled from potentially sensitive
    // source text. A match only indicates a pattern; no input excerpt is returned.
    private static readonly IReadOnlyList<PatternRule> Rules =
    [
        new("credential-material", SecurityFindingSeverities.High,
            "Có dấu hiệu chứa thông tin xác thực",
            "Mô tả khớp một mẫu token, mật khẩu, khóa API hoặc private key. Đây có thể là chuỗi minh họa hoặc dữ liệu thực.",
            "Không dán giá trị bí mật vào agent. Nếu đã lộ khóa thật, dùng quy trình thu hồi/đổi khóa của dịch vụ và kiểm tra phạm vi truy cập.",
            Pattern(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:authorization\s*:\s*bearer|bearer\s+[a-z0-9._~+/-]{8,}|(?:api[_\s-]?key|password|passwd|secret|access[_\s-]?token)\s*[:=]\s*[""']?[^\s,""']{6,}|(?:gh[pousr]_[a-z0-9]{20,}|sk-[a-z0-9_-]{20,}))")),
        new("destructive-action", SecurityFindingSeverities.High,
            "Có dấu hiệu thao tác phá hủy hoặc khó hoàn tác",
            "Mô tả khớp mẫu xóa dữ liệu hàng loạt, format ổ, Git force/reset hoặc câu lệnh tương đương. Không kết luận thao tác chắc chắn nguy hiểm trong mọi hoàn cảnh.",
            "Xác minh chính xác mục tiêu, bản sao lưu, quyền thao tác và phương án khôi phục trước khi dùng công cụ có quyền riêng.",
            Pattern(@"(?:\brm\s+-[a-z]*r[a-z]*f\b|\brm\s+-[a-z]*f[a-z]*r\b|\brmdir\s+/s\b|\bformat\s+[a-z]:|\b(?:drop\s+(?:database|table)|truncate\s+table)\b|\bgit\s+reset\s+--hard\b|\bgit\s+push\b[^\r\n]{0,120}\s--force\b|\bremove-item\b[^\r\n]{0,100}\s-recurse\b|\bshutil\.rmtree\s*\()")),
        new("remote-code-execution", SecurityFindingSeverities.High,
            "Có dấu hiệu chạy mã lấy từ nguồn bên ngoài",
            "Mô tả khớp mẫu tải script rồi chạy trực tiếp hoặc gọi trình thông dịch lệnh từ chuỗi.",
            "Kiểm tra nguồn và nội dung script độc lập trong môi trường cách ly; không chạy pipeline tải-và-thực-thi khi chưa hiểu tác động.",
            Pattern(@"(?:\b(?:curl|wget)\b[^\r\n]{0,200}\|\s*(?:sudo\s+)?(?:bash|sh|zsh)\b|\b(?:invoke-expression|iex)\s*[\(\s])")),
        new("permission-bypass", SecurityFindingSeverities.Medium,
            "Có dấu hiệu mở rộng quyền hoặc bỏ qua kiểm soát",
            "Mô tả khớp mẫu nới quyền truy cập hoặc vô hiệu hóa một số cơ chế bảo vệ.",
            "Kiểm tra nguyên tắc quyền tối thiểu, người phê duyệt và các kiểm soát hiện hành; không tự thay đổi quyền.",
            Pattern(@"(?:\bchmod\s+777\b|\b--no-verify\b|\bdisable\s+(?:audit|firewall|antivirus|logging)\b|\bturn\s+off\s+(?:audit|firewall|antivirus)\b|\bgrant\s+all\s+privileges\b)")),
        new("external-transfer", SecurityFindingSeverities.Medium,
            "Có dấu hiệu chuyển dữ liệu ra ngoài",
            "Mô tả khớp một mẫu upload/chuyển dữ liệu. Chưa xác định nội dung gửi đi, bên nhận hoặc mức độ nhạy cảm.",
            "Kiểm tra phân loại dữ liệu, đích nhận, quyền chia sẻ và sự chấp thuận trước khi truyền dữ liệu.",
            Pattern(@"(?:\b(?:curl)\b[^\r\n]{0,160}(?:--data(?:-binary)?\b|-F\s|--upload-file\b)|\b(?:upload|exfiltrate)\b[^\r\n]{0,80}\b(?:file|data|database|secret|token)\b|\b(?:send|post)\s+(?:a\s+)?(?:file|database)\s+to\b)"))
    ];

    public static SecurityReviewReport Review(string input, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(input)
            || input.Length > SecurityAgentLimits.MaximumInputCharacters)
            throw new AgentValidationException("Mô tả thao tác không được trống hoặc vượt 4.000 ký tự.");

        var findings = new List<SecurityReviewFinding>();
        foreach (var rule in Rules)
        {
            if (!rule.Pattern.IsMatch(input)) continue;
            findings.Add(new SecurityReviewFinding(
                rule.Id, rule.Severity, rule.Title,
                rule.Explanation, rule.SuggestedCheck));
            if (findings.Count >= SecurityAgentLimits.MaximumFindings) break;
        }

        var flagged = findings.Count > 0;
        return new SecurityReviewReport(
            flagged ? SecurityReviewStatuses.PatternsDetected
                : SecurityReviewStatuses.NoPatternDetected,
            flagged
                ? "Có mẫu rủi ro cần người dùng rà soát trước khi thực hiện. Đây không phải kết luận rằng thao tác chắc chắn không an toàn."
                : "Không phát hiện mẫu nào trong bộ quy tắc giới hạn. Điều này KHÔNG xác nhận thao tác an toàn hoặc được phép.",
            findings,
            [
                "Chỉ rà soát mô tả thao tác người dùng nhập; không đọc repo, file, hệ thống, quyền thực tế, logs hoặc trạng thái công cụ.",
                "Bộ mẫu giới hạn có thể bỏ sót rủi ro hoặc cảnh báo nhầm; không thay thế đánh giá bảo mật, permission gate hay kiểm tra thủ công.",
                "Không lặp lại nội dung người dùng, token, password hoặc chuỗi trùng mẫu trong báo cáo; không gửi nội dung đến AI provider."
            ],
            LocalOnly: true,
            InputEchoed: false,
            IndependentlyAudited: false,
            SafetyCertified: false,
            EnforcesPolicy: false,
            ChangedPermissions: false,
            ExecutedTools: false,
            DispatchedAgents: false,
            CreatedAt: now);
    }

    public static bool RunSelfTest()
    {
        try
        {
            var credential = Review("api_key=SuperPrivateValue12345", DateTimeOffset.UnixEpoch);
            var output = Render(credential);
            if (credential.Status != SecurityReviewStatuses.PatternsDetected
                || !credential.Findings.Any(x => x.RuleId == "credential-material")
                || output.Contains("SuperPrivateValue12345", StringComparison.Ordinal)
                || credential.InputEchoed || !credential.LocalOnly
                || credential.SafetyCertified || credential.EnforcesPolicy)
                return false;

            var destructive = Review("rm -rf ./important", DateTimeOffset.UnixEpoch);
            if (!destructive.Findings.Any(x => x.RuleId == "destructive-action"))
                return false;

            var neutral = Review("Đọc tài liệu về mô hình phân quyền.", DateTimeOffset.UnixEpoch);
            return neutral.Status == SecurityReviewStatuses.NoPatternDetected
                && neutral.Findings.Count == 0
                && !neutral.SafetyCertified && !neutral.IndependentlyAudited;
        }
        catch
        {
            return false;
        }
    }

    public static string Render(SecurityReviewReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BÁO CÁO RÀ SOÁT MẪU BẢO MẬT — KHÔNG PHẢI PHÊ DUYỆT");
        builder.AppendLine(report.Summary);
        foreach (var finding in report.Findings)
        {
            builder.Append("- ").Append(finding.Title)
                .Append(" [").Append(finding.RuleId).Append(": ")
                .Append(finding.Severity).AppendLine("]");
            builder.Append("  ").AppendLine(finding.Explanation);
            builder.Append("  Kiểm tra thủ công: ").AppendLine(finding.SuggestedCheck);
        }
        foreach (var limitation in report.Limitations)
            builder.Append("- Giới hạn: ").AppendLine(limitation);
        builder.AppendLine("Chưa chạy công cụ, thay đổi quyền, chặn thao tác hay cấp chứng nhận an toàn.");
        return builder.ToString().TrimEnd();
    }
}
