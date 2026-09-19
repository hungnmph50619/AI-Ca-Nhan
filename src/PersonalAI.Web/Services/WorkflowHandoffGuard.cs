using System.Text.RegularExpressions;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

/// <summary>
/// Conservative, local handoff gate. It reduces accidental transfer of recognizable
/// credentials; it is not DLP, authorization, or protection against prompt injection.
/// </summary>
public static class WorkflowHandoffGuard
{
    public const int MaximumWorkflowSeconds = 120;
    public const int MaximumStepSeconds = 50;
    public const int MaximumHandoffCharacters = AgentOrchestrationLimits.MaximumTransferredCharacters;

    private static readonly Regex SensitivePattern = new(
        @"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:authorization\s*:\s*bearer\s+\S+|bearer\s+[a-z0-9._~+/-]{8,}|(?:api[_\s-]?key|password|passwd|access[_\s-]?token|client[_\s-]?secret)\s*[:=]\s*[""']?[^\s,""']{6,}|gh[pousr]_[a-z0-9]{20,}|sk-[a-z0-9_-]{20,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    public static string Prepare(AgentWorkflowStepRequest step, string? priorOutput)
    {
        if (!step.IncludePreviousOutput)
            return AgentWorkflowValidation.BuildGoal(step, null);
        if (string.IsNullOrWhiteSpace(priorOutput))
            throw new AgentValidationException("Bước trước không có nội dung để chuyển.");

        // Reject *the entire* prior output if a recognizable credential occurs,
        // including beyond the 1,600-character handoff window.
        // Do not return matching text or excerpts in errors/audit records.
        var sensitive = false;
        try
        {
            sensitive = SensitivePattern.IsMatch(priorOutput);
        }
        catch (RegexMatchTimeoutException)
        {
            sensitive = true; // Fail closed if inspection itself times out.
        }

        if (sensitive)
            throw new AgentValidationException(
                "Không chuyển đầu ra vì có dấu hiệu thông tin xác thực. Hãy rà soát dữ liệu riêng trước khi thử lại.");

        return AgentWorkflowValidation.BuildGoal(step, priorOutput);
    }

    public static bool RunSelfTest()
    {
        var step = new AgentWorkflowStepRequest(
            "security.security-reviewer", "Review previous step", IncludePreviousOutput: true);
        try
        {
            var normal = Prepare(step, "Normal read-only workflow output");
            if (!normal.Contains("Normal read-only workflow output", StringComparison.Ordinal))
                return false;

            // Check beyond handoff truncation: a credential later in the full output
            // must never be ignored just because it would not reach the next agent.
            foreach (var sensitive in new[]
            {
                "api_key=DefinitelySecretValue1234",
                new string('A', MaximumHandoffCharacters + 100)
                    + " password=SecretAfterHandoffWindow1234",
                "Authorization: Bearer abcdef1234567890abcdef"
            })
            {
                try
                {
                    Prepare(step, sensitive);
                    return false;
                }
                catch (AgentValidationException exception)
                {
                    if (exception.Message.Contains("SecretValue", StringComparison.Ordinal)
                        || exception.Message.Contains("SecretAfter", StringComparison.Ordinal))
                        return false;
                }
            }

            var untouched = Prepare(step with { IncludePreviousOutput = false },
                "api_key=DefinitelySecretValue1234");
            return untouched == step.Goal
                && MaximumStepSeconds < MaximumWorkflowSeconds;
        }
        catch
        {
            return false;
        }
    }
}
