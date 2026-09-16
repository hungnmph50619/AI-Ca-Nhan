namespace PersonalAI.Web.Teams;

public interface ITeamProfileCatalog
{
    string DefaultProfileId { get; }
    IReadOnlyList<TeamProfile> GetAll();
}

public sealed class TeamProfileCatalog : ITeamProfileCatalog
{
    private static readonly IReadOnlyList<TeamProfile> Profiles =
    [
        new(
            "personal-assistant",
            "Trợ lý cá nhân",
            "Hỏi đáp và hỗ trợ công việc cá nhân hằng ngày.",
            [new("assistant", "Trợ lý AI", "Trả lời, phân tích và đề xuất.")]),
        new(
            "software-development",
            "Đội lập trình",
            "Hồ sơ đội mẫu cho các giai đoạn phát triển sau.",
            [
                new("manager", "AI Manager", "Chia việc và tổng hợp kết quả."),
                new("analyst", "Phân tích", "Làm rõ yêu cầu và tiêu chí hoàn thành."),
                new("developer", "Lập trình", "Thiết kế và thay đổi mã nguồn."),
                new("tester", "Kiểm thử", "Build, kiểm thử và phát hiện lỗi."),
                new("reviewer", "Kiểm duyệt", "Rà soát chất lượng, bảo mật và rủi ro.")
            ])
    ];

    public string DefaultProfileId => "personal-assistant";

    public IReadOnlyList<TeamProfile> GetAll() => Profiles;
}
