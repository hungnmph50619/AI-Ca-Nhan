# Human-like Computer Operator Roadmap — v3.1 → v4.0

Mục tiêu: AI-Ca-Nhan thao tác Windows theo vòng lặp nhìn thấy được và kiểm chứng được như một người dùng thật:

```text
Observe → Understand → Plan one small step → Act visibly → Wait → Verify → Recover/Continue
```

## Nguyên tắc

- Mỗi thao tác desktop phải có permission/confirmation phù hợp.
- Emergency stop `Ctrl + Shift + F12` luôn có quyền ưu tiên.
- Mọi thao tác đều có progress/audit; không chạy "tàng hình".
- Không nhập mật khẩu, OTP hoặc bí mật bằng generic keyboard tools.
- Với League of Legends: chỉ tự động client/menu. Khi trận thật bắt đầu, hệ thống chuyển về observe/analyze/advise và không tự chơi.

## v3.1.x — Human Input Foundation

### v3.1.0
- focus/minimize/maximize/restore cửa sổ;
- di chuột trực tiếp và di chuột mượt nhìn thấy được;
- click trái/phải/double-click;
- scroll;
- drag-and-drop;
- nhập text foreground có confirmation;
- phím đơn;
- hotkey 2–4 phím;
- emergency stop + bounded control session.

### v3.1.1+
- key-down/key-up có kiểm soát;
- mở app qua allowlist;
- taskbar/window switching primitives;
- input pacing và human-visible delays;
- regression tests cho từng thao tác.

## v3.2.x — Reliable Screen Observation

- per-monitor DPI awareness;
- capture đúng HWND;
- Windows Graphics Capture/Desktop Duplication fallback;
- multi-monitor;
- overlay bị loại khỏi capture;
- phát hiện cửa sổ bị che;
- chờ UI ổn định;
- frame-before/frame-after comparison.

## v3.3.x — Visual Desktop Agent

- hiểu app/window/state hiện tại;
- phát hiện controls/menus/dialogs;
- bounded action vocabulary;
- confidence + retry;
- multi-frame voting;
- Observe → Decide → Act → Verify loop.

## v3.4.x — Human-like Window Management

- Alt+Tab/taskbar;
- minimize/maximize/restore;
- popup/dialog handling;
- chuyển app;
- quản lý nhiều cửa sổ;
- recovery khi focus bị cướp.

## v3.5.x — Keyboard Operator

- text fields;
- Enter/Esc/Tab/navigation keys;
- Ctrl+A/C/V/X/Z/L;
- modifier hold/release;
- field verification;
- password/OTP/secret protection.

## v3.6.x — Action Verification Engine

- kiểm tra UI có thay đổi sau click;
- kiểm tra text đã nhập;
- kiểm tra cửa sổ/app đúng foreground;
- kiểm tra scroll/drag có tác dụng;
- retry hoặc đổi chiến lược nếu action không thành công.

## v3.7.x — Recovery & Self-Correction

- retry theo state;
- back/undo khi click sai;
- popup handling;
- load-time adaptation;
- vision disagreement handling;
- loop/resource guards.

## v3.8.x — Generic Computer Operator

- goal → plan → visible desktop actions;
- thao tác nhiều app trong một task;
- không hard-code theo một app;
- live progress luôn hiển thị.

## v3.9.x — App Skills

- Browser;
- Explorer;
- Word;
- Excel;
- VS Code / Visual Studio;
- GitHub tooling;
- Riot/League client.

Skills tăng tốc nhưng luôn có Visual Operator fallback.

## v4.0 — Human-like Computer Use

Acceptance:
- nhìn đúng màn hình;
- tự chia task thành bước nhỏ;
- thao tác nhìn thấy được;
- verify sau mỗi bước;
- tự phục hồi lỗi thường gặp;
- progress overlay + audit đầy đủ;
- emergency stop;
- permission theo rủi ro;
- không tự động gameplay League.
