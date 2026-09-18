# AI Cá Nhân — Life Context Foundation v1.6.0

PersonalAI là trợ lý AI cá nhân chạy bằng ASP.NET Core 8, ưu tiên dữ liệu cục bộ, có thể dùng Gemini hoặc OpenAI cho phần sinh câu trả lời. v1.0.0 vẫn là **Stable Personal AI Core**; v1.1 thêm Controlled Computer Use, v1.2 Browser Agent, v1.3 Connector Foundation, v1.4 Software Development Agent, v1.5 Android Companion và v1.6 thêm **Life Context Foundation** với explicit consent, retention, workspace isolation và context injection có budget.

> Đây vẫn là ứng dụng thiết kế cho một người dùng trên máy cá nhân. Không đưa trực tiếp lên Internet hoặc dùng như hệ thống nhiều người dùng nếu chưa bổ sung authentication, authorization và hardening triển khai phù hợp.

## Có gì trong v1.6.0?

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

v1.6 dùng strategy:

```text
workspace-scoped-budgeted-context-v2-life-context
```

Context được chọn từ:

- Memory
- Documents
- Tasks
- Life Context đã consent

Ngân sách mặc định:

- tổng vẫn giữ: 7.600 ký tự;
- Documents: tối đa 4.400 ký tự;
- Memory: tối đa 1.800 ký tự;
- Tasks: tối đa 1.400 ký tự;
- Life Context candidate budget: tối đa 1.200 ký tự;
- tối đa 4 document chunks;
- tối đa 4 memories;
- tối đa 3 tasks;
- tối đa 4 Life Context entries.

Life Context không làm tăng global ceiling; nó dùng phần context budget còn lại và chỉ được chọn khi source đang bật, consent còn hiệu lực, entry chưa hết hạn và query có liên quan.

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
- BROWSER
- CONNECTOR
- DEVELOPMENT

WRITE, DELETE, EXTERNAL, SENSITIVE, COMPUTER, BROWSER, CONNECTOR và DEVELOPMENT yêu cầu confirmation theo policy hiện tại. COMPUTER dùng cho desktop control; BROWSER dùng cho browser session/navigation; CONNECTOR dùng cho authenticated external integrations; DEVELOPMENT dùng cho source/process capability phục vụ phát triển phần mềm.

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

### Controlled Browser Agent

v1.2.0 thêm browser-specific layer, không dùng desktop click mù làm cơ chế duyệt web.

Engine hiện tại là `http-html`:

- HTTP/HTTPS GET;
- redirect validation;
- title/text extraction;
- link extraction;
- workspace-scoped session;
- SSRF guard ở URL, DNS và TCP connect.

Browser tools:

- `browser.session.info` — metadata phiên hiện tại, READ + BROWSER;
- `browser.navigate` — GET tới URL công khai, READ + EXTERNAL + BROWSER;
- `browser.page.observe` — đọc snapshot text/link, READ + BROWSER;
- `browser.link.open` — mở link theo index, READ + EXTERNAL + BROWSER.

Mọi browser tool cần explicit confirmation.

v1.2.0 cố ý chưa có JavaScript renderer, cookie/login, form submit, POST, download, upload, screenshot, click hoặc autonomous browsing loop.

Status:

```http
GET /api/browser/status
```

### Connector Foundation

v1.3.0 thêm connector layer riêng thay vì dùng Browser Agent để lách authentication.

Kind hiện tại:

`http-bearer`

Mỗi connection gồm:

- name;
- public HTTPS base origin;
- encrypted Bearer credential;
- workspace ownership;
- created/updated metadata.

Bearer token được mã hóa local bằng ASP.NET Core Data Protection và **không được trả lại qua API/UI**.

Connector tools:

- `connectors.list` — READ + SENSITIVE + CONNECTOR;
- `connector.http.get` — READ + EXTERNAL + SENSITIVE + CONNECTOR.

Mọi connector tool cần explicit confirmation.

Authenticated read hiện chỉ dùng HTTPS GET cùng origin. Cross-origin redirect không được phép mang Authorization header.

v1.3.0 chưa có POST/PUT/PATCH/DELETE qua connector, webhook send, email send, calendar write, upload hoặc provider-specific OAuth.

