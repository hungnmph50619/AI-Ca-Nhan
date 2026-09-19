# Rà soát chức năng còn thiếu so với lộ trình — 19/09/2026

## Phạm vi đối chiếu
- Tài liệu lộ trình người dùng cung cấp: v1.1.0–v1.1.7 (máy tính), v1.2 (trình duyệt), v1.4 (phát triển phần mềm), v2.1–v2.2 (đa tác nhân), v2.3–v2.7 (đánh giá và tự cải tiến).
- Mã nguồn đối chiếu: nhánh tích hợp `feature/v1.8-automation` sau khi đưa v2.2.6 vào, không phải nhánh `main`.
- Số phiên bản trong giao diện/API **không chứng minh** mọi chức năng trong lộ trình cũ đã triển khai.

## Bảng thiếu hụt đã xác nhận

| Mốc lộ trình | Mã nguồn hiện tại | Trạng thái / việc cần làm |
| --- | --- | --- |
| v1.1.0 Chụp toàn màn hình/cửa sổ/vùng, nhiều màn hình | `ComputerUseService.cs` chỉ đọc kích thước và tọa độ; không có chụp ảnh màn hình | **Chưa có**; thiết kế quyền đọc ảnh nhạy cảm, giới hạn vùng và vòng đời ảnh trước khi triển khai. |
| v1.1.1 Nhận biết cửa sổ | Liệt kê, lấy cửa sổ đang hoạt động và chuyển cửa sổ theo ID | **Một phần**; cần xử lý cửa sổ không ổn định và bảo vệ tiêu đề nhạy cảm. |
| v1.1.2 Đọc cây thành phần Windows | Không có bộ đọc cây UI Automation trong dịch vụ điều khiển máy | **Chưa có**; phải giới hạn số phần tử, thời gian và vùng dữ liệu. |
| v1.1.3 Chuột | Chỉ di chuyển con trỏ tới tọa độ đã xác nhận | **Chưa có** nhấp trái/phải/đúp, cuộn và kéo thả. Không tự động hóa chuỗi thao tác khi chưa có cơ chế dừng độc lập và quyền theo hành động. |
| v1.1.4 Bàn phím | Không có công cụ gõ chữ/nhấn phím trong `ComputerUseTools.cs` | **Chưa có**; cần hạn chế phím nhạy cảm, vùng nhập, độ dài, xác nhận từng hành động. |
| v1.1.5 Đọc ảnh khi không đọc được cây giao diện | Không có ảnh chụp hoặc chuỗi xác minh mục tiêu | **Chưa có**; chỉ nên làm sau khi có chụp màn hình và giới hạn quyền. |
| v1.1.6 Vòng lặp quan sát–hành động | Dịch vụ máy tính chỉ có thao tác đơn lẻ; điều phối tác nhân tắt công cụ và tự chọn tác nhân | **Chưa có**; chưa bật tự động điều khiển PC. |
| v1.1.7 Dừng khẩn cấp | Không có phím nóng hệ thống hay bộ thu hồi quyền điều khiển phiên | **Chưa có đầy đủ**; nhánh này bổ sung **nút dừng giới hạn** cho hai hành động hiện có, mặc định khóa khi khởi động. Nút không chặn công cụ khác, không dừng tác vụ đang chạy bên ngoài và không thay phím dừng độc lập. |
| v1.2 Trình duyệt tương tác | `BrowserAgentService.cs` dùng HTTP/HTML, công bố JS/cookie/download/submit đều tắt | **Một phần**; chưa phải điều khiển trình duyệt bằng Playwright như lộ trình. |
| v1.4 Tự phát triển phần mềm | `DevelopmentAgentService.cs` công bố không cho shell tùy ý, tiến trình tùy ý và ghi Git | **Một phần**; không diễn giải khả năng đọc/dựng dự án thành quyền tự sửa mã và tạo PR. |
| v2.2 Điều phối đa tác nhân | `AgentOrchestrationService.cs`: tuần tự có duyệt, công cụ/tự chọn/đồng thời/hàng đợi chung đều tắt, không khôi phục sau khởi động | **Một phần** so với kế hoạch điều phối tự động đầy đủ. |
| v2.3–v2.7 Đánh giá, tự cải tiến, tự sửa chính mình | Chưa xác nhận được một hệ thống khép kín có đánh giá, xây dựng, kiểm thử, duyệt và triển khai có rollback | **Không được coi là hoàn thành** chỉ dựa vào số phiên bản. Cần báo cáo kiểm thử riêng cho từng giai đoạn. |

## Đã triển khai trong nhánh rà soát này
1. Thêm chốt tạm dừng cho **chuyển cửa sổ và di chuyển con trỏ**. Trạng thái mặc định là dừng ở mỗi lần khởi động. Chốt được kiểm tra ngay trong dịch vụ ở thời điểm thao tác, không chỉ tại giao diện.
2. Thêm nút **Dừng điều khiển** và **Cho phép điều khiển** ở mục Máy tính. Cho phép lại cần xác nhận thủ công trong giao diện; endpoint chỉ tiếp nhận yêu cầu từ địa chỉ vòng lặp cục bộ và vẫn áp dụng xác nhận quyền của Tool Framework cho từng công cụ.
3. API trạng thái công bố `desktopActionsPaused`. Thêm hồi quy Linux: mặc định tạm dừng, nút dừng, từ chối bật khi thiếu xác nhận, từ chối bật trên môi trường không hỗ trợ.
4. Không bổ sung nhấp chuột, gõ phím, ảnh chụp hay vòng lặp AI vào nhánh này.

## Giới hạn an toàn và kiểm thử bắt buộc trước khi mở rộng
- Nút dừng chỉ chặn hành động mới đi qua dịch vụ trên cùng tiến trình. Một lệnh Windows đã bắt đầu có thể hoàn tất; không có phím nóng hệ thống; nếu chương trình ngừng phản hồi cần dừng tiến trình bằng tay.
- Xác nhận trên giao diện và header cục bộ **không phải cơ chế xác thực người dùng**; không công khai máy chủ hay mở cổng điều khiển ra mạng.
- Không đưa mật khẩu, mã xác thực hoặc nội dung nhạy cảm vào ảnh/ô nhập để thử nghiệm.
- Cần thử nghiệm thực tế trên Windows với nhiều màn hình, kích thước và tỉ lệ hiển thị khác nhau; CI Linux không chứng nhận được hành vi user32 trên Windows.
- Chỉ khi có dừng độc lập, kiểm soát phiên và kiểm thử Windows mới lần lượt thêm thao tác chuột đơn lẻ, bàn phím đơn lẻ, nhận biết giao diện, ảnh chụp có kiểm soát, rồi vòng lặp quan sát–hành động. Mỗi bước phải có kiểm thử từ chối quyền và nhật ký kiểm chứng.
