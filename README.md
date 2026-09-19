# AI Cá Nhân — Orchestration Reliability v2.2.2

PersonalAI là trợ lý AI cá nhân chạy bằng ASP.NET Core 8, ưu tiên dữ liệu cục bộ, có thể dùng Gemini hoặc OpenAI cho phần sinh câu trả lời. v2.0.0 đã tạo **Personal AI OS**; v2.1.0 thêm Agent Framework; v2.1.1 thêm **Planner Agent**; v2.1.2 bổ sung **Research Agent**; v2.1.3 có **Developer Agent**; v2.1.4 bổ sung **Office Agent**; v2.1.5 thêm **Operator Agent**; v2.1.6 bổ sung **Reviewer Agent**; v2.1.7 thêm **Security Agent** rà soát rủi ro cục bộ; **v2.2.0 bổ sung workflow tuần tự có xác nhận** để gọi 2–3 agent đã được người dùng chọn trước và chuyển đầu ra khi có opt-in. **v2.2.1** thêm giới hạn thời gian từng bước/toàn workflow và bộ kiểm tra mẫu credential trước handoff. Chưa có auto-delegation, agent tự chạy tool, shared queue, autonomous loop hoặc parallel agents.

> Đây vẫn là ứng dụng thiết kế cho một người dùng trên máy cá nhân. Personal AI OS v2.0 giữ toàn bộ hardening của v1.9 nhưng **không thay thế authentication/authorization** cần có khi triển khai Internet hoặc môi trường nhiều người dùng.

## Có gì trong v2.2.2?

### Orchestration Reliability

Trước khi chuyển đầu ra có opt-in sang agent tiếp theo, workflow tạm dừng tại **checkpoint cần người dùng xem đúng nội dung sẽ chuyển** (tối đa 1.600 ký tự) rồi đồng ý hoặc từ chối. Đồng ý mới gọi bước nhận; từ chối thì dừng workflow. Checkpoint có token dùng một lần và digest của nội dung, ràng buộc vào workflow/workspace; checkpoint chờ tối đa 10 phút, lưu tạm trong bộ nhớ server, mất khi server khởi động lại.

- API: `POST /api/agents/orchestration/resume` bổ sung cho execute/status. Kết quả có trạng thái `awaiting-review` và thông tin checkpoint để giao diện xác nhận riêng.
- Workflow giữ 120 giây **thời gian thực thi chủ động** qua các lần resume, không tính thời gian người dùng đang đọc checkpoint; mỗi bước vẫn giới hạn 50 giây theo cơ chế huỷ hợp tác.
- Bộ lọc mẫu credential không nhận ra mọi bí mật hoặc prompt injection; cần tự kiểm tra nội dung trước khi chấp thuận gửi đến AI provider đã cấu hình. Không chạy tool, tự chọn agent, tự phê duyệt hay lưu workflow bền vững.
- Xem `docs/releases/v2.2.2.md` để biết giới hạn và hợp đồng API.

### Workflow Hardening v2.2.1 vẫn được giữ nguyên

## Có gì trong v2.2.1?

### Workflow Hardening

Workflow được giới hạn **120 giây tổng** và **50 giây cho từng bước** qua cancellation token; nếu timeout được phát hiện, workflow dừng, trả các bước đã hoàn tất và không chạy bước tiếp theo. Đây là giới hạn huỷ hợp tác; nếu AI provider không hỗ trợ cancellation thì không có bảo đảm dừng tức thì.

Handoff vẫn cần checkbox của người dùng tại **mỗi bước nhận**. Trước khi chuyển, bộ quy tắc cục bộ kiểm tra toàn bộ đầu ra trước đó (kể cả ngoài cửa sổ 1.600 ký tự) và chặn khi phát hiện một số mẫu private key, bearer token hoặc credential assignment. Kết quả báo lỗi không chứa giá trị khớp. Bộ lọc **không bảo đảm phát hiện mọi bí mật** và không thay thế kiểm tra nội dung trước khi chia sẻ với AI provider.

