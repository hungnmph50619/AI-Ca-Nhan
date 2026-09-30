# Nhập văn bản Notepad — bản thử nghiệm có xác nhận

## Phạm vi
Bản này nối tiếp PR #45–#48, **không** phải điều khiển bàn phím toàn Windows. Chỉ bổ sung một dòng văn bản **1–32 ký tự Unicode thuộc BMP** (gồm tiếng Việt) vào cửa sổ Notepad đang hoạt động; không hỗ trợ phím nóng, xuống dòng, tab, nhấp chuột tự động, nhập vào trình duyệt hay tự chạy vòng lặp AI.

## Sử dụng trong bản thử nghiệm trên Windows
1. Chạy ứng dụng trên `127.0.0.1`; mở Notepad mới/trống và trang AI Cá Nhân.
2. Trong mục **Máy tính**, xác nhận trạng thái phím dừng `Ctrl + Shift + F12`, bật phiên tối đa 60 giây/5 thao tác.
3. Bấm **Tìm cửa sổ Notepad**, chọn đúng cửa sổ đang mở, nhập dòng thử nghiệm không chứa dữ liệu nhạy cảm, bấm **Xác nhận nhập sau 5 giây** và đồng ý.
4. Ngay lập tức chuyển sang đúng cửa sổ Notepad đã chọn bằng Alt + Tab. Sau 5 giây, dịch vụ sẽ kiểm tra lại ID cửa sổ và tiến trình `notepad`; nếu không trùng thì từ chối nhập.
5. Bấm `Ctrl + Shift + F12` để khóa quyền điều khiển; kiểm tra kết quả. Không dùng Notepad đang chứa nội dung quan trọng.

## Biện pháp giới hạn
- Lệnh đi qua API riêng trên loopback và cần header xác nhận thủ công, **không đăng ký như công cụ chung**, vì công cụ chung ghi đầu vào vào nhật ký hoạt động. Chỉ ghi nhật ký metadata rằng đã thực hiện, **không ghi nội dung nhập**. Đừng gửi mật khẩu, mã xác thực, dữ liệu cá nhân hoặc bí mật vào bản thử nghiệm.
- Kiểm tra 1–32 ký tự, cấm ký tự điều khiển, ký tự thay thế UTF-16, ký tự định dạng/không hiển thị, dấu xuống dòng/tab. Gửi cặp sự kiện Unicode nhấn–nhả; không cho phép phím chức năng/hotkey.
- Mỗi lần gọi tốn một lượt điều khiển dù nhập không thành công. Phím dừng/nút Dừng và hạn 60 giây vẫn áp dụng.
- Đích phải là **cửa sổ đang hoạt động** với tiến trình `notepad`. Không tự chuyển cửa sổ hay tự chọn vị trí con trỏ. Người dùng phải tự nhấp vào vùng soạn thảo Notepad trước khi quay về trang AI để chọn thao tác; nội dung sẽ được gõ vào nơi đang nhận phím trong Notepad khi được gửi.
- Vì cửa sổ có thể đổi giữa bước kiểm tra và lúc gửi, và Notepad có nhiều vùng tương tác, ứng dụng chưa bảo đảm văn bản vào đúng vùng mong muốn; chỉ thử trên cửa sổ mới/trống. Nếu Windows chấp nhận một phần cặp phím, hệ thống báo lỗi và **không tự gửi lại** để tránh nhập trùng.
- Header loopback **không phải xác thực người dùng mạnh**; không công khai máy chủ lên LAN/Internet. Giới hạn phím dừng, UAC và thao tác đã phát sinh vẫn như PR #48.

## Kiểm thử
- CI Linux/Windows: thiếu xác nhận phải bị từ chối; khi khóa, lệnh nhập phải bị từ chối; không tạo thao tác gõ thật vào desktop CI.
- Cần kiểm thử thủ công trên Windows: tiếng Việt và chuỗi thường, Notepad không ở foreground, Notepad bị đóng/đổi cửa sổ trong 5 giây, phím dừng trước lúc gửi, nhiều màn hình và bàn phím khác nhau.
- Chưa kết nối AI vào chức năng này, chưa có trình điều khiển văn bản nói chung hay nhận diện ô nhập. Chỉ mở rộng sau khi các ràng buộc đã được xác nhận trên Windows thực tế.
