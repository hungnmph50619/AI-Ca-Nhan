# Computer Backend Benchmark

Mục tiêu của nhánh này là so sánh các backend điều khiển máy tính bằng cùng một bộ tác vụ, không thay thế Computer Operator hiện tại và không hard-code theo từng ứng dụng.

## Baseline

- AI-Ca-Nhan v4.0.25 Universal Reliable Operator
- Commit gốc của nhánh benchmark: `7fe933d474a6c326310a0168abdf1246af1c7e1b`

## Backend thử nghiệm đầu tiên: UFO²

UFO² chạy như tiến trình bên ngoài. AI-Ca-Nhan không dùng shell để ghép lệnh và không truyền bí mật qua command line.

### Yêu cầu

- Windows 10/11
- Python 3.10+
- Git
- UFO² đã clone và cài dependency theo tài liệu chính thức của Microsoft UFO.

Ví dụ cài đặt:

```powershell
git clone https://github.com/microsoft/UFO.git
cd UFO
pip install -r requirements.txt
copy config\ufo\agents.yaml.template config\ufo\agents.yaml
```

Sau đó cấu hình model/API key trực tiếp trong UFO² theo tài liệu của UFO².

### Cấu hình AI-Ca-Nhan

Không lưu API key UFO² trong repository AI-Ca-Nhan.

Đặt biến môi trường:

```powershell
$env:PERSONALAI_UFO2_ROOT="C:\duong-dan\UFO"
$env:PERSONALAI_UFO2_PYTHON="python"
```

Khởi động lại AI-Ca-Nhan sau khi đặt biến môi trường.

### Kiểm tra trạng thái

```http
GET /api/computer/backend-benchmark/status
```

Endpoint chỉ chấp nhận localhost.

### Chạy UFO² benchmark

```http
POST /api/computer/backend-benchmark/ufo2/run
X-PersonalAI-Manual-Approval: dong-y
Content-Type: application/json

{
  "goal": "Mở Notepad và nhập dòng Hello benchmark",
  "taskName": "ufo2-notepad"
}
```

AI-Ca-Nhan gọi UFO² theo CLI chính thức:

```text
python -m ufo --task <taskName> -r "<goal>"
```

Không chạy UFO² nếu Computer Operator hiện tại đang chạy, để tránh hai agent cùng điều khiển chuột/bàn phím.

## Bộ scenario chuẩn

1. **Notepad**
   - Mở Notepad.
   - Nhập một câu ngắn.
   - Dừng khi câu đã xuất hiện.

2. **Calculator**
   - Mở Calculator.
   - Không thực hiện phép tính nguy hiểm hay thay đổi hệ thống.

3. **File Explorer**
   - Mở File Explorer.
   - Điều hướng tới Downloads.
   - Không xóa/đổi tên tệp.

4. **Browser**
   - Mở trình duyệt.
   - Truy cập một trang thử nghiệm công khai.

5. **Ứng dụng tải chậm**
   - Mở ứng dụng có thời gian chuyển trạng thái dài.
   - Đánh giá khả năng wait/reobserve thay vì click lặp.

6. **Riot Client**
   - Mở Riot Client.
   - Dừng khi client hiển thị ổn định.

7. **League of Legends → Practice Tool**
   - Mở League of Legends.
   - Vào Phòng Tập.
   - Dừng khi đã vào trận và có thể điều khiển tướng.
   - Không tiếp tục gameplay.

## Chỉ số phải ghi

- Hoàn thành mục tiêu thực tế: có/không.
- Thời gian tổng.
- Số hành động.
- Số lần gọi Vision/LLM nếu backend cung cấp.
- Số lần retry/replan.
- Số side effect lặp.
- Số lần người dùng phải can thiệp.
- Lỗi provider/timeout.
- Đường dẫn log/screenshot của backend.

Exit code 0 không tự động được coi là task success. Phải đối chiếu log/screenshot/trạng thái desktop.

## Nguyên tắc quyết định kiến trúc

- Không chọn backend chỉ vì một lần chạy nhanh.
- Không hard-code logic League vào router.
- Windows native ưu tiên structured capability khi có.
- Browser ưu tiên DOM/Playwright khi có.
- Custom-rendered UI/game mới dùng CUA/Vision mạnh hơn.
- Backend ngoài bị lỗi không được làm hỏng baseline AI-Ca-Nhan.
- Không merge nhánh benchmark vào roadmap chính trước khi có số liệu.
