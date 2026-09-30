# Bộ tự phát triển theo roadmap — đa nhà cung cấp và tiếp tục sau gián đoạn

Đây là lớp hỗ trợ phát triển các phiên bản sau v2.5.3. Nó không thay thế roadmap sản phẩm và không tự merge/deploy production.

## Mục tiêu

Một vòng phát triển được điều phối theo thứ tự:

1. xác định phiên bản kế tiếp;
2. tạo/sử dụng branch `experiment/roadmap-v...`;
3. thu thập source context giới hạn;
4. gọi nhà cung cấp AI để đề xuất file edits;
5. ghi file có kiểm tra SHA khi overwrite;
6. chạy `dotnet restore`;
7. chạy `dotnet build -c Release`;
8. chạy `dotnet test -c Release` khi được yêu cầu;
9. nếu thất bại, gửi log và các file đã đổi cho AI để sửa lại;
10. chỉ trả `ready-for-commit` khi restore/build/test đạt và version file đã được cập nhật.

Push, merge và deploy vẫn tắt.

## Chuyển nhà cung cấp AI

`AutopilotProviderRouter` không khóa autopilot vào OpenAI.

Thứ tự ưu tiên:

- nhà cung cấp AI đang được chọn trong Cài đặt AI;
- OpenAI nếu chưa thử;
- Gemini nếu chưa thử.

Chỉ provider đã cấu hình mới được gọi.

Khi provider gặp lỗi tạm thời như rate limit, timeout hoặc lỗi máy chủ, router thử provider tiếp theo. Nếu tất cả provider đều không dùng được, lượt chạy dừng an toàn.

Việc chuyển provider không có nghĩa là dùng giao diện chatgpt.com làm backend. Autopilot chỉ dùng các provider/API đã cấu hình trong ứng dụng.

## Checkpoint / Resume

Checkpoint được lưu ngoài source repository tại:

`%LOCALAPPDATA%/PersonalAI/DevelopmentAutopilot/`

Có thể override bằng cấu hình `DevelopmentAutopilot:Root`.

Checkpoint chỉ lưu metadata phát triển:

- workspace;
- target version;
- branch;
- stage;
- attempt;
- provider/model cuối;
- danh sách file đã đổi + SHA;
- kết quả pass/fail restore/build/test.

Không lưu API key/token trong checkpoint.

Khi một lượt bị gián đoạn sau khi đã sửa file, lần chạy kế tiếp có thể tiếp tục từ các file đã thay đổi và bước verify/repair thay vì bắt đầu lại từ đầu.

## API

- `GET /api/development-autopilot/status`
- `GET /api/development-autopilot/providers`
- `GET /api/development-autopilot/checkpoint`
- `DELETE /api/development-autopilot/checkpoint?confirmed=true`
- `GET /api/development-autopilot/next`
- `POST /api/development-autopilot/run-next`

## Ranh giới an toàn hiện tại

Autopilot hiện vẫn:

- yêu cầu xác nhận gửi source/log tới AI;
- yêu cầu xác nhận tạo branch;
- yêu cầu xác nhận sửa file;
- chỉ dùng branch experiment;
- không sửa `.git`, `.github`, credential/secret;
- không commit;
- không push;
- không merge;
- không deploy production;
- giới hạn tối đa 3 vòng sửa lỗi.

Các quyền GitHub tự động sẽ chỉ được mở dần ở các phiên bản roadmap tương ứng.
