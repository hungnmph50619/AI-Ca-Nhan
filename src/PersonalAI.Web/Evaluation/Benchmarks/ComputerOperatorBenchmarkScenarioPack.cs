using System.Text.Json;
using PersonalAI.Web.Evaluation.Regression;

namespace PersonalAI.Web.Evaluation.Benchmarks;

public sealed record ComputerOperatorBenchmarkScenarioDefinition(
    string Id,
    string Title,
    string Goal,
    string ExpectedEffect,
    int MaximumSteps,
    bool RequiresInteractiveDesktop,
    string Environment,
    string Skill,
    string Risk);

public sealed record ComputerOperatorBenchmarkScenarioPack(
    string Version,
    string Platform,
    DateTimeOffset DefinedAtUtc,
    IReadOnlyList<ComputerOperatorBenchmarkScenarioDefinition> Scenarios);

public static class ComputerOperatorBenchmarkScenarioCatalog
{
    public const string PackVersion = "v4.9.1";
    public const string Platform = "windows";
    public const int ScenarioCount = 6;

    private static readonly IReadOnlyList<ComputerOperatorBenchmarkScenarioDefinition>
        Scenarios =
        [
            new(
                "windows-notepad-text-entry",
                "Nhập văn bản vào trình soạn thảo native",
                "Mở Notepad, nhập chính xác dòng 'PersonalAI benchmark text entry', xác nhận dòng chữ đã xuất hiện, sau đó đóng Notepad mà không lưu tệp.",
                "Notepad được mở, văn bản benchmark xuất hiện chính xác, rồi ứng dụng được đóng an toàn mà không tạo tệp.",
                24,
                true,
                "Windows có Notepad",
                "native-launch,text-entry,verification,close",
                "low"),
            new(
                "windows-calculator-arithmetic",
                "Tương tác với ứng dụng Calculator",
                "Mở Calculator, tính 37 + 58 và xác nhận kết quả hiển thị là 95, sau đó đóng Calculator.",
                "Calculator hiển thị kết quả 95 trước khi được đóng.",
                24,
                true,
                "Windows có Calculator",
                "native-launch,button-targeting,verification,close",
                "low"),
            new(
                "windows-search-native-launch",
                "Dùng Windows Search để mở ứng dụng native",
                "Mở Windows Search, tìm Calculator, mở đúng ứng dụng Calculator, xác nhận cửa sổ Calculator xuất hiện rồi đóng ứng dụng.",
                "Windows Search dẫn tới đúng ứng dụng Calculator; browser hoặc kết quả web không được coi là thành công.",
                28,
                true,
                "Windows Search và Calculator khả dụng",
                "system-search,app-identity,verification,recovery",
                "low"),
            new(
                "windows-settings-navigation",
                "Điều hướng ứng dụng Settings",
                "Mở Windows Settings, vào mục System nếu chưa ở đó, xác nhận giao diện System hiển thị rồi đóng Settings mà không thay đổi thiết lập.",
                "Windows Settings mở đúng khu vực System và không có thiết lập nào bị thay đổi.",
                28,
                true,
                "Windows Settings khả dụng",
                "native-launch,navigation,structured-ui,verification",
                "low"),
            new(
                "windows-window-focus-switch",
                "Chuyển focus giữa hai ứng dụng",
                "Mở Notepad và Calculator. Chuyển focus sang Notepad, sau đó sang Calculator, xác nhận mỗi lần cửa sổ đích trở thành foreground, rồi đóng cả hai ứng dụng.",
                "Computer Operator chuyển foreground chính xác giữa hai cửa sổ và đóng chúng an toàn.",
                32,
                true,
                "Windows có Notepad và Calculator",
                "multi-window,focus,app-identity,verification",
                "low"),
            new(
                "windows-delayed-native-launch",
                "Chờ ứng dụng native khởi động thay vì timeout sớm",
                "Mở Windows Settings và chờ theo trạng thái cho tới khi giao diện thật sự sẵn sàng. Không báo lỗi chỉ vì cửa sổ chưa xuất hiện ngay; khi Settings sẵn sàng thì xác nhận rồi đóng.",
                "Operator chờ dựa trên evidence, nhận diện Settings khi sẵn sàng và không kết luận thất bại do timeout ngắn cố định.",
                28,
                true,
                "Windows Settings khả dụng",
                "adaptive-wait,native-launch,verification",
                "low")
        ];

    public static ComputerOperatorBenchmarkScenarioPack GetPack() =>
        new(
            PackVersion,
            Platform,
            DateTimeOffset.UnixEpoch,
            Scenarios);

    public static IReadOnlyList<CreateRegressionDatasetItemRequest>
        BuildRegressionCases() =>
        Scenarios
            .Select(ToRegressionCase)
            .ToArray();

    private static CreateRegressionDatasetItemRequest ToRegressionCase(
        ComputerOperatorBenchmarkScenarioDefinition scenario)
    {
        var input =
            JsonSerializer.SerializeToElement(
                new
                {
                    goal = scenario.Goal,
                    expectedEffect = scenario.ExpectedEffect,
                    maximumSteps = scenario.MaximumSteps,
                    requiresInteractiveDesktop =
                        scenario.RequiresInteractiveDesktop
                });

        var expected =
            JsonSerializer.SerializeToElement(
                new
                {
                    expectedEffect = scenario.ExpectedEffect,
                    environment = scenario.Environment,
                    skill = scenario.Skill,
                    risk = scenario.Risk
                });

        return new(
            Id: $"external-{scenario.Id}",
            Title: scenario.Title,
            Category: "computer-operator.external-benchmark",
            Input: input,
            Expected: expected,
            Metadata:
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["packVersion"] = PackVersion,
                    ["platform"] = Platform,
                    ["environment"] = scenario.Environment,
                    ["skill"] = scenario.Skill,
                    ["risk"] = scenario.Risk,
                    ["source"] = "built-in-benchmark-scenario-pack"
                },
            Enabled: true);
    }
}
