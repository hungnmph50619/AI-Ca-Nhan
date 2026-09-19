# Giới hạn phiên điều khiển máy tính — bản thử nghiệm

## Mục tiêu
Tiếp nối PR #45 (khóa điều khiển mặc định) và PR #46 (một lần nhấp chuột trái có xác nhận), bản này giới hạn thời gian và số thao tác sau mỗi lần người dùng chủ động cho phép điều khiển.

- Mỗi lần bật có **60 giây** và **tối đa 5 lượt thao tác**. Hết thời gian hoặc dùng hết lượt, chốt trong dịch vụ tự khóa khi đọc trạng thái hoặc khi có yêu cầu điều khiển tiếp theo.
- Mỗi lần gọi `FocusWindow`, `MoveCursor` hoặc `ClickLeft` tiêu tốn một lượt ngay cả khi Windows từ chối thao tác. Không có cơ chế tự gia hạn.
- Thời hạn và số lượt còn lại xuất hiện trong API trạng thái và giao diện tiếng Việt. Nút Dừng vẫn cho phép thu hồi các lượt còn lại bất cứ lúc nào.
- Mỗi lần khởi động lại ứng dụng đều bắt đầu ở trạng thái khóa. Các quyền `WRITE`/`COMPUTER`/`SENSITIVE` và xác nhận cho từng công cụ vẫn được kiểm tra riêng.

## Giới hạn
- Không có phím nóng dừng độc lập ở cấp Windows; nút Dừng trong trang web chỉ chặn các lệnh tương lai đi qua dịch vụ. Một lệnh đã bắt đầu có thể hoàn tất trước khi Stop trả về.
- Mốc 60 giây là **hạn cấp quyền**; không phải đồng hồ có thể ngắt một lời gọi native đang chạy. Trạng thái được tính lại tại lần truy cập tiếp theo; giao diện không tự đếm ngược liên tục.
- API bật quyền hiện chỉ giới hạn ở loopback với header đồng ý; **chưa phải xác thực danh tính**. Không mở ứng dụng ra mạng LAN/Internet. Thử nghiệm nhấp chuột chỉ trên cửa sổ không chứa dữ liệu quan trọng.
- Chưa có nhập liệu bàn phím, nhận diện cây giao diện, chụp màn hình, tự chọn mục tiêu hoặc vòng lặp AI.
- CI Windows kiểm tra hết lượt bằng 5 lần chuyển sang ID cửa sổ 0x0 không hợp lệ, không tạo nhấp chuột thật. Cần thử trên máy người dùng trước khi hợp nhất.
