# Phím tắt dừng điều khiển trên Windows — bản thử nghiệm

Bản này nối tiếp PR #45, #46 và #47. Dành cho ứng dụng Windows đang chạy trong phiên người dùng tương tác; không thay đổi phiên bản đang chạy trên máy người dùng cho đến khi hợp nhất và cài đặt lại.

## Cách hoạt động
- Khi khởi động ứng dụng, bộ nhận phím trên **luồng Windows riêng**, không phụ thuộc trình duyệt, thử đăng ký `Ctrl + Shift + F12` bằng `RegisterHotKey`.
- Nếu đăng ký thành công, API `/api/computer/status` báo `stopHotkeyAvailable: true`, giao diện hiện phím sẵn sàng; người dùng có thể chủ động bật tối đa 60 giây/5 thao tác như PR #47.
- Khi nhấn tổ hợp phím trong desktop Windows thông thường, luồng nhận thông điệp khóa chốt điều khiển: từ chối các thao tác mới đi qua `ComputerControlGate`. Người dùng phải mở lại phiên và xác nhận từng công cụ riêng.
- Nếu đăng ký phím thất bại, mất hàng đợi nhận phím hoặc ứng dụng đang tắt, chốt tự khóa, API cho phép điều khiển từ chối yêu cầu. Không âm thầm hoạt động nếu thiếu khả năng dừng này.
- Khi tắt ứng dụng, luồng phím được báo thoát, quyền bị khóa và phím được trả về cho Windows.

## Giới hạn cần hiểu đúng
- Đây **không** phải phím dừng khẩn cấp độc lập ở cấp hệ điều hành. Nếu ứng dụng treo/crash, lệnh Windows đã được gửi, hoặc máy đang ở màn hình bảo mật/UAC, phím có thể không có tác dụng; cần dừng tiến trình ứng dụng thủ công khi cần.
- Thao tác đã bắt đầu vẫn có thể hoàn tất trước khi chốt dừng nhận được khóa. Không hoàn tác nhấp chuột, không ngắt được ứng dụng khác.
- Nếu tổ hợp phím đã bị chương trình khác chiếm, hệ thống sẽ không cho phép bật điều khiển. Không giả định phím luôn đăng ký được.
- Endpoint cục bộ và header xác nhận hiện chưa phải xác thực người dùng hoàn chỉnh: chỉ chạy ở loopback, không mở máy chủ ra LAN/Internet.
- Bản này chưa bổ sung chuột phải, kéo thả, bàn phím, chụp màn hình hay vòng lặp AI. Chỉ thử tác vụ điều khiển trên cửa sổ thử nghiệm.

## Kiểm thử
- CI Linux xác nhận phím không khả dụng và trạng thái mặc định khóa; không bật quyền khi chạy ngoài Windows.
- CI Windows kiểm tra điều khiển chỉ bật nếu bộ nhận phím đã đăng ký; nếu không đăng ký được, API phải từ chối bật. Các bài kiểm thử không gửi sự kiện nhấp chuột hay phím tắt thật đến desktop của máy CI.
- **Bắt buộc thử trên máy Windows thật trước khi hợp nhất:** chạy ứng dụng, xác nhận phím hiển thị sẵn sàng, bật phiên điều khiển, nhấn `Ctrl + Shift + F12` khi đang mở cửa sổ thử nghiệm, sau đó kiểm tra trạng thái khóa và thử yêu cầu di chuyển chuột (phải bị từ chối).
