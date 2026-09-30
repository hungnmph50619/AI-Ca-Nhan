# Bộ tự phát triển theo roadmap

Khung này cho phép PersonalAI dùng **OpenAI đã cấu hình trong Cài đặt AI** để triển khai phiên bản kế tiếp trong roadmap theo một vòng có kiểm soát.

## Luồng hiện tại

1. Đọc `PersonalAiRelease.Version`.
2. Chọn đúng phiên bản tiếp theo trong catalog.
3. Kiểm tra repo đang ở `main/master` hoặc đúng nhánh thử nghiệm.
4. Tạo nhánh `experiment/roadmap-vX-X-X`.
5. Tìm các file liên quan trong workspace.
6. Gửi yêu cầu + source liên quan sang OpenAI khi người dùng đã xác nhận.
7. Chỉ cho AI tạo/ghi đè file trong `src/`, `tests/`, `docs/` hoặc solution.
8. Chạy:
   - `dotnet restore`
   - `dotnet build -c Release`
   - `dotnet test -c Release`
9. Nếu lỗi, gửi log và các file đã thay đổi lại cho OpenAI để sửa, tối đa 3 vòng.
10. Chỉ trả `ready-for-commit` khi restore/build/test đều đạt **và** file version đã được đổi sang phiên bản mục tiêu.

## API

- `GET /api/development-autopilot/status`
- `GET /api/development-autopilot/next`
- `POST /api/development-autopilot/run-next`

Ví dụ body:

```json
{
  "repositoryPath": ".",
  "dotnetTargetPath": "PersonalAI.sln",
  "baseBranch": "main",
  "confirmExternalAi": true,
  "confirmBranchCreation": true,
  "confirmFileChanges": true,
  "runTests": true,
  "maximumRepairAttempts": 2
}
```

## Điều kiện máy local

Workspace của PersonalAI phải trỏ vào **thư mục gốc repo AI-Ca-Nhan** để bộ điều khiển nhìn thấy `PersonalAI.sln`, `src/`, `tests/` và Git repository.

OpenAI phải được cấu hình trong **Cài đặt AI**. Source code và log chỉ được gửi ra provider sau khi request có `confirmExternalAi=true`.

## Giới hạn an toàn hiện tại

Bản bootstrap này cố ý KHÔNG:

- commit tự động;
- push tự động;
- tạo PR;
- merge `main`;
- deploy production;
- sửa `.github/` hoặc `.git/`;
- force push/reset;
- đọc hoặc ghi private key/secret file.

Kết quả tốt nhất hiện tại là `ready-for-commit`. Các quyền Git mạnh sẽ được mở dần ở các mốc Git/CI trong roadmap sau khi có locking, working-tree safety và policy.

## Quy tắc hoàn thành phiên bản

Không được coi một phiên bản là xong chỉ vì AI đã viết code. Phải đồng thời đạt:

- restore thành công;
- build thành công;
- test thành công;
- version file đúng phiên bản mục tiêu;
- không vượt giới hạn vòng tự sửa;
- không vi phạm vùng file bị cấm.

Đây là nền tảng để sau này thêm commit/push/PR/CI mà không bỏ qua kiểm thử.