API quản lý local:

```http
GET    /api/connectors/status
GET    /api/connectors
POST   /api/connectors
DELETE /api/connectors/{id}?confirmed=true
```

Storage mặc định:

```text
%LOCALAPPDATA%\PersonalAI\Connectors\connections.json
```

### Software Development Agent

v1.4.0 thêm controlled development capability nhưng **không mở terminal tùy ý**.

Development tools:

- `dev.workspace.inspect` — phát hiện project manifest/ngôn ngữ trong workspace;
- `dev.search.text` — tìm text trong source mà không dùng grep/shell;
- `dev.git.status` — Git status read-only;
- `dev.git.diff` — Git diff read-only, external diff/textconv bị tắt;
- `dev.dotnet.restore` — restore target .NET cụ thể;
- `dev.dotnet.build` — build `--no-restore`;
- `dev.dotnet.test` — test `--no-restore`.

Tất cả dùng permission `DEVELOPMENT` và `SENSITIVE`, luôn cần explicit confirmation. Restore còn dùng `EXTERNAL`.

v1.4.0 cố ý chưa có:

- generic shell / cmd / PowerShell / sh;
- arbitrary executable hoặc arbitrary CLI args;
- git add/commit/push/pull/reset/checkout/merge/rebase;
- autonomous edit-build-test loop;
- background coding agent.

File edit vẫn dùng các `workspace.*` tools hiện có để giữ path sandbox, SHA guard và Undo.

Status:

```http
GET /api/development/status
```

`dotnet build/test` có thể chạy MSBuild task hoặc test code do project định nghĩa, nên chỉ nên dùng với workspace/repository mà người dùng tin cậy.

### Android Companion

v1.5.0 thêm ứng dụng Android native tại:

```text
android/PersonalAI.Companion/
```

Android Companion dùng one-time pairing code từ desktop. Mỗi device nhận một token riêng, bị khóa vào workspace đã pair.

Backend chỉ lưu **SHA-256 hash** của device token; token plaintext chỉ được trả đúng một lần lúc claim. App Android mã hóa token bằng **Android Keystore + AES-GCM**.

Client API:

```http
GET  /api/companion/client/me
GET  /api/companion/client/core
GET  /api/companion/client/tasks
POST /api/companion/client/chat
```

Android chat dùng chung Context Manager với desktop nhưng backend cưỡng chế `UseTools = false`, nên v1.5 không cho điện thoại chạy Computer/Browser/Connector/Development/Workspace tools.

Tasks trên Android là read-only. Không có execute/retry/resume/cancel qua companion namespace.

Desktop local quản lý pairing/device tại:

```http
POST   /api/companion/admin/pairing/start
GET    /api/companion/admin/devices
DELETE /api/companion/admin/devices/{id}?confirmed=true
```

Mặc định pairing/client API yêu cầu HTTPS. HTTP LAN chỉ được bật khi backend có `Companion__AllowInsecureHttp=true` và người dùng cũng opt-in trên app Android.

### Life Context Foundation

v1.6.0 thêm lớp context đời sống theo nguyên tắc **consent trước, collection sau**.

Supported source kinds:

- `manual`
- `calendar`
- `location`
- `activity`
- `device`
- `other`

Tạo source calendar/location/activity/device **không tự cấp quyền hệ điều hành và không bật collector**. v1.6 chỉ lưu snapshot do người dùng/import flow chủ động gửi.

Mỗi source có:

- workspace ownership;
- explicit consent;
- enabled state;
- retention từ 1–365 ngày;
- encrypted entry content.

Storage mặc định:

```text
%LOCALAPPDATA%\PersonalAI\LifeContext\life-context.json
```

Entry content được bảo vệ bằng ASP.NET Core Data Protection với protector `PersonalAI.LifeContext.v1`.

API chính:

```http
GET    /api/life-context/status
GET    /api/life-context/sources
POST   /api/life-context/sources
PATCH  /api/life-context/sources/{id}/enabled
PATCH  /api/life-context/sources/{id}/consent
POST   /api/life-context/sources/{id}/entries
GET    /api/life-context/entries
DELETE /api/life-context/entries/{id}
DELETE /api/life-context/sources/{id}?confirmed=true
```

