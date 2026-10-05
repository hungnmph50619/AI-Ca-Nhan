using System.Security.Cryptography;
using System.Text;

namespace PersonalAI.Web.Services;

public static class GeminiCallKinds
{
    public const string Planning = "planning";
    public const string Verification = "verification";
}

public sealed record GeminiCallBudgetPolicy(
    int MaximumPlanningCalls,
    int MaximumVerificationCalls,
    int MaximumTotalCalls,
    int MaximumRepeatedVerificationCalls)
{
    public static GeminiCallBudgetPolicy AccuracyFirst { get; } =
        new(
            MaximumPlanningCalls: 12,
            MaximumVerificationCalls: 8,
            MaximumTotalCalls: 18,
            MaximumRepeatedVerificationCalls: 2);
}

public sealed record GeminiCallBudgetDecision(
    bool Allowed,
    string Kind,
    int PlanningCalls,
    int VerificationCalls,
    int TotalCalls,
    int RemainingTotalCalls,
    string Reason);

public sealed record GeminiCallBudgetSnapshot(
    int PlanningCalls,
    int VerificationCalls,
    int TotalCalls,
    int RemainingPlanningCalls,
    int RemainingVerificationCalls,
    int RemainingTotalCalls,
    int RepeatedVerificationKeys);

public interface IComputerOperatorGeminiBudgetSession
{
    GeminiCallBudgetDecision TryReservePlanning(
        string reason);

    GeminiCallBudgetDecision TryReserveVerification(
        string action,
        string? expectedEffect,
        string reason);

    GeminiCallBudgetSnapshot GetSnapshot();
}

public interface IComputerOperatorGeminiBudgetFactory
{
    IComputerOperatorGeminiBudgetSession Create(
        GeminiCallBudgetPolicy? policy = null);
}

public sealed class ComputerOperatorGeminiBudgetFactory
    : IComputerOperatorGeminiBudgetFactory
{
    public IComputerOperatorGeminiBudgetSession Create(
        GeminiCallBudgetPolicy? policy = null) =>
        new ComputerOperatorGeminiBudgetSession(
            policy ?? GeminiCallBudgetPolicy.AccuracyFirst);
}

public sealed class ComputerOperatorGeminiBudgetSession(
    GeminiCallBudgetPolicy policy)
    : IComputerOperatorGeminiBudgetSession
{
    private readonly object sync = new();
    private readonly Dictionary<string, int> verificationKeyCounts =
        new(StringComparer.Ordinal);
    private int planningCalls;
    private int verificationCalls;

    public GeminiCallBudgetDecision TryReservePlanning(
        string reason)
    {
        lock (sync)
        {
            if (planningCalls >=
                policy.MaximumPlanningCalls)
            {
                return Deny(
                    GeminiCallKinds.Planning,
                    "Đã dùng hết ngân sách Gemini planning của task; dừng an toàn thay vì tiếp tục gọi bộ não lớn.");
            }

            if (TotalCalls >=
                policy.MaximumTotalCalls)
            {
                return Deny(
                    GeminiCallKinds.Planning,
                    "Đã dùng hết tổng ngân sách Gemini của task; không được gọi thêm.");
            }

            planningCalls++;

            return Allow(
                GeminiCallKinds.Planning,
                string.IsNullOrWhiteSpace(reason)
                    ? "Cho phép Gemini planning vì task cần quyết định bước tiếp theo."
                    : reason);
        }
    }

    public GeminiCallBudgetDecision TryReserveVerification(
        string action,
        string? expectedEffect,
        string reason)
    {
        lock (sync)
        {
            if (verificationCalls >=
                policy.MaximumVerificationCalls)
            {
                return Deny(
                    GeminiCallKinds.Verification,
                    "Đã dùng hết ngân sách Gemini verification; phải quay về local evidence/replan.");
            }

            if (TotalCalls >=
                policy.MaximumTotalCalls)
            {
                return Deny(
                    GeminiCallKinds.Verification,
                    "Đã dùng hết tổng ngân sách Gemini của task; không được gọi semantic verifier thêm.");
            }

            var key =
                BuildVerificationKey(
                    action,
                    expectedEffect);

            verificationKeyCounts.TryGetValue(
                key,
                out var repeated);

            if (repeated >=
                policy.MaximumRepeatedVerificationCalls)
            {
                return Deny(
                    GeminiCallKinds.Verification,
                    "Cùng một action + expected effect đã gọi Gemini verification quá nhiều lần; phải đổi chiến lược hoặc quan sát lại.");
            }

            verificationKeyCounts[key] =
                repeated + 1;
            verificationCalls++;

            return Allow(
                GeminiCallKinds.Verification,
                string.IsNullOrWhiteSpace(reason)
                    ? "Cho phép Gemini verification vì structured/local evidence chưa đủ."
                    : reason);
        }
    }

    public GeminiCallBudgetSnapshot GetSnapshot()
    {
        lock (sync)
        {
            return new(
                planningCalls,
                verificationCalls,
                TotalCalls,
                Math.Max(
                    0,
                    policy.MaximumPlanningCalls -
                    planningCalls),
                Math.Max(
                    0,
                    policy.MaximumVerificationCalls -
                    verificationCalls),
                Math.Max(
                    0,
                    policy.MaximumTotalCalls -
                    TotalCalls),
                verificationKeyCounts.Count);
        }
    }

    private int TotalCalls =>
        planningCalls +
        verificationCalls;

    private GeminiCallBudgetDecision Allow(
        string kind,
        string reason) =>
        new(
            Allowed: true,
            kind,
            planningCalls,
            verificationCalls,
            TotalCalls,
            Math.Max(
                0,
                policy.MaximumTotalCalls -
                TotalCalls),
            reason);

    private GeminiCallBudgetDecision Deny(
        string kind,
        string reason) =>
        new(
            Allowed: false,
            kind,
            planningCalls,
            verificationCalls,
            TotalCalls,
            Math.Max(
                0,
                policy.MaximumTotalCalls -
                TotalCalls),
            reason);

    private static string BuildVerificationKey(
        string action,
        string? expectedEffect)
    {
        var normalized =
            $"{(action ?? string.Empty).Trim().ToLowerInvariant()}|{(expectedEffect ?? string.Empty).Trim()}";

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    normalized)));
    }
}
