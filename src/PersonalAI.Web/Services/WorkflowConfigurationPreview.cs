using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

/// <summary>
/// Bản xem trước chỉ mô tả cấu hình do người dùng nhập; không gọi agent hay AI.
/// Dấu kiểm tra cấu hình chỉ chống việc vô tình thay đổi sau khi xem trước,
/// không phải mã xác thực hoặc quyền thực thi.
/// </summary>
public static class WorkflowConfigurationPreview
{
    public sealed record PreviewStep(
        int Step,
        string AgentId,
        int GoalCharacters,
        bool IncludePreviousOutput,
        bool CanUseAiProvider);

    public sealed record PreviewResponse(
        string Version,
        string WorkspaceId,
        string ConfigurationDigest,
        IReadOnlyList<PreviewStep> Steps,
        bool UseKnowledge,
        bool UseMemory,
        bool UseTaskContext,
        bool UseLifeContext,
        bool PerformsActions,
        string Notice);

    public static PreviewResponse Build(
        AgentWorkflowRequest request,
        string workspaceId,
        Func<string, AgentDefinition?> getAgent)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var steps = AgentWorkflowValidation.Validate(
            request with { ConfirmSelectedWorkflow = true },
            id => getAgent(id) is not null);

        var summaries = steps
            .Select((step, index) => new PreviewStep(
                index + 1,
                step.AgentId!,
                step.Goal!.Length,
                step.IncludePreviousOutput,
                getAgent(step.AgentId!)?.UsesAiProvider ?? false))
            .ToArray();

        return new PreviewResponse(
            PersonalAiRelease.Version,
            workspaceId,
            Digest(request with { Steps = steps }, workspaceId),
            summaries,
            request.UseKnowledge,
            request.UseMemory,
            request.UseTaskContext,
            request.UseLifeContext,
            PerformsActions: false,
            Notice: "Đây chỉ là bản xem trước cấu hình, chưa chạy tác nhân. Sau khi xem, hãy xác nhận đúng danh sách bước, mục tiêu và nguồn dữ liệu trước khi chạy.");
    }

    public static string Digest(AgentWorkflowRequest request, string workspaceId)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            WorkspaceId = workspaceId,
            Steps = (request.Steps ?? [])
                .Select(step => new
                {
                    AgentId = step.AgentId?.Trim(),
                    Goal = step.Goal?.Trim(),
                    step.IncludePreviousOutput
                })
                .ToArray(),
            request.UseKnowledge,
            request.UseMemory,
            request.UseTaskContext,
            request.UseLifeContext
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static void ValidateDigest(AgentWorkflowRequest request, string workspaceId)
    {
        // Older API consumers remain compatible when the optional digest is absent.
        // The user-facing v2.2.5 UI always supplies a digest after showing preview.
        if (request.ReviewedConfigurationDigest is null) return;

        var expected = Digest(request, workspaceId);
        if (request.ReviewedConfigurationDigest.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(request.ReviewedConfigurationDigest),
                Encoding.ASCII.GetBytes(expected)))
            throw new AgentValidationException(
                "Cấu hình đã thay đổi kể từ lúc xem trước. Hãy kiểm tra lại toàn bộ bước và xác nhận một lần nữa.");
    }

    public static bool RunSelfTest()
    {
        try
        {
            var initial = new AgentWorkflowRequest(
                [new("security.security-reviewer", "Bước một"),
                 new("security.security-reviewer", "Bước hai", true)]);
            AgentDefinition? Resolve(string id) =>
                id == "security.security-reviewer"
                    ? new AgentDefinition(id, "Bảo mật", "security", "Kiểm tra",
                        [], [], [], false, false, true)
                    : null;
            var preview = Build(initial, "personal", Resolve);
            ValidateDigest(initial with
            {
                ConfirmSelectedWorkflow = true,
                ReviewedConfigurationDigest = preview.ConfigurationDigest
            }, "personal");

            foreach (var changed in new[]
            {
                initial with { UseKnowledge = true },
                initial with { Steps = [initial.Steps![0], initial.Steps[1] with { Goal = "Bước đã thay đổi" }] }
            })
            {
                try
                {
                    ValidateDigest(changed with
                    {
                        ReviewedConfigurationDigest = preview.ConfigurationDigest
                    }, "personal");
                    return false;
                }
                catch (AgentValidationException)
                {
                    // Dấu kiểm tra cũ không được áp dụng cho cấu hình mới.
                }
            }

            return preview.Steps.Count == 2
                && !preview.PerformsActions
                && preview.Steps[1].IncludePreviousOutput
                && preview.ConfigurationDigest.Length == 64
                && !preview.Notice.Contains("token", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
