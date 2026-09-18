# AI Cá Nhân — Controlled Computer Use v1.1.0

PersonalAI là trợ lý AI cá nhân chạy bằng ASP.NET Core 8, ưu tiên dữ liệu cục bộ, có thể dùng Gemini hoặc OpenAI cho phần sinh câu trả lời. v1.0.0 vẫn là **Stable Personal AI Core**; v1.1.0 là capability layer đầu tiên phía trên core đó, bổ sung **Computer Use có kiểm soát trên Windows** mà không mở autonomous agent loop.

> Đây vẫn là ứng dụng thiết kế cho một người dùng trên máy cá nhân. Không đưa trực tiếp lên Internet hoặc dùng như hệ thống nhiều người dùng nếu chưa bổ sung authentication, authorization và hardening triển khai phù hợp.

## Có gì trong v1.1.0?

### Chat và AI provider

- Chat nhiều lượt với lịch sử lưu trong trình duyệt.
- Nhiều cuộc trò chuyện: tạo, chuyển, đổi tên, xóa.
- Gemini và OpenAI qua lớp provider riêng.
- API key được giữ ở backend, mã hóa bằng ASP.NET Core Data Protection và lưu ngoài repository.
- Có thể chọn provider/model trong giao diện.
- Constitution riêng tại `src/PersonalAI.Web/AI-CONSTITUTION.md`.

### Workspace

Có các workspace dựng sẵn:

- Cá nhân
- Công việc
- Học tập
- AI Cá Nhân
- Du lịch

Có thể tạo workspace tùy chỉnh, tối đa 20 workspace.

Memory, Documents/RAG, Tasks, Files và Conversations được tách theo workspace. Request backend dùng header:

```text
X-PersonalAI-Workspace
```

Workspace không tồn tại bị từ chối thay vì âm thầm fallback sang workspace khác.

### Memory

- Fact / preference / rule.
- Long-term hoặc temporary.
- Bật/tắt, cập nhật, xóa, import/export.
- Phát hiện nội dung trùng hoặc có vẻ là bản cập nhật.
- Nội dung memory được bảo vệ bằng Data Protection khi lưu.
- Context Manager chỉ lấy memory đang bật, còn hiệu lực và liên quan đến câu hỏi.

### Documents / RAG

Hỗ trợ:

- PDF
- DOCX
- TXT
- Markdown

Pipeline hiện có:

```text
upload
  ↓
extract
  ↓
chunk
  ↓
SQLite / FTS
  ↓
local embeddings
  ↓
hybrid search
  ↓
rerank
  ↓
Context Manager
```

Có document management, integrity/quality checks, reindex, local semantic search, hybrid search và source reader.

Tài liệu được coi là **untrusted reference data**, không phải system instruction.

Khi chat dùng tài liệu, chỉ các đoạn được Context Manager chọn mới được gửi đến provider AI đang dùng; không gửi toàn bộ kho dữ liệu.

### Context Manager

v1.0 giữ strategy:

```text
workspace-scoped-budgeted-context-v1
```

Context được chọn từ:

- Memory
- Documents
- Tasks

Ngân sách mặc định:

- tổng: 7.600 ký tự;
- Documents: tối đa 4.400 ký tự;
- Memory: tối đa 1.800 ký tự;
- Tasks: tối đa 1.400 ký tự;
- tối đa 4 document chunks;
- tối đa 4 memories;
- tối đa 3 tasks.

Endpoint debug không gọi AI:

```http
POST /api/context/preview
```

### Tool Framework

Tool Framework có permission contract:

- READ
- WRITE
- DELETE
- EXTERNAL
- SENSITIVE
- COMPUTER

WRITE, DELETE, EXTERNAL, SENSITIVE và COMPUTER yêu cầu confirmation theo policy hiện tại. COMPUTER được dùng riêng cho các thao tác điều khiển desktop.

Các tool local gồm nhóm đọc/tiện ích và workspace files, ví dụ:

- clock
- text stats
- calculator
- date math
- memory search
- document search
- app summary
- workspace list/read/write/create directory/move/delete

Tool proposal không đồng nghĩa tool execution. AI có thể đề xuất tool nhưng người dùng vẫn kiểm soát việc chạy và các bước xác nhận.

