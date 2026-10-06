# Rà soát kiến trúc Computer Operator sau v4.9.7

## Mục tiêu

Rà soát theo nguyên tắc: không xóa/gộp chỉ vì tên giống nhau; chỉ gộp khi trách nhiệm thực sự trùng. Giữ Computer Operator là core tổng quát, không hard-code ứng dụng.

## Kết luận chính

### 1. FIX NGAY — Progress Intelligence và Learned Timing chưa đi vào main runtime

Trước đợt rà soát, `ComputerOperatorTaskService` vẫn gọi:

- `ComputerOperatorAdaptiveWaitPolicy.ForAction(...)`
- `adaptiveWait.WaitAsync(...)`

nên `IComputerOperatorProgressIntelligence` và `IComputerOperatorAdaptiveWaitPolicyResolver` tồn tại nhưng chưa tác động đầy đủ tới đường chạy thật.

Đã sửa:
- Inject `IComputerOperatorProgressIntelligence`.
- Inject `IComputerOperatorAdaptiveWaitPolicyResolver`.
- Main Adaptive Wait dùng resolver learned timing.
- Main post-action observation đi qua Progress Intelligence trước khi quyết định trạng thái Adaptive Wait.

### 2. KHÔNG TẠO MỚI — Unified Capability Registry

Repo đã có:
- `IToolCapabilityRegistry / ToolCapabilityRegistry`
- `ICapabilityToolRouter / CapabilityToolRouter`

Vì vậy roadmap MCP không được tạo thêm một registry song song.

Hướng đúng:
- mở rộng `UnifiedToolCapability` để có source/provider/health/reliability nếu cần;
- MCP adapter đăng ký capability vào cùng contract;
- CapabilityToolRouter tiếp tục là lớp chọn tool theo channel/risk/verification.

### 3. GIỮ RIÊNG — Tool capability và runtime capability

Hai hệ sau không trùng trách nhiệm:

- `ToolCapabilityRegistry`: logical tool contract, permission, risk, confirmation, verification.
- `UniversalCapabilityDiscoveryService + UniversalCapabilityCache`: backend/runtime availability và health (FlaUI, Win32, Playwright, Vision...).

Không gộp thành một class. Về sau chỉ bridge runtime health vào ranking của tool capability.

### 4. GIỮ RIÊNG — Personal Memory và Operator Experience

- `PersonalMemoryStore`: fact/preference/rule của người dùng.
- `IComputerOperatorExperienceRepository`: machine/operator experience từ failure/recovery/strategy.

Không được trộn hai nguồn này vì khác vòng đời, privacy và tiêu chí tin cậy.

### 5. GIỮ NHƯNG CẦN FACADE — Recovery stack

Các lớp hiện tại có trách nhiệm khác nhau:

- `ComputerOperatorRecoverySession`: memory cục bộ trong một task.
- `ComputerOperatorFailureRecoveryEngine`: lập recovery plan.
- `ComputerOperatorLoopGuardSession`: phát hiện retry/oscillation/stagnation.

Không xóa/gộp dữ liệu. Về sau nên có một Recovery Coordinator/Facade để main task service không phải tự điều phối quá nhiều class.

### 6. CẦN REFACTOR DẦN — static engine trong ComputerOperatorTaskService

Task service hiện còn tự tạo static instance cho một số engine/router như:
- DesktopVerificationRouter
- ComputerOperatorConfidenceEngine
- ComputerOperatorFailureRecoveryEngine
- DesktopLocalActionPlanner
- UnifiedDecisionKernel
- StructuredTargetRevalidator

Điều này làm interface/DI không phát huy hết tác dụng và khó cắm learning/MCP/test double.

Không refactor tất cả trong một commit vì rủi ro regression cao. Chuyển dần sang DI ở các milestone tiếp theo.

### 7. CẦN SỬA TRƯỚC FAST PATH — Learned Timing hiện chưa bền qua restart

`ComputerOperatorTelemetry` hiện khai báo `MemoryOnly=true`.
Do đó Learned Timing hiện học tốt trong phiên chạy nhưng mất dữ liệu khi process restart.

Hướng đúng:
- không ghi mọi telemetry thô lâu dài;
- persist timing profile đã aggregate (sample count, median/P90/P95, last verified) vào Experience DB;
- raw timing chỉ giữ ngắn hạn.

Đưa việc này vào Data Lifecycle/Consolidation trước khi Fast Path được bật.

### 8. EXPERIENCE DB — nền đúng nhưng chưa được phép tăng vô hạn

Hiện có safety cap 5.000 event/workspace.
Đây chỉ là bảo vệ tạm thời.

Trước khi hệ thống tạo event learning với lưu lượng lớn phải có:
- candidate validation;
- consolidation;
- retention/archive;
- decay/supersession;
- aggregated timing/strategy stats.

Không tăng cap để giải quyết vấn đề dữ liệu.

### 9. CI — có nhiều workflow lịch sử cần lập bản đồ coverage

Repo có 64 workflow YAML, nhiều workflow theo milestone cũ.

Phần lớn workflow cũ chỉ trigger branch lịch sử/path hẹp nên chưa tạo tải trên branch Computer Operator hiện tại, nhưng đây vẫn là maintenance debt.

Không xóa hàng loạt.
Hướng dọn:
1. map workflow -> capability hiện còn được bảo vệ;
2. chuyển test còn giá trị vào CI/acceptance hiện tại;
3. archive/delete workflow chỉ khi coverage đã được thay thế;
4. giữ tối thiểu các gate hiện hành.

### 10. League-specific code

`LeaguePracticeAutomationService` và `LeaguePracticeOpenTool` là feature/test-specific code.
Có thể tồn tại nếu không được Computer Operator core phụ thuộc.

Nguyên tắc:
- không thêm logic League vào planner/runtime/recovery/memory core;
- dùng League như stress test/adapter riêng;
- về sau có thể chuyển vào feature/sample namespace nếu cần làm sạch cấu trúc.

## Boundary chuẩn sau rà soát

Goal
-> UniversalTaskRouter (chọn channel)
-> CapabilityToolRouter (chọn logical tool)
-> ExecutionGateway / ExecutionAgent
-> Computer Operator core
-> Observe
-> Runtime/Progress Intelligence
-> Experience Retrieval
-> Plan/Strategy
-> Safe Executor (one action)
-> Verify
-> Recovery nếu cần
-> Learning candidate
-> Validator
-> Experience Store

MCP chỉ đi vào Tool/Capability boundary, không chen trực tiếp giữa planner và Win32 executor.

## Roadmap đã chỉnh để tránh trùng

- v4.9.8 Failure -> Recovery -> Learning Candidate
- v4.9.9 Experience Validator
- v4.9.10 Experience Consolidation + persistent timing profiles
- v4.9.11 Retention / Archive / Cleanup
- v4.9.12 Retrieval / Vector Memory
- v4.9.13 Strategy Ranking + Fast Path
- v4.9.14 Decay + Supersession
- v4.9.15 Recovery/Loop Guard Coordinator integration
- v4.9.16 Extend existing ToolCapabilityRegistry for source/health/reliability
- v4.9.17 MCP Adapter + Safety + Audit
- Reliability/Continual Learning Gate
- v5.0 Stable Self-Learning General Computer Operator

Không tạo một "Unified Capability Registry" thứ hai.
