# AI Cá Nhân — MVP v0.1

Website chat bằng ASP.NET Core 8, gọi OpenAI Responses API và nhớ hội thoại ngay trong trình duyệt.

## Phiên bản này đã có

- Giao diện chat responsive trên máy tính và điện thoại.
- Hội thoại nhiều lượt, lưu cục bộ trong trình duyệt.
- Backend giữ kín API key; trình duyệt không nhìn thấy khóa.
- Bộ nguyên tắc riêng trong `AI-CONSTITUTION.md`.
- Tắt lưu response phía API bằng `store: false`.
- Giới hạn độ dài và số lượt để tránh gửi dữ liệu quá lớn ngoài ý muốn.
- Không cần database và không cần cài thêm NuGet package.

## Yêu cầu

- Visual Studio 2022 có workload **ASP.NET and web development**, hoặc
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Một OpenAI API key có billing riêng. Gói ChatGPT Plus không tự động bao gồm chi phí API.

## Chạy trên Windows với Visual Studio

1. Mở `PersonalAI.sln`.
2. Trong Windows PowerShell, lưu API key cho tài khoản Windows hiện tại:

   ```powershell
   setx OPENAI_API_KEY "sk-..."
   ```

3. Đóng và mở lại Visual Studio để biến môi trường có hiệu lực.
4. Nhấn `Ctrl + F5`.

Không ghi API key thật vào `appsettings.json`, không gửi key lên GitHub và không đưa key vào JavaScript.

## Chạy bằng dòng lệnh

### PowerShell

```powershell
$env:OPENAI_API_KEY="sk-..."
dotnet run --project .\src\PersonalAI.Web
```

### macOS / Linux

```bash
export OPENAI_API_KEY="sk-..."
dotnet run --project ./src/PersonalAI.Web
```

Mở địa chỉ được in trong terminal: `https://localhost:7188` hoặc `http://localhost:5188`.

## Thay model

Mở `src/PersonalAI.Web/appsettings.json` và đổi:

```json
"Model": "gpt-5.6-luna"
```

`gpt-5.6-luna` được chọn làm mặc định vì hướng tới chi phí thấp. Model có thể đổi mà không phải sửa mã nguồn.

## Sửa nguyên tắc của AI

Mở:

```text
src/PersonalAI.Web/AI-CONSTITUTION.md
```

Sửa các nguyên tắc rồi khởi động lại ứng dụng. Nội dung file này được gửi trong trường `instructions` ở mỗi yêu cầu.

## Cấu trúc chính

```text
PersonalAI/
├── PersonalAI.sln
├── README.md
└── src/PersonalAI.Web/
    ├── AI-CONSTITUTION.md
    ├── Program.cs
    ├── Models/
    ├── Options/
    ├── Services/
    └── wwwroot/
        ├── index.html
        ├── styles.css
        └── app.js
```

## Luồng xử lý

```text
Trình duyệt → POST /api/chat → ASP.NET Core → OpenAI Responses API
                                      ↑
                            AI-CONSTITUTION.md
```

Lịch sử hiện chỉ nằm trong `localStorage` của trình duyệt. Phiên bản sau có thể thêm đăng nhập, PostgreSQL và RAG mà không cần thay giao diện hiện tại.
