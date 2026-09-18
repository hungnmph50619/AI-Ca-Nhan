(() => {
  const VERSION = "0.9.6";
  let loading = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureAuditButton();
    ensureAuditDialog();
    refreshAuditCount().catch(() => {});
  }

  function updateVersion() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;
  }

  function ensureAuditButton() {
    if (document.querySelector("#auditButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "auditButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "◎";

    const label = document.createElement("span");
    label.textContent = "Nhật ký";

    const count = document.createElement("span");
    count.id = "auditSidebarCount";
    count.className = "tool-count";
    count.setAttribute("aria-label", "Số sự kiện audit trong không gian hiện tại");
    count.textContent = "0";

    button.append(icon, label, count);

    const settings = document.querySelector("#settingsButton");
    container.insertBefore(button, settings || null);
    button.addEventListener("click", openAuditDialog);
  }

  function ensureAuditDialog() {
    let dialog = document.querySelector("#auditDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "auditDialog";
    dialog.className = "settings-dialog v096-audit-dialog";
    dialog.setAttribute("aria-labelledby", "auditTitle");

    const card = document.createElement("div");
    card.className = "settings-card v096-audit-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "TIME · AGENT · ACTION · TOOL · TARGET · REASON · RESULT";

    const title = document.createElement("h2");
    title.id = "auditTitle";
    title.textContent = "Nhật ký hệ thống";

    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Nhật ký hệ thống");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v096-audit-intro";
    intro.innerHTML = "<strong>Audit Foundation v0.9.6</strong><p>Nhật ký này ghi metadata vận hành của workspace hiện tại. Không lưu nguyên văn prompt, nội dung memory, nội dung tài liệu hay tham số tool đầy đủ.</p>";

    const controls = document.createElement("div");
    controls.className = "v096-audit-controls";

    const agent = createSelect("auditAgentFilter", "Tất cả tác nhân", [
      ["user", "Người dùng"],
      ["personal-ai", "PersonalAI"],
      ["task-engine", "Task Engine"],
      ["system", "Hệ thống"]
    ]);
    const result = createSelect("auditResultFilter", "Tất cả kết quả", [
      ["succeeded", "Thành công"],
      ["prepared", "Đã chuẩn bị"],
      ["proposed", "Đã đề xuất"],
      ["denied", "Bị chặn"],
      ["cancelled", "Đã hủy"],
      ["failed", "Thất bại"]
    ]);

    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Tải lại";
    refresh.addEventListener("click", () => loadAudit());

    agent.addEventListener("change", () => loadAudit());
    result.addEventListener("change", () => loadAudit());
    controls.append(agent, result, refresh);

    const summary = document.createElement("div");
    summary.id = "auditSummary";
    summary.className = "v096-audit-summary";
    summary.textContent = "Đang đọc nhật ký…";

    const list = document.createElement("div");
    list.id = "auditList";
    list.className = "v096-audit-list";
    list.setAttribute("role", "list");

    const empty = document.createElement("div");
    empty.id = "auditEmpty";
    empty.className = "v096-audit-empty";
    empty.hidden = true;
    empty.innerHTML = "<strong>Chưa có sự kiện audit</strong><p>Các hành động mới trong workspace này sẽ xuất hiện ở đây.</p>";

    const note = document.createElement("p");
    note.className = "security-copy";
    note.textContent = "Audit v0.9.6 là append-only qua API: giao diện chỉ đọc, không có endpoint sửa hoặc xóa. Hệ thống giữ tối đa 10.000 sự kiện trong 90 ngày.";

    card.append(header, intro, controls, summary, list, empty, note);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
    document.body.appendChild(dialog);
    return dialog;
  }

  function createSelect(id, placeholder, options) {
    const select = document.createElement("select");
    select.id = id;
    select.setAttribute("aria-label", placeholder);

    const all = document.createElement("option");
    all.value = "";
    all.textContent = placeholder;
    select.appendChild(all);

    options.forEach(([value, label]) => {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = label;
      select.appendChild(option);
    });

    return select;
  }

  async function openAuditDialog() {
    const dialog = ensureAuditDialog();
    dialog.showModal();
    await loadAudit();
  }

  async function refreshAuditCount() {
    const response = await fetch("/api/audit/summary");
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) return;
    const count = document.querySelector("#auditSidebarCount");
    if (count) count.textContent = String(Number(payload.totalEvents) || 0);
  }

  async function loadAudit() {
    if (loading) return;
    loading = true;

    const summary = document.querySelector("#auditSummary");
    const list = document.querySelector("#auditList");
    const empty = document.querySelector("#auditEmpty");
    if (summary) summary.textContent = "Đang đọc nhật ký…";

    try {
      const agent = document.querySelector("#auditAgentFilter")?.value || "";
      const result = document.querySelector("#auditResultFilter")?.value || "";
      const params = new URLSearchParams({ limit: "100" });
      if (agent) params.set("agent", agent);
      if (result) params.set("result", result);

      const [eventsResponse, summaryResponse] = await Promise.all([
        fetch(`/api/audit?${params.toString()}`),
        fetch("/api/audit/summary")
      ]);

      const events = await eventsResponse.json().catch(() => ({}));
      const summaryPayload = await summaryResponse.json().catch(() => ({}));

      if (!eventsResponse.ok) {
        throw new Error(events.error || "Không thể đọc nhật ký audit.");
      }
      if (!summaryResponse.ok) {
        throw new Error(summaryPayload.error || "Không thể đọc tổng hợp audit.");
      }

      renderAudit(events, summaryPayload);
      await refreshAuditCount();
    } catch (error) {
      if (summary) {
        summary.textContent = error.message || "Không thể đọc nhật ký audit.";
      }
      if (list) list.replaceChildren();
      if (empty) empty.hidden = true;
    } finally {
      loading = false;
    }
  }

  function renderAudit(payload, summaryPayload) {
    const list = document.querySelector("#auditList");
    const empty = document.querySelector("#auditEmpty");
    const summary = document.querySelector("#auditSummary");
    if (!list || !empty || !summary) return;

    const items = Array.isArray(payload.items) ? payload.items : [];
    list.replaceChildren();

    summary.textContent =
      `${Number(summaryPayload.totalEvents) || 0} sự kiện · lưu ${Number(payload.retentionDays) || 90} ngày · tối đa ${Number(payload.maximumEntries) || 10000} mục`;

    items.forEach(item => list.appendChild(createAuditItem(item)));
    empty.hidden = items.length > 0;
  }

  function createAuditItem(item) {
    const article = document.createElement("article");
    article.className = "v096-audit-item";
    article.setAttribute("role", "listitem");

    const top = document.createElement("div");
    top.className = "v096-audit-item-top";

    const action = document.createElement("strong");
    action.textContent = actionLabel(item.action);

    const status = document.createElement("span");
    status.className = `v096-audit-result result-${safeClass(item.result)}`;
    status.textContent = resultLabel(item.result);
    top.append(action, status);

    const grid = document.createElement("dl");
    grid.className = "v096-audit-grid";
    addField(grid, "Thời gian", formatTime(item.time));
    addField(grid, "Tác nhân", agentLabel(item.agent));
    addField(grid, "Hành động", item.action || "—");
    addField(grid, "Công cụ", item.tool || "—");
    addField(grid, "Đối tượng", item.target || "—");
    addField(grid, "Lý do", reasonLabel(item.reason));
    addField(grid, "Kết quả", resultLabel(item.result));

    article.append(top, grid);
    return article;
  }

  function addField(grid, term, value) {
    const dt = document.createElement("dt");
    dt.textContent = term;
    const dd = document.createElement("dd");
    dd.textContent = value;
    grid.append(dt, dd);
  }

  function actionLabel(action) {
    const labels = {
      "workspace.create": "Tạo không gian",
      "workspace.update": "Cập nhật không gian",
      "memory.create": "Tạo trí nhớ",
      "memory.update": "Cập nhật trí nhớ",
      "memory.enable": "Bật trí nhớ",
      "memory.disable": "Tắt trí nhớ",
      "memory.delete": "Xóa trí nhớ",
      "memory.delete-all": "Xóa toàn bộ trí nhớ",
      "memory.import": "Nhập trí nhớ",
      "document.upload": "Tải tài liệu",
      "document.rename": "Đổi tên tài liệu",
      "document.reindex": "Lập chỉ mục lại",
      "document.delete": "Xóa tài liệu",
      "document.bulk-delete": "Xóa nhiều tài liệu",
      "context.preview": "Xem trước ngữ cảnh",
      "chat.completed": "Hoàn tất trả lời",
      "tool.proposed": "AI đề xuất công cụ",
      "tool.proposal.prepare": "Chuẩn bị đề xuất công cụ",
      "tool.execute": "Chạy công cụ",
      "tool.synthesize": "AI diễn giải kết quả",
      "tool.native-continue": "AI tiếp tục sau công cụ",
      "task.prepare": "Chuẩn bị tác vụ",
      "task.plan": "AI lập kế hoạch tác vụ",
      "task.step.execute": "Chạy bước tác vụ",
      "task.retry.prepare": "Chuẩn bị thử lại",
      "task.resume": "Tiếp tục tác vụ",
      "task.cancel": "Hủy tác vụ"
    };
    return labels[action] || action || "Sự kiện";
  }

  function agentLabel(agent) {
    return {
      user: "Người dùng",
      "personal-ai": "PersonalAI",
      "task-engine": "Task Engine",
      system: "Hệ thống"
    }[agent] || agent || "—";
  }

  function reasonLabel(reason) {
    return {
      "user-request": "Yêu cầu của người dùng",
      "structured-plan": "Kế hoạch có cấu trúc",
      "user-goal": "Mục tiêu của người dùng",
      "user-requested-step": "Người dùng yêu cầu chạy bước",
      "user-confirmed-step": "Người dùng xác nhận chạy bước",
      "confirmation-required": "Cần xác nhận",
      "safe-retry": "Thử lại được đánh giá an toàn",
      "user-reviewed-retry": "Người dùng đã kiểm tra thử lại",
      "retry-policy-blocked": "Chính sách thử lại chặn",
      "direct-request": "Yêu cầu chạy trực tiếp",
      "direct-request-confirmed": "Yêu cầu trực tiếp đã xác nhận",
      "manual-proposal": "Đề xuất thủ công",
      "approved-proposal": "Chạy đề xuất đã phê duyệt",
      "approved-proposal-confirmed": "Đề xuất đã xác nhận",
      "chat-tool-selection": "AI chọn công cụ cho lượt chat",
      "context-managed-chat": "Lượt chat dùng Context Manager",
      "documents-only-no-source": "Không có nguồn phù hợp trong chế độ chỉ tài liệu",
      "external-confirmation-required": "Cần xác nhận gửi dữ liệu ra ngoài",
      "user-confirmed-external": "Người dùng xác nhận gửi ra ngoài"
    }[reason] || reason || "—";
  }

  function resultLabel(result) {
    return {
      succeeded: "Thành công",
      prepared: "Đã chuẩn bị",
      proposed: "Đã đề xuất",
      denied: "Bị chặn",
      cancelled: "Đã hủy",
      failed: "Thất bại",
      "invalid-input": "Dữ liệu không hợp lệ",
      "not-found": "Không tìm thấy",
      "timed-out": "Quá thời gian"
    }[result] || result || "—";
  }

  function formatTime(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? "Không rõ"
      : new Intl.DateTimeFormat("vi-VN", {
          dateStyle: "short",
          timeStyle: "medium"
        }).format(date);
  }

  function safeClass(value) {
    return String(value || "unknown")
      .toLowerCase()
      .replace(/[^a-z0-9-]+/g, "-");
  }
})();
