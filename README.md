# AI Cá Nhân — MVP v0.5.1

Website chat bằng ASP.NET Core 8, mặc định dùng Gemini API, nhớ nhiều cuộc trò chuyện và có kho dữ liệu riêng chạy bằng SQLite. Kiến trúc nhà cung cấp AI đã được tách riêng để sau này có thể đổi Gemini, OpenAI hoặc mô hình chạy cục bộ.

## Phiên bản này đã có

- Giao diện chat responsive trên máy tính và điện thoại.
- Hội thoại nhiều lượt, lưu cục bộ trong trình duyệt.
- Tạo, chuyển, đổi tên và xóa nhiều cuộc trò chuyện ở thanh bên.
- Tự đặt tiêu đề từ câu hỏi đầu tiên và tự nhập lịch sử từ phiên bản cũ.
- Kho dữ liệu riêng: tải lên, xem danh sách và xóa tệp TXT/Markdown ngay trong giao diện.
- Kiểm tra giới hạn 10 MB, tên tệp, định dạng văn bản và nội dung trùng lặp bằng SHA-256.
- Metadata và nội dung trích xuất được lưu bằng SQLite ngoài thư mục dự án, sẵn sàng cho bước RAG tiếp theo.
- Backend giữ kín API key; trình duyệt không nhìn thấy khóa.
- Chọn Gemini/OpenAI, model và nhập API key ngay trong giao diện.
- API key được mã hóa và lưu ngoài thư mục dự án trên chính máy đang chạy ứng dụng.
- Bộ nguyên tắc riêng trong `AI-CONSTITUTION.md`.
- Giới hạn độ dài và số lượt để tránh gửi dữ liệu quá lớn ngoài ý muốn.
- Không cần cài máy chủ database; SQLite chạy nhúng và được Visual Studio tự khôi phục qua NuGet.
- Có hồ sơ nền móng cho `Trợ lý cá nhân` và `Đội lập trình`; phiên bản này chưa tự chạy nhiều agent.

v0.5.1 mới xây phần quản lý dữ liệu. Nội dung tài liệu **chưa được tự động gửi** đến Gemini/OpenAI và AI **chưa dùng tài liệu để trả lời**; đó là phạm vi v0.5.2–v0.5.3.

## Yêu cầu

- Visual Studio 2022 có workload **ASP.NET and web development**, hoặc
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Một Gemini API key từ Google AI Studio hoặc OpenAI API key. Model Gemini mặc định có Free Tier theo chính sách hiện hành của Google.

Lưu ý: theo bảng giá Gemini, dữ liệu gửi qua Free Tier có thể được dùng để cải thiện sản phẩm của Google. Chưa đưa tài liệu mật, dữ liệu khách hàng hoặc thông tin nhạy cảm vào bản thử nghiệm này.

## Chạy trên Windows với Visual Studio

1. Mở `PersonalAI.sln`.
2. Nhấn `Ctrl + F5`.
3. Trong website, chọn **Cài đặt AI** ở thanh bên.
4. Chọn Gemini hoặc OpenAI, nhập tên model và API key.
5. Nhấn **Lưu & kiểm tra**. Từ lần chạy sau ứng dụng tự dùng lại cấu hình này.
6. Chọn **Kho dữ liệu** để thêm tệp `.txt` hoặc `.md` (tối đa 10 MB).

Không ghi API key thật vào `appsettings.json`, không gửi key lên GitHub, không đưa key vào JavaScript và không gửi khóa cho người khác.

## API key được lưu ở đâu?

Trên Windows, cấu hình nằm tại:

```text
%LOCALAPPDATA%\PersonalAI\ai-settings.json
```

API key trong file đã được bảo vệ bằng ASP.NET Core Data Protection và không nằm trong thư mục Git. Giao diện chỉ nhận lại trạng thái cùng bốn ký tự cuối, không nhận key đầy đủ. Nếu chưa lưu bằng giao diện, ứng dụng vẫn hỗ trợ `GEMINI_API_KEY` và `OPENAI_API_KEY` làm phương án dự phòng.

Ứng dụng v0.5.1 được thiết kế để chạy cá nhân trên máy của bạn. Trước khi đưa lên Internet hoặc cho nhiều người dùng, cần bổ sung đăng nhập và phân quyền vì màn hình cài đặt và kho dữ liệu hiện chưa có xác thực.

## Kho dữ liệu được lưu ở đâu?

Trên Windows, SQLite và bản sao tài liệu nằm tại:

```text
%LOCALAPPDATA%\PersonalAI\Knowledge\personal-ai.db
%LOCALAPPDATA%\PersonalAI\Knowledge\files\
```

Các tệp này không nằm trong repository và không bị Git tải lên GitHub. Khi xóa một tài liệu trong giao diện, cả metadata trong SQLite và bản sao cục bộ của tài liệu đều được xóa.

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

Nên đổi trực tiếp bằng nút **Cài đặt AI**. Giá trị trong `appsettings.json` chỉ là mặc định cho lần chạy đầu:

```json
"AI": {
  "Provider": "Gemini"
},
"Gemini": {
  "Model": "gemini-3.1-flash-lite"
}
```

Bạn có thể chuyển qua lại giữa Gemini và OpenAI mà không sửa mã nguồn. Mỗi nhà cung cấp giữ model và API key riêng.

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
    ├── Services/       # AI providers, cài đặt và SQLite knowledge store
    ├── Teams/          # hồ sơ đội agent
    └── wwwroot/
        ├── index.html
        ├── styles.css
        └── app.js
```

## Luồng xử lý

```text
Chat:      Trình duyệt → POST /api/chat → IAiProvider → Gemini/OpenAI
Kho dữ liệu: Trình duyệt → /api/knowledge/documents → SQLite + tệp cục bộ
```

Lịch sử hội thoại hiện nằm trong `localStorage` của trình duyệt. v0.5.2 sẽ tách nội dung thành các đoạn nhỏ; v0.5.3 sẽ tìm đoạn liên quan, đưa vào ngữ cảnh AI và hiển thị nguồn đã dùng. Lớp lưu trữ được tách riêng để sau này có thể chuyển từ SQLite sang PostgreSQL mà không phải viết lại giao diện.

Endpoint `GET /api/team-profiles` cho thấy các hồ sơ đội đã được khai báo. Đây mới là hợp đồng dữ liệu cho tương lai, chưa phải hệ thống tự động thực hiện hành động.