### Controlled Computer Use

v1.1.0 thêm lớp Computer Use chạy qua chính Tool Framework hiện có.

Backend hiện hỗ trợ **Windows interactive session**.

Các tool quan sát:

- `computer.screen.info` — kích thước desktop/màn hình, READ;
- `computer.cursor.position` — vị trí cursor, READ;
- `computer.windows.list` — visible window metadata, READ + SENSITIVE + confirmation;
- `computer.window.active` — foreground window metadata, READ + SENSITIVE + confirmation.

Các action có kiểm soát:

- `computer.window.focus` — chuyển focus tới visible window, WRITE + COMPUTER + SENSITIVE + confirmation;
- `computer.cursor.move` — di chuyển cursor, WRITE + COMPUTER + confirmation.

v1.1.0 cố ý **chưa có** screenshot, OCR, click chuột, scroll, gõ phím, clipboard, mở ứng dụng, shell hay arbitrary process execution.

Status cục bộ:

```http
GET /api/computer/status
```

Window title/process metadata được coi là dữ liệu nhạy cảm. Tool có SENSITIVE permission không tự được gửi kết quả ra provider AI để synthesis/native continuation.

### Tasks

Task Engine hỗ trợ:

- plan nhiều bước;
- state bền vững bằng SQLite;
- restart recovery;
- interrupted state;
- safe retry;
- task dependencies;
- step dependencies;
- permission/confirmation của từng tool step.

Task không tự chạy chuỗi bước. Người dùng phải chủ động chạy từng bước.

### Audit

Audit Foundation trả lời:

```text
Time · Workspace · Agent · Action · Tool · Target · Reason · Result
```

Audit:

- workspace-scoped;
- local SQLite;
- giữ tối đa 90 ngày;
- tối đa 10.000 event;
- không lưu nguyên văn prompt, Memory, document content, full tool arguments/output hay API key.

API:

```http
GET /api/audit
GET /api/audit/summary
```

### Undo

Undo Foundation lưu pre-action state cho một số thao tác file có thể đảo ngược an toàn.

Hiện hỗ trợ có guard:

- tạo file;
- overwrite/append file;
- xóa file nhỏ;
- tạo/xóa thư mục rỗng;
- di chuyển file.

Undo luôn:

1. kiểm tra trạng thái hiện tại;
2. yêu cầu confirmation;
3. kiểm tra guard lại ở server;
4. mới áp dụng inverse operation.

Nếu file đã thay đổi sau hành động gốc, Undo bị chặn thay vì ghi đè dữ liệu mới.

API:

```http
GET  /api/undo
GET  /api/undo/{undoId}/assessment
POST /api/undo/{undoId}/execute
```

Undo khả dụng 7 ngày, tối đa 500 record, snapshot tối đa 512 KB.

## Stable Core contract

v1.1.0 giữ API contract của Stable Core và mở controlled capability layer:

- Version: `1.1.0`
- API contract: `1`
- Channel: `controlled`
- Stable Core: v1.0 semantics
- Controlled module: `computer-use`

Capabilities:

```http
GET /api/system/capabilities
```

Readiness local:

```http
GET /api/system/health
```

Health check **không gọi Gemini/OpenAI**.

Mọi API response có:

```text
X-PersonalAI-Request-Id
X-PersonalAI-Api-Version: 1
```

Unhandled API exception được sanitize; stack trace và đường dẫn local không được trả về client.

## Guardrails hiện tại

v1.1.0 **không phải autonomous agent**.

Hiện tại:

- Workspace isolation: bật
- WRITE confirmation: bắt buộc
- DELETE confirmation: bắt buộc
- EXTERNAL confirmation: bắt buộc
- Undo confirmation: bắt buộc
- Computer-control confirmation: bắt buộc
- Sensitive computer observation confirmation: bắt buộc
- Automatic multi-step execution: tắt
- Background scheduler: tắt
- Autonomous agent loop: tắt
- Parallel tool calls: tắt

Chưa có:

- browser automation;
- screenshot/OCR;
- mouse click hoặc keyboard typing;
- background scheduler;
- autonomous agent loop;
- automatic confirmation;
- connectors Gmail/Calendar;
- cloud multi-user auth.

## Yêu cầu