- API giữ nguyên `GET /api/agents/orchestration/status`, `POST /api/agents/orchestration/execute`; status bổ sung thời gian và cờ kiểm tra handoff, output có `stopReason` và trạng thái `timed-out`.
- Không agent tự chọn nhiệm vụ/agent, không chạy tool, chỉnh sửa file, chia sẻ tự động, ghi workflow hoặc tự xác nhận side effects.
- Tài liệu đầy đủ: `docs/releases/v2.2.1.md`. Mốc tiếp theo là **v2.2.2 — Orchestration Reliability**.

### Controlled Agent Orchestration v2.2.0 vẫn được giữ nguyên

## Có gì trong v2.2.0?

### Controlled Agent Orchestration

Người dùng mở **Agents → Workflows · v2.2.0**, chọn agent và mục tiêu riêng cho từng bước (2–3 bước), chọn checkbox ở bước nhận nếu cho phép chuyển tối đa 1.600 ký tự đầu ra từ bước liền trước, rồi xác nhận toàn bộ workflow. Context bổ sung mặc định tắt. Backend gọi **thực sự từng executable agent theo thứ tự** qua Agent Framework, trả kết quả và dừng khi có bước lỗi.

- API: `GET /api/agents/orchestration/status`, `POST /api/agents/orchestration/execute`.
- Không xác nhận toàn bộ workflow → HTTP 400 trước khi agent nào chạy; handoff cần checkbox ở từng bước. Không tự chọn/thêm agent, chạy tool, sửa file, gửi tin, tạo task hoặc tự lưu workflow.
- Agent sử dụng AI provider có thể nhận mục tiêu, context được người dùng bật và đầu ra đã được opt-in. Không nhập bí mật; nhãn "dữ liệu chỉ đọc" không bảo đảm mô hình tuyệt đối miễn nhiễm prompt injection.
- Chỉ cho phép một workflow cùng lúc, tối đa một agent execution tại một thời điểm. **Không có** agent-initiated messaging, automatic delegation, shared queue, autonomous loop hoặc parallel execution.
- Mốc tiếp theo là **v2.2.1 — Workflow Hardening**, chưa triển khai.

### Security Agent v2.1.7 vẫn được giữ nguyên

## Có gì trong v2.1.7?

### Security Agent

Executable agent thứ tám `security.security-reviewer` rà soát **mẫu rủi ro cục bộ** trong mô tả thao tác do người dùng chủ động nhập (tối đa 4.000 ký tự). Bộ kiểm tra cố định cảnh báo một số mẫu chứa credential/private key, xóa dữ liệu khó hoàn tác, tải-và-chạy script, nới quyền hoặc chuyển dữ liệu ra bên ngoài.

- API: `GET /api/security-agent/status`, `GET /api/agents/security.security-reviewer`, `POST /api/agents/security.security-reviewer/execute`.
- Không gọi AI provider, không tự đọc file/repo/context, không lặp lại mô tả người dùng hoặc chuỗi khớp mẫu trong báo cáo.
- Không phát hiện mẫu **không có nghĩa an toàn**. Kết quả không thay thế permission gate, human review, quét bảo mật toàn hệ thống hoặc pentest; có thể có false positives và false negatives.
- Không tự thực thi, sửa permission, chặn hành động, gửi dữ liệu, tạo task hoặc dispatch agent. Không nhập mật khẩu/token thật vào ô yêu cầu.

### Reviewer Agent v2.1.6 vẫn được giữ nguyên

## Có gì trong v2.1.6?

### Reviewer Agent

Executable agent thứ bảy `quality.reviewer` rà soát văn bản/bản nháp/kế hoạch người dùng **dán trực tiếp** (80–4.000 ký tự) và trả báo cáo `needs-user-review` có `summary`, tối đa 8 findings, questions và limitations.

- API: `GET /api/reviewer-agent/status`, `GET /api/agents/quality.reviewer`, `POST /api/agents/quality.reviewer/execute`.
- Mỗi finding có `evidenceExcerpt` khớp **nguyên văn** trong nội dung được dán, observation, suggestedRevision và verificationStep. Trích đoạn không tồn tại sẽ bị parser từ chối.
- Nếu không đủ nội dung, trả `insufficient-material` mà không gọi AI provider.
- Không tự đọc file, history, bản nháp agent khác, duyệt web, chạy test, sửa hoặc phê duyệt. Kiểm tra chuỗi excerpt không có nghĩa đã xác minh nhận định hay thông tin thực tế bên ngoài.
- Văn bản dán có thể được gửi tới AI provider đã cấu hình. Không dán bí mật hoặc dữ liệu nhạy cảm nếu không muốn chia sẻ.

