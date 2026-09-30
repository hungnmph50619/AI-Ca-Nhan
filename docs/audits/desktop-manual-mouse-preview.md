# Thử nghiệm: nhấp chuột trái đơn lẻ có kiểm soát

Bản này xây trên nhánh rà soát chức năng còn thiếu; không nâng số phiên bản sản phẩm và chưa đưa vào bản đang chạy của người dùng. Mục tiêu hẹp là kiểm tra một thao tác chuột có xác nhận, **không** mở quyền cho AI tự điều khiển máy liên tục.

## Đã thêm
- Công cụ `computer.mouse.click-left` nhận ID cửa sổ và tọa độ trên màn hình ảo. Kiểm tra cửa sổ đích còn hiển thị, đang ở phía trước và tọa độ nằm trong cửa sổ, trước khi gửi một lần nhấn–nhả bằng Windows SendInput.
- Cơ chế tạm dừng từ PR #45 khóa thao tác mặc định mỗi lần khởi động; nếu đang dừng, lệnh bị từ chối ngay trong dịch vụ Windows. Chỉ người dùng tại máy cục bộ mới có thể yêu cầu bật lại trong giao diện, với xác nhận thủ công. Mỗi lần nhấp còn cần quyền `WRITE`, `COMPUTER`, `SENSITIVE` và cờ xác nhận riêng qua chính sách công cụ.
- Không có vòng lặp AI, tự động chọn mục tiêu hoặc gửi nhiều lần nhấp. Không có nhấp phải/đúp, kéo thả, cuộn, bàn phím hay chụp màn hình.
- Kiểm thử hồi quy: chính sách từ chối lệnh thiếu quyền/xác nhận, chốt dừng từ chối nhấp dù tham số công cụ đã được xác nhận. Kiểm thử không gửi sự kiện nhấp chuột thật vào máy chạy CI.

## Giới hạn đặc biệt
- Nhấp chuột có thể gây tác động ngay trong chương trình khác, kể cả kích hoạt nút xóa hoặc gửi dữ liệu. **Chỉ thử trên cửa sổ trống hoặc ứng dụng thử nghiệm**, tuyệt đối không dùng với giao diện đang chứa tài liệu quan trọng, ngân hàng, email hoặc mật khẩu.
- Nút dừng hiện tại không phải phím dừng khẩn cấp độc lập. Nếu Windows đã tiếp nhận thao tác trước khi nhấn Dừng, tác động vẫn có thể xảy ra. Máy chủ không được công khai ra Internet/LAN; header đồng ý cục bộ không phải xác thực mạnh.
- Kiểm tra cửa sổ/điểm nhấp diễn ra trước khi gọi SendInput; giao diện có thể thay đổi sau kiểm tra nên **không bảo đảm mục tiêu pixel là cùng một nút**. Chưa có phép đọc cây giao diện hoặc ảnh chụp để xác nhận kết quả.
- Chỉ hỗ trợ một thao tác chủ động có xác nhận. Các tác nhân trong v2.2.6 vẫn không có quyền gọi công cụ trong quy trình điều phối.
- Kiểm thử Windows trong CI xác nhận quyền và từ chối khi dừng, nhưng không chứng minh thao tác nhấp thực tế hoặc chức năng bàn phím. Cần thử thủ công trên thiết bị thử nghiệm trước khi hợp nhất.

## Các bước tiếp theo
1. Bổ sung dừng độc lập và giới hạn phiên điều khiển có thể thu hồi.
2. Thêm nhấp/phím đơn lẻ với xác nhận từng hành động, đảm bảo chặn được nội dung nhạy cảm và trạng thái cửa sổ bất ngờ.
3. Thêm đọc cây giao diện và chụp màn hình có chọn vùng, chỉ với sự đồng ý rõ ràng, không lưu hoặc chuyển ảnh ra ngoài theo mặc định.
4. Chỉ xây vòng lặp quan sát–hành động sau khi hoàn tất kiểm thử cấp quyền, dừng khẩn cấp và độ ổn định trên Windows.