Consent revoke có thể purge snapshot hiện có; UI mặc định revoke kèm purge. Entry hết retention bị loại khỏi query/context và được cleanup khỏi local encrypted store.

v1.6 cố ý chưa có background GPS, Android location permission, calendar auto-sync, activity recognition, health data, sensor collection hoặc cloud sync.

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

v1.6.0 giữ API contract của Stable Core và mở rộng controlled capability layer:

- Version: `1.6.0`
- API contract: `1`
- Channel: `controlled`
- Stable Core: v1.0 semantics
- Controlled modules: `computer-use`, `browser-agent`, `connectors`, `software-development`, `android-companion`, `life-context`

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

v1.6.0 **không phải autonomous agent**.

Hiện tại:

- Workspace isolation: bật
- WRITE confirmation: bắt buộc
- DELETE confirmation: bắt buộc
- EXTERNAL confirmation: bắt buộc
- Undo confirmation: bắt buộc
- Computer-control confirmation: bắt buộc
- Sensitive computer observation confirmation: bắt buộc
- Browser confirmation: bắt buộc
- Browser private-network access: tắt
- Browser side effects/form submit: tắt
- Connector confirmation: bắt buộc
- Connector credential encryption: bật
- Connector write actions: tắt
- Development confirmation: bắt buộc
- Development arbitrary shell: tắt
- Development arbitrary process: tắt
- Development Git write actions: tắt
- Android pairing: bắt buộc
- Android device token: backend chỉ lưu hash
- Android remote tool execution: tắt
- Android remote task mutation: tắt
- Life Context explicit consent: bắt buộc
- Life Context automatic collection: tắt
- Life Context entry content encryption: bật
- Automatic multi-step execution: tắt
- Background scheduler: tắt
- Autonomous agent loop: tắt
- Parallel tool calls: tắt

Chưa có:

- JavaScript browser automation;
- browser form submit/login/cookies;
- browser download/upload;
- screenshot/OCR;
- mouse click hoặc keyboard typing;
- background scheduler;
- autonomous agent loop;
- automatic confirmation;
- Gmail/Calendar/Microsoft provider-specific OAuth;
- connector write actions;
- arbitrary shell/process execution;
- Git write actions;
- autonomous coding loop;
- Android remote tool/task mutation;
- Android background agent/sensor collection;
- Life Context background GPS/calendar/activity/sensor collection;
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
Connectors\connections.json
Companion\devices.json
LifeContext\life-context.json
Workspace\
```

Workspace phụ có storage riêng cho Knowledge và Files. Conversations được namespace theo workspace trong browser localStorage.

Một số root có thể override bằng configuration/environment, gồm:

```text
Workspace:Root / Workspace__Root
Tasks:Root     / Tasks__Root
Audit:Root     / Audit__Root
Undo:Root      / Undo__Root
Connectors:Root / Connectors__Root
Companion:Root  / Companion__Root
LifeContext:Root / LifeContext__Root
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
  ├── Connectors
  ├── Software Development Agent
  ├── Android Companion
  ├── Life Context
  │
  ├── Context Manager
  │      ├── Memory selection
  │      ├── Document hybrid retrieval
  │      ├── Task relevance
  │      └── Life Context relevance + consent
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
├── android/
│   └── PersonalAI.Companion/
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
- Android Companion pairing/auth/isolation;
- Android debug APK build;
- Life Context consent/encryption/retention/isolation;
- Context Manager Life Context selection/opt-out;
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

Các mốc capability:

```text
docs/releases/v1.1.0.md
docs/releases/v1.2.0.md
docs/releases/v1.3.0.md
docs/releases/v1.4.0.md
docs/releases/v1.5.0.md
docs/releases/v1.6.0.md
```

## Hướng phát triển sau v1.6

Computer Use, Browser Agent, Connector Foundation, Software Development Agent, Android Companion và Life Context hiện nằm trong controlled capability layer.

Theo roadmap gốc, mốc kế tiếp là **v1.7 — Decision Engine**, sau đó là **v1.8 Automation → v1.9 Reliability / Security → v2.0 Personal AI OS**.