### Operator Agent v2.1.5 vẫn được giữ nguyên

## Có gì trong v2.1.5?

### Operator Agent

Executable agent thứ sáu `operations.operator` nhận mục tiêu và tạo một kế hoạch thao tác **chưa thực thi** gồm 1–10 bước: channel (browser/computer/connector/manual), riskLevel, dependsOn, requiresUserConfirmation, kết quả mong đợi và cách kiểm chứng. Những checkpoint cần xác nhận chỉ là metadata để người dùng xem, **không cấp quyền thực thi**.

- API: `GET /api/operator-agent/status`, `GET /api/agents/operations.operator`, `POST /api/agents/operations.operator/execute`.
- Parser từ chối dependency vòng/trỏ về bước sau, channel/risk không hợp lệ và bước high-risk không có checkpoint.
- Không tự dùng browser/computer/connector/tool, không sửa file, gửi tin, tạo task, ghi kế hoạch hoặc dispatch agent.
- Context bật bởi người dùng có thể được gửi đến AI provider; tuyệt đối không nhập password/token/OTP vào mục tiêu.

### Office Agent v2.1.4 vẫn được giữ nguyên

## Có gì trong v2.1.4?

### Office Agent

Executable agent thứ năm `office.office-assistant` soạn **bản nháp** email, memo, agenda, minutes và summary từ yêu cầu người dùng và các nguồn context được bật. Kết quả có `kind`, `title`, `body`, `actionItems` và `missingInformation`, hiển thị trong sidebar **Agents**.

- API: `GET /api/office-agent/status`, `GET /api/agents/office.office-assistant`, `POST /api/agents/office.office-assistant/execute`.
- Parser giới hạn loại bản nháp, nội dung và số mục; kết quả luôn là draft chưa gửi hoặc lưu tự động.
- `actionItems` chỉ là lời đề xuất, không phải tác vụ đã được thực hiện.
- Không gửi email, không tự tạo calendar event/task, không chỉnh sửa/lưu tài liệu, không tự gọi tool và không điều phối agent.
- Context được người dùng bật có thể được gửi đến AI provider đã cấu hình. Cần rà soát thông tin, người nhận và quyết định trước khi dùng bản nháp.

### Developer Agent v2.1.3 vẫn được giữ nguyên

## Có gì trong v2.1.3?

### Developer Agent

Executable agent thứ tư: `development.developer`. Agent xem project metadata, tìm tối đa **24 đoạn mã** theo từ khóa trong workspace, rồi tạo báo cáo gồm nhận định, chỉ số nguồn mã, đề xuất thay đổi và bước kiểm chứng. Có thể dùng cú pháp `search: ExecuteAsync` để tìm chính xác. Nếu không có dòng mã phù hợp, agent trả trạng thái `insufficient-context` mà không gọi AI provider.

- API: `GET /api/developer-agent/status`, `GET /api/agents/development.developer`, `POST /api/agents/development.developer/execute`.
- Source-index validation chặn các vị trí snippet bịa đặt; **không bảo đảm mọi nhận định của mô hình được mã hỗ trợ đúng**.
- Không tự gọi development tools, chạy build/test/git/shell, sửa file, tạo commit, task hoặc dispatch agent.
- Người dùng chọn Developer Agent trong sidebar **Agents** và nhận báo cáo đọc-only. Các snippet khớp từ khóa có thể được gửi tới AI provider đã cấu hình theo thao tác chủ động này; không dùng chức năng với mã nhạy cảm nếu không muốn gửi ra ngoài.

### Research Agent v2.1.2 vẫn được giữ nguyên

## Có gì trong v2.1.2?

### Research Agent