- .NET 8 SDK, hoặc
- Visual Studio 2022 với workload **ASP.NET and web development**.

Để chat với model bên ngoài, cần API key Gemini hoặc OpenAI. Các chức năng local như quản lý dữ liệu, Tasks, Audit, Undo và health contract vẫn có thể hoạt động khi AI provider chưa được cấu hình.

## Chạy ứng dụng

### Windows / PowerShell

```powershell
$env:GEMINI_API_KEY="khóa-của-bạn"
dotnet run --project .\src\PersonalAI.Web
```

### macOS / Linux

```bash
export GEMINI_API_KEY="khóa-của-bạn"
dotnet run --project ./src/PersonalAI.Web
```

Hoặc mở `PersonalAI.sln` bằng Visual Studio và chạy project `PersonalAI.Web`.

Ứng dụng thường mở ở URL do ASP.NET Core in ra trong terminal, ví dụ `https://localhost:7188` hoặc `http://localhost:5188`.

## API key

Cấu hình AI trên Windows nằm tại:

```text
%LOCALAPPDATA%\PersonalAI\ai-settings.json
```

Không commit API key vào:

- `appsettings.json`
- JavaScript
- Git
- issue/log công khai

Nếu chưa lưu key trong giao diện, ứng dụng vẫn hỗ trợ biến môi trường:

```text
GEMINI_API_KEY
OPENAI_API_KEY
```

## Dữ liệu cục bộ

Các store chính trên Windows nằm ngoài repository, dưới `%LOCALAPPDATA%\PersonalAI`.

Ví dụ:

```text
Memory\memories.json
Knowledge\personal-ai.db
Knowledge\files\
Tasks\personal-tasks.db
Audit\audit.db
Undo\undo.db
Workspace\
```

Workspace phụ có storage riêng cho Knowledge và Files. Conversations được namespace theo workspace trong browser localStorage.

Một số root có thể override bằng configuration/environment, gồm:

```text
Workspace:Root / Workspace__Root
Tasks:Root     / Tasks__Root
Audit:Root     / Audit__Root
Undo:Root      / Undo__Root
```

## Kiến trúc mức cao

```text
Browser
  │
  ├── Conversations (localStorage, workspace scoped)
  │
  ▼
ASP.NET Core
  │
  ├── Workspace boundary
  ├── Memory
  ├── Documents / RAG
  ├── Tasks
  ├── Files
  ├── Audit
  ├── Undo
  │
  ├── Context Manager
  │      ├── Memory selection
  │      ├── Document hybrid retrieval
  │      └── Task relevance
  │
  ├── Tool Framework
  │      ├── permission policy
  │      ├── confirmation
  │      ├── activity log
  │      ├── audit
  │      └── undo capture
  │
  └── IAiProvider
         ├── Gemini
         └── OpenAI
```

## Cấu trúc repository

```text
PersonalAI/
├── PersonalAI.sln
├── README.md
├── docs/
│   └── releases/
└── src/
    └── PersonalAI.Web/
        ├── AI-CONSTITUTION.md
        ├── Program.cs
        ├── Models/
        ├── Options/
        ├── Services/
        ├── Teams/
        └── wwwroot/
```

## CI

GitHub Actions hiện kiểm tra regression cho các nền chính, gồm:

- build .NET;
- Memory;
- Documents/RAG;
- embeddings/hybrid search;
- Tool Framework;
- Task persistence/recovery;
- task dependencies;
- Workspace isolation;
- Context Manager;
- Audit;
- Undo;
- Stable Core contract;
- JavaScript syntax;
- UI guardrails tiếng Việt.

## Release notes

Chi tiết từng mốc nằm trong:

```text
docs/releases/
```

Mốc Stable Core:

```text
docs/releases/v1.0.0.md
```

Mốc capability hiện tại:

```text
docs/releases/v1.1.0.md
```

## Hướng phát triển sau v1.1

Computer Use v1.1 đã mở capability desktop đầu tiên theo mô hình permission + confirmation. Theo roadmap, bước kế tiếp là **v1.2 — Browser Agent**, với browser-specific observation/action contract, origin/URL guard, confirmation cho external side effects và audit đầy đủ.

Browser Agent không nên dựa vào click desktop mù làm cơ chế chính.
