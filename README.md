# AI Cá Nhân — MVP v0.2

Website chat bằng ASP.NET Core 8, mặc định dùng Gemini API và nhớ hội thoại ngay trong trình duyệt. Kiến trúc nhà cung cấp AI đã được tách riêng để sau này có thể đổi Gemini, OpenAI hoặc mô hình chạy cục bộ.

## Phiên bản này đã có

- Giao diện chat responsive trên máy tính và điện thoại.
- Hội thoại nhiều lượt, lưu cục bộ trong trình duyệt.
- Backend giữ kín API key; trình duyệt không nhìn thấy khóa.
- Bộ nguyên tắc riêng trong `AI-CONSTITUTION.md`.
- Giới hạn độ dài và số lượt để tránh gửi dữ liệu quá lớn ngoài ý muốn.
- Không cần database và không cần cài thêm NuGet package.
- Có hồ sơ nền móng cho `Trợ lý cá nhân` và `Đội lập trình`; phiên bản này chưa tự chạy nhiều agent.

## Yêu cầu

- Visual Studio 2022 có workload **ASP.NET and web development**, hoặc
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Một Gemini API key từ Google AI Studio. Model mặc định có Free Tier theo chính sách hiện hành của Google.

Lưu ý: theo bảng giá Gemini, dữ liệu gửi qua Free Tier có thể được dùng để cải thiện sản phẩm của Google. Chưa đưa tài liệu mật, dữ liệu khách hàng hoặc thông tin nhạy cảm vào bản thử nghiệm này.

## Chạy trên Windows với Visual Studio

1. Mở `PersonalAI.sln`.
2. Tạo API key tại [Google AI Studio](https://aistudio.google.com/apikey).
3. Trong Windows PowerShell, lưu API key cho tài khoản Windows hiện tại:

   ```powershell
   setx GEMINI_API_KEY "khóa-của-bạn"
   ```

4. Đóng và mở lại Visual Studio để biến môi trường có hiệu lực.
5. Nhấn `Ctrl + F5`.

Không ghi API key thật vào `appsettings.json`, không gửi key lên GitHub, không đưa key vào JavaScript và không gửi khóa cho người khác.

## Chạy bằng dòng lệnh

### PowerShell

```powershell
$env:GEMINI_API_KEY="khóa-của-bạn"
dotnet run --project .\src\PersonalAI.Web
```

### macOS / Linux

```bash
export GEMINI_API_KEY="khóa-của-bạn"
dotnet run --project ./src/PersonalAI.Web
```

Mở địa chỉ được in trong terminal: `https://localhost:7188` hoặc `http://localhost:5188`.

## Chọn nhà cung cấp và model

Mặc định dự án dùng Gemini:

```json
"AI": {
  "Provider": "Gemini"
},
"Gemini": {
  "Model": "gemini-3.1-flash-lite"
}
```

Muốn quay lại OpenAI, đổi `Provider` thành `OpenAI`, chọn model trong phần `OpenAI` và cấu hình biến môi trường `OPENAI_API_KEY`. Không phải sửa mã nguồn.

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
    ├── Services/       # IAiProvider, Gemini, OpenAI
    ├── Teams/          # hồ sơ đội agent
    └── wwwroot/
        ├── index.html
        ├── styles.css
        └── app.js
```

## Luồng xử lý

```text
Trình duyệt → POST /api/chat → ASP.NET Core → IAiProvider → Gemini/OpenAI
                                      ↑
                            AI-CONSTITUTION.md
```

Lịch sử hiện chỉ nằm trong `localStorage` của trình duyệt. Phiên bản sau có thể thêm đăng nhập, PostgreSQL và RAG mà không cần thay giao diện hiện tại.

Endpoint `GET /api/team-profiles` cho thấy các hồ sơ đội đã được khai báo. Đây mới là hợp đồng dữ liệu cho tương lai, chưa phải hệ thống tự động thực hiện hành động.