Research Agent thứ ba: `research.researcher`, hoạt động trên tập tài liệu trong workspace hiện tại. Context Manager truy xuất tối đa 4 đoạn Knowledge/RAG; nếu không tìm được đoạn phù hợp, agent trả `insufficient-evidence` mà không gọi AI provider. Khi có nguồn, agent tạo báo cáo có `summary`, `findings`, `sourceNumbers`, `unansweredQuestions`, `limitations` và siêu dữ liệu các đoạn tài liệu đã truy xuất.

- API: `GET /api/research/status`, `GET /api/agents/research.researcher`, `POST /api/agents/research.researcher/execute`.
- Parser chặn chỉ số nguồn không tồn tại; nhận định `sourced` phải tham chiếu một nguồn trong tập truy xuất.
- UI Agents hiển thị findings, chỉ số trích dẫn và tên tài liệu/đoạn/trang khi có.
- Không tự duyệt web/email, chạy browser/connector/tool, tạo task, persist báo cáo hay dispatch agent.
- Kiểm tra chỉ số nguồn **không bảo đảm nhận định của mô hình thật sự được văn bản hỗ trợ**; cần đối chiếu đoạn nguồn trước khi sử dụng.

### Planner Agent v2.1.1 vẫn được giữ nguyên

## Có gì trong v2.1.1?

### Planner Agent

v2.1.1 triển khai agent thứ hai: `planning.planner`.

Planner nhận một goal và trả về plan có cấu trúc gồm:

- summary;
- 2–12 steps;
- dependency giữa các step;
- suggested role;
- required capabilities;
- expected outcome;
- open questions;
- assumptions;
- risks.

Boundary của Planner:

- `CreatesTasks = false`;
- `DispatchesAgents = false`;
- `PersistsAutomatically = false`;
- `ToolExecutionEnabled = false`;
- explicit invocation bắt buộc;
- dependency chỉ được trỏ tới step trước, nên parser chặn dependency vòng ngay ở lớp plan.

Agent Framework hiện có:

- `core.personal-assistant`;
- `planning.planner`.

API chung:

```http
GET  /api/agents/status
GET  /api/agents
GET  /api/agents/{agentId}
POST /api/agents/{agentId}/execute
```

API Planner:

```http
GET /api/planner/status
```

Trong sidebar **Agents**, khi chọn Planner Agent, nút chuyển thành **Lập kế hoạch** và kết quả được hiển thị theo step/dependency/role/outcome. Plan chỉ ở trạng thái `prepared`; hệ thống chưa tự biến plan thành Task Engine task và chưa phân phối step cho agent khác.

### Agent Framework v2.1.0 vẫn là nền

### Personal AI OS

v2.0 thêm một lớp hệ thống thống nhất ở trên các subsystem đã có:

- `/api/os/status` — readiness theo workspace hiện tại, capability state và governance gate;
- `/api/os/manifest` — OS layers, capability map và các ranh giới autonomy;
- 5 lớp: Knowledge & Context, Planning & Decision, Tools & Automation, Device & External Interfaces, Governance & Recovery;
- 18 capability được phân loại `ready`, `controlled`, `foundation`, `unconfigured` hoặc `unavailable`;
- readiness gate trước v2.1 yêu cầu Workspace, Tasks, Tools, Audit và Hardening không unavailable;
- sidebar có bảng điều khiển **Personal AI OS** để xem trạng thái hệ thống.

Email và Calendar được biểu diễn đúng mức triển khai hiện tại:

- Email = connector-backed read foundation, chưa có Gmail/Outlook provider-specific OAuth hoặc send action;
- Calendar = Life Context calendar snapshot + connector foundation, chưa có provider-specific auto-sync/write.

v2.0 **không** bật Multi-Agent, Agent Orchestration, autonomous agent loop hay parallel tool calls. Các phần đó bắt đầu từ roadmap v2.1 trở đi.

### Reliability & Security Hardening từ v1.9 vẫn được giữ nguyên

### Reliability & Security Hardening

v1.9 bổ sung lớp bảo vệ vận hành trước v2.0:

- API rate guard mặc định 600 request/phút cho mỗi IP + workspace;
- giới hạn tối đa 16 API request xử lý đồng thời;
- chặn API request lớn hơn 12 MB trước khi đi sâu vào pipeline;
- runtime heartbeat và crash marker để phát hiện lần chạy trước kết thúc không sạch;
- permission audit chạy trên **policy thật** của Tool Framework để phát hiện công cụ rủi ro có thể bypass confirmation;
- backup toàn bộ cây dữ liệu PersonalAI mặc định với giới hạn 20.000 file / 512 MB nguồn;
- SQLite được snapshot bằng SQLite backup API thay vì copy file database đang mở;
- giữ tối đa 5 backup gần nhất;
- restore theo cơ chế **queue → restart → staging → pre-restore backup → apply → rollback khi lỗi**;
- maintenance lock cross-process để hai thao tác backup/restore không chạy chồng nhau;
- Hardening được đưa vào `/api/system/health` và có status API riêng.

API mới:

```http
GET  /api/hardening/status
GET  /api/hardening/permissions
GET  /api/hardening/backups
POST /api/hardening/backups
POST /api/hardening/restore
```

Tạo backup và xếp hàng restore đều yêu cầu xác nhận rõ ràng. Restore không thay dữ liệu ngay trong process đang chạy; nó chỉ được áp dụng ở lần khởi động tiếp theo.

> Phạm vi backup v1.9 là cây dữ liệu mặc định dưới `%LOCALAPPDATA%\\PersonalAI`. Store được redirect sang đường dẫn ngoài bằng các cấu hình `*:Root` chưa được tự động gom vào archive này.

### Nền tảng chức năng từ v1.8 vẫn được giữ nguyên

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

### Decision Engine

v1.7.0 thêm lớp hỗ trợ ra quyết định dựa trên cùng Context Manager đang phục vụ chat.

Input gồm:

- câu hỏi quyết định;
- 2–8 lựa chọn;
- tối đa 8 tiêu chí;
- constraints tùy chọn;
- cờ bật/tắt Documents, Memory, Tasks và Life Context.

API:

```http
GET  /api/decisions/status
POST /api/decisions/preview
POST /api/decisions/analyze
```

`preview` chỉ chọn context cục bộ, không gọi Gemini/OpenAI.

`analyze` dùng active AI provider để tạo decision brief gồm evidence, so sánh lựa chọn, trade-off, risk và uncertainty.

Decision Engine cố ý **không**:

- implement `IPersonalAiTool`;
- dùng Tool Orchestration;
- chạy Task Engine;
- auto-action;
- auto-schedule;
- lưu question/options/analysis mặc định.

Mọi response đều giữ contract:

```text
requiresUserDecision = true
autoActionEnabled = false
toolExecutionEnabled = false
```

Recommendation không phải authorization để PersonalAI hành động.

### Controlled Automation

v1.8.0 thêm scheduler nền cho **task đã tồn tại**.

Automation hỗ trợ:

- one-time schedule;
- interval schedule từ 5 phút đến 7 ngày;
- tối đa 50 automation / workspace;
- persistence bằng SQLite;
- restart recovery;
- run-now;
- pause / enable;
- resume sau review;
- workspace isolation;
- Audit.

Boundary quan trọng:

```text
mỗi scheduler tick
        ↓
tối đa 1 task step
        ↓
chỉ LocalOnly + READ + không confirmation
```

Scheduler luôn gọi Task Engine với:

```text
confirmed = false
```

Nếu step cần WRITE, DELETE, EXTERNAL, SENSITIVE, COMPUTER, BROWSER, CONNECTOR, DEVELOPMENT hoặc confirmation riêng:

```text
automation
   ↓
awaiting-confirmation
   ↓
enabled = false
```

Không có đường auto-confirm.

Decision Engine recommendation cũng không tự tạo/chạy automation; muốn hành động vẫn phải là một request riêng do người dùng xác nhận.

API:

```http
GET    /api/automations/status
GET    /api/automations
GET    /api/automations/{id}
POST   /api/automations
PATCH  /api/automations/{id}/enabled
POST   /api/automations/{id}/resume
POST   /api/automations/{id}/run-now
DELETE /api/automations/{id}?confirmed=true
```

Storage:

```text
%LOCALAPPDATA%\PersonalAI\Automation\automations.db
```

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

v2.1.1 giữ API contract của Stable Core, Personal AI OS v2.0, Agent Framework v2.1.0 và thêm controlled Planner Agent:

- Version: `2.1.1`
- API contract: `1`
- Channel: `controlled`
- Stable Core: v1.0 semantics
- Controlled modules: `computer-use`, `browser-agent`, `connectors`, `software-development`, `android-companion`, `life-context`, `decision-engine`, `automation`

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

v2.1.1 **không phải autonomous agent**.

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
- Decision Engine yêu cầu user quyết định: bật
- Decision Engine auto-action: tắt
- Decision Engine tool execution: tắt
- Decision Engine persistent analysis: tắt
- Automation explicit create: bắt buộc
- Automation auto-confirm: tắt
- Decision recommendation auto-execution: tắt
- Automation tối đa một task step mỗi tick: bật
- Automatic multi-step execution: tắt
- Background scheduler: bật
- Agent Framework: bật
- Planner Agent: bật
- Planner creates Task Engine tasks: tắt
- Planner dispatches agents: tắt
- Planner persists plans automatically: tắt
- Agent explicit invocation: bắt buộc
- Agent tool execution: tắt
- Agent messaging: tắt
- Automatic agent delegation: tắt
- Parallel agent execution: tắt
- Agent Orchestration: tắt
- Autonomous agent loop: tắt
- Parallel tool calls: tắt

Chưa có:

- JavaScript browser automation;
- browser form submit/login/cookies;
- browser download/upload;
- screenshot/OCR;
- mouse click hoặc keyboard typing;
- cron expression tùy ý / sub-minute schedule;
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
- Decision Engine automatic action/tool execution;
- Decision → automation implicit execution;
- background WRITE/DELETE/EXTERNAL/SENSITIVE action;
- persistent decision history;
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
Automation\automations.db
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
Automation:Root  / Automation__Root
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
  ├── Decision Engine
  ├── Automation
  ├── Hardening / Backup / Recovery
  ├── Personal AI OS Manifest / Readiness
  ├── Agent Framework
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
- Decision Engine validation/context preview/no-auto-action contract;
- Automation persistence/workspace isolation/one-step scheduler/no-auto-confirm;
- v1.9 hardening health/permission audit/rate-resource guards/confirmation boundaries;
- v2.0 Personal AI OS manifest/readiness/governance/capability-state contract;
- v2.1 Agent Framework registry/explicit execution/context isolation/no-tool-execution contract;
- v2.1.2 Research Agent source-bound reporting, insufficient-evidence fallback và citation-index validation;
- v2.1.3 Developer Agent workspace-only snippet review, no-write/no-command policy và evidence-index validation;
- v2.1.4 Office Agent structured draft-only output, parser validation và no-send/no-write/no-task policy;
- v2.1.5 Operator Agent risk-aware plan-only output, dependency validation và no-execution policy;
- v2.1.6 Reviewer Agent excerpt validation, no-external-verification và no-approval policy;
- v2.1.7 Security Agent local pattern review, no-input-echo, no-enforcement và no-safety-certification policy;
- v2.2.0 bounded sequential workflow, explicit user approval/handoff, stop-on-error và no-tool/no-autonomy policy;
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
docs/releases/v1.7.0.md
docs/releases/v1.8.0.md
docs/releases/v1.9.0.md
docs/releases/v2.0.0.md
docs/releases/v2.1.0.md
docs/releases/v2.1.1.md
docs/releases/v2.1.2.md
docs/releases/v2.1.3.md
docs/releases/v2.1.4.md
docs/releases/v2.1.5.md
docs/releases/v2.1.6.md
docs/releases/v2.1.7.md
docs/releases/v2.2.0.md
docs/releases/v2.2.1.md
docs/releases/v2.2.2.md
```

## Hướng phát triển sau v2.2.2

Computer Use, Browser Agent, Connector Foundation, Software Development Agent, Android Companion, Life Context, Decision Engine và Automation hiện nằm trong controlled capability layer.

Sau v2.2.2, mốc kế tiếp là **v2.2.3 — Workflow Observability**. Web/email research và tự thực thi tool chưa được tích hợp vào điều phối; việc mở thêm quyền cần thiết kế độc lập, truy xuất nguồn và permission/confirmation rõ ràng.
