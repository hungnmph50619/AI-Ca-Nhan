# AI Cá Nhân — MVP v0.5.3

Website chat bằng ASP.NET Core 8, mặc định dùng Gemini API, nhớ nhiều cuộc trò chuyện và có kho dữ liệu riêng chạy bằng SQLite. Kiến trúc nhà cung cấp AI đã được tách riêng để sau này có thể đổi Gemini, OpenAI hoặc mô hình chạy cục bộ.

## Phiên bản này đã có

- Giao diện chat responsive trên máy tính và điện thoại.
- Hội thoại nhiều lượt, lưu cục bộ trong trình duyệt.
- Tạo, chuyển, đổi tên và xóa nhiều cuộc trò chuyện ở thanh bên.
- Tự đặt tiêu đề từ câu hỏi đầu tiên và tự nhập lịch sử từ phiên bản cũ.
- Kho dữ liệu riêng: tải lên, xem danh sách và xóa tệp TXT/Markdown ngay trong giao diện.
- Kiểm tra giới hạn 10 MB, tên tệp, định dạng văn bản và nội dung trùng lặp bằng SHA-256.
- Nội dung được tự chia thành các đoạn nhỏ có chồng lấn và lập chỉ mục tìm kiếm toàn văn bằng SQLite FTS5.
- Có ô **Tìm trong dữ liệu** để xem trước tối đa 5 đoạn liên quan nhất ngay trong giao diện.
- Tài liệu đã thêm từ v0.5.1 được tự lập chỉ mục khi ứng dụng khởi động, không cần tải lại.
- Khi trò chuyện, hệ thống tự tìm tối đa 5 đoạn liên quan và bổ sung chúng vào ngữ cảnh Gemini/OpenAI.
- Câu trả lời hiển thị tên tệp và số đoạn đã được dùng; thông tin nguồn vẫn được giữ khi tải lại trình duyệt.
- Dữ liệu tài liệu được coi là nội dung tham khảo không đáng tin cậy, không phải chỉ dẫn hệ thống.
- Backend giữ kín API key; trình duyệt không nhìn thấy khóa.
- Chọn Gemini/OpenAI, model và nhập API key ngay trong giao diện.
- API key được mã hóa và lưu ngoài thư mục dự án trên chính máy đang chạy ứng dụng.
- Bộ nguyên tắc riêng trong `AI-CONSTITUTION.md`.
- Giới hạn độ dài và số lượt để tránh gửi dữ liệu quá lớn ngoài ý muốn.
- Không cần cài máy chủ database; SQLite chạy nhúng và được Visual Studio tự khôi phục qua NuGet.
- Có hồ sơ nền móng cho `Trợ lý cá nhân` và `Đội lập trình`; phiên bản này chưa tự chạy nhiều agent.

v0.5.3 giữ toàn bộ tệp và chỉ mục trên máy, nhưng khi trò chuyện sẽ **gửi tối đa 5 đoạn liên quan** đến nhà cung cấp AI đang chọn để tạo câu trả lời. Ứng dụng không gửi toàn bộ kho dữ liệu. Không tải tài liệu nhạy cảm nếu bạn không muốn nội dung liên quan được gửi đến Gemini/OpenAI.

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

Ứng dụng v0.5.3 được thiết kế để chạy cá nhân trên máy của bạn. Trước khi đưa lên Internet hoặc cho nhiều người dùng, cần bổ sung đăng nhập và phân quyền vì màn hình cài đặt và kho dữ liệu hiện chưa có xác thực.

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
Chat:      Trình duyệt → POST /api/chat → tìm đoạn liên quan → IAiProvider → Gemini/OpenAI
Kho dữ liệu: Trình duyệt → /api/knowledge/documents → SQLite + tệp cục bộ
Tìm kiếm: Trình duyệt → /api/knowledge/search → SQLite FTS5 → các đoạn liên quan
```

Lịch sử hội thoại và metadata nguồn hiện nằm trong `localStorage` của trình duyệt. v0.5.3 đã đưa các đoạn liên quan vào ngữ cảnh AI và hiển thị nguồn đã dùng; bước tiếp theo có thể bổ sung chế độ bật/tắt dữ liệu riêng, xem trích đoạn nguồn trực tiếp từ câu trả lời và đánh giá chất lượng truy xuất. Lớp lưu trữ được tách riêng để sau này có thể chuyển từ SQLite sang PostgreSQL mà không phải viết lại giao diện.

Endpoint `GET /api/team-profiles` cho thấy các hồ sơ đội đã được khai báo. Đây mới là hợp đồng dữ liệu cho tương lai, chưa phải hệ thống tự động thực hiện hành động.

## Thử nghiệm cầu nối Xerath Support Assistant — bản đầu tiên

AI Cá Nhân có API cục bộ `GET /api/integrations/xerath/status` và `POST /api/integrations/xerath/notice` để nhận **hai loại tín hiệu đã được xác nhận** từ trợ lý Xerath: `own-health-loss` (máu của chính bạn vừa giảm nhanh) và `completed-kill` (điểm hạ gục đã được công bố). Endpoint phản hồi câu thông báo tiếng Việt theo mẫu cố định, kèm nguồn, trạng thái xác nhận và hạn sử dụng; **đây mới là kết nối thử nghiệm, KHÔNG chạy Gemini/OpenAI, KHÔNG có nhận diện hình ảnh, KHÔNG theo dõi rừng địch hoặc đưa quyết định chiến thuật**.

Cách chạy: tại thư mục AI-Ca-Nhan, dùng `dotnet run --project src/PersonalAI.Web --launch-profile PersonalAI.Web`. Chương trình mặc định có địa chỉ **http://localhost:5188** bên cạnh HTTPS. Trên cùng máy Windows, chạy Xerath Support Assistant, mở **Chỉ số trực tiếp & tổng hợp**, bấm **Kiểm tra kết nối AI cá nhân** để xác nhận API đang hoạt động, sau đó tùy chọn tích ô **Thử cầu nối AI cá nhân trên máy** và bật HUD. Kết nối không bắt buộc; khi AI Cá Nhân chưa chạy, HUD hiển thị thông báo cục bộ sẵn có mà không đợi quá lâu.

Hợp đồng dữ liệu yêu cầu `POST http://127.0.0.1:5188/api/integrations/xerath/notice`, header `X-Xerath-Bridge: 1`, body JSON dạng `{"kind":"own-health-loss","gameTimeSeconds":120,"healthPercent":30}` hoặc `{"kind":"completed-kill","gameTimeSeconds":120,"healthPercent":null}`. API chỉ chấp nhận người gọi loopback và tên host localhost/127.0.0.1, chỉ xử lý tín hiệu nằm trong danh sách cho phép và không đưa chuỗi tùy ý từ bên gọi vào HUD. **Không mở địa chỉ này ra Internet**, không cấu hình port-forward hoặc reverse proxy; API này chưa có xác thực giữa các tiến trình cục bộ. Không có ảnh, lịch sử chat, API key hoặc dữ liệu vị trí tướng địch nào được gửi qua cầu nối.

**Các bước tương lai:** bổ sung kiểm thử truyền dữ liệu và đo độ trễ; tạo bộ quan sát ảnh để *phân tích luyện tập* với nhãn rõ ràng; chỉ xem xét bật từng loại phân tích lên HUD trong trận sau khi kiểm thử và xác minh phạm vi Riot cho phép. Đừng hiểu API này là AI đã biết xem trận hoặc tự chọn đường di chuyển và kỹ năng.

