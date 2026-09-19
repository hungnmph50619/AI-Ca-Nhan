using PersonalAI.Web.Models;

namespace PersonalAI.Web.Services;

/// <summary>
/// Chẩn đoán chỉ dựa trên siêu dữ liệu, không đọc mục tiêu, nội dung đầu ra,
/// dữ liệu chuyển giao hay mã xác nhận. Không tự thực hiện hoặc tiếp tục quy trình.
/// </summary>
public static class WorkflowDiagnostics
{
    public static WorkflowDiagnosticsStatusResponse GetStatus() =>
        new(PersonalAiRelease.Version,
            ReadOnly: true,
            WorkspaceIsolated: true,
            MemoryOnly: true,
            ContainsGoals: false,
            ContainsOutputs: false,
            ContainsTokens: false,
            GrantsExecutionRights: false,
            SelfTestPassed: RunSelfTest(),
            NextStage: "v2.2.5-workflow-usability");

    public static WorkflowDiagnosis Explain(WorkflowObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var (statusLabel, summary, checks) = observation.Status switch
        {
            AgentWorkflowStatuses.Completed =>
                ("Đã hoàn tất", "Các bước đã chọn đã hoàn tất theo báo cáo của hệ thống.",
                    new[] { "Tự đối chiếu từng kết quả với mục tiêu ban đầu trước khi sử dụng." }),
            AgentWorkflowStatuses.AwaitingReview =>
                ("Đang chờ bạn duyệt",
                    "Quy trình đã tạm dừng để bạn xem nội dung trước khi chuyển sang bước tiếp theo.",
                    new[] { "Quay lại màn hình đang chờ duyệt và kiểm tra kỹ dữ liệu sẽ chuyển.",
                        "Chỉ đồng ý khi bạn chấp nhận việc chia sẻ nội dung đó với tác nhân tiếp theo.",
                        "Nếu đã đóng màn hình hoặc máy chủ khởi động lại, có thể cần tạo quy trình mới." }),
            AgentWorkflowStatuses.TimedOut =>
                ("Hết thời gian", "Quy trình đã dừng vì hết thời gian cho phép.",
                    new[] { "Kiểm tra kết quả các bước đã hoàn thành.",
                        "Giảm độ phức tạp của mục tiêu hoặc thực hiện từng bước riêng.",
                        "Không tự chạy lại các thao tác có thể gây thay đổi dữ liệu." }),
            AgentWorkflowStatuses.Stopped =>
                ("Đã dừng", "Quy trình chưa hoàn thành toàn bộ các bước đã chọn.",
                    new[] { "Xem bước bị dừng và kết quả những bước trước đó.",
                        "Khắc phục nguyên nhân được mô tả dưới đây trước khi tạo quy trình mới." }),
            _ => ("Chưa xác định", "Chưa có đủ siêu dữ liệu để giải thích trạng thái này.",
                    new[] { "Kiểm tra lại trạng thái quy trình trong ứng dụng." })
        };

        var (reasonLabel, extra) = observation.StopReason switch
        {
            "timeout" => ("Hết thời gian",
                "Bước hoặc toàn bộ quy trình đã vượt giới hạn thời gian được đặt."),
            "validation-blocked" => ("Dữ liệu hoặc điều kiện không hợp lệ",
                "Hệ thống đã dừng trước bước tiếp theo; không xác định nguyên nhân cụ thể chỉ từ siêu dữ liệu."),
            "step-failed" => ("Một bước không hoàn thành",
                "Bước được gọi có lỗi; cần xem kết quả và trạng thái của tác nhân liên quan."),
            "review-declined" => ("Bạn đã từ chối chuyển dữ liệu",
                "Quy trình đã dừng theo quyết định không chia sẻ dữ liệu ở bước duyệt."),
            _ => ((string?)null, (string?)null)
        };

        var suggestions = checks.ToList();
        if (extra is not null) suggestions.Add(extra);

        return new WorkflowDiagnosis(
            observation.WorkflowId,
            observation.WorkspaceId,
            observation.Status,
            statusLabel,
            summary,
            observation.StopReason is "timeout" or "validation-blocked" or "step-failed" or "review-declined"
                ? observation.StopReason : null,
            reasonLabel,
            observation.RequestedSteps,
            observation.CompletedSteps,
            observation.FailedStep,
            suggestions,
            [
                "Đây chỉ là giải thích dựa trên trạng thái và số bước; không đọc nội dung, tệp, mục tiêu hoặc ngoại lệ thực tế.",
                "Không tự phân tích mã nguồn, tự sửa lỗi, cấp quyền, tiếp tục hoặc chạy lại quy trình.",
                "Thông tin chỉ được giữ tạm trong bộ nhớ máy chủ và không đại diện cho kiểm tra độc lập."
            ],
            ContainsGoals: false,
            ContainsOutputs: false,
            ContainsTokens: false,
            GrantsExecutionRights: false,
            observation.LastUpdatedAt);
    }

    public static bool RunSelfTest()
    {
        const string privateValue = "KhongDuocLoGiaTriBiMat1234";
        var observation = new WorkflowObservation(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "personal",
            AgentWorkflowStatuses.Stopped,
            3, 1, 2, privateValue, false,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0);
        var diagnosis = Explain(observation);
        return diagnosis.StatusLabel == "Đã dừng"
            && diagnosis.StopReason is null
            && !diagnosis.ToString().Contains(privateValue, StringComparison.Ordinal)
            && !diagnosis.ContainsGoals && !diagnosis.ContainsOutputs
            && !diagnosis.ContainsTokens && !diagnosis.GrantsExecutionRights
            && Explain(observation with { StopReason = "review-declined" })
                .StopReasonLabel == "Bạn đã từ chối chuyển dữ liệu";
    }
}
