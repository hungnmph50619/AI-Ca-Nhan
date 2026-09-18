(() => {
  const VERSION = "0.9.7";
  let loading = false;
  let executing = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureUndoButton();
    ensureUndoDialog();
    refreshUndoCount().catch(() => {});
  }

  function updateVersion() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;
  }

  function ensureUndoButton() {
    if (document.querySelector("#undoButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "undoButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "↶";

    const label = document.createElement("span");
    label.textContent = "Hoàn tác";

    const count = document.createElement("span");
    count.id = "undoSidebarCount";
    count.className = "tool-count";
    count.setAttribute(
      "aria-label",
      "Số hành động đang có thể hoàn tác trong không gian hiện tại");
    count.textContent = "0";

    button.append(icon, label, count);

    const audit = document.querySelector("#auditButton");
    const settings = document.querySelector("#settingsButton");
    if (audit?.parentElement === container) {
      container.insertBefore(button, audit);
    } else {
      container.insertBefore(button, settings || null);
    }

    button.addEventListener("click", openUndoDialog);
  }

  function ensureUndoDialog() {
    let dialog = document.querySelector("#undoDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "undoDialog";
    dialog.className = "settings-dialog v097-undo-dialog";
    dialog.setAttribute("aria-labelledby", "undoTitle");

    const card = document.createElement("div");
    card.className = "settings-card v097-undo-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "PRE-STATE · GUARD · CONFIRM · INVERSE";

    const title = document.createElement("h2");
    title.id = "undoTitle";
    title.textContent = "Hoàn tác có kiểm soát";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Hoàn tác");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v097-undo-intro";
    intro.innerHTML =
      "<strong>Undo Foundation v0.9.7</strong>" +
      "<p>PersonalAI chỉ cho hoàn tác khi trạng thái hiện tại vẫn khớp với hành động gốc. Hoàn tác không tự chạy và luôn cần xác nhận.</p>";

    const toolbar = document.createElement("div");
    toolbar.className = "v097-undo-toolbar";

    const summary = document.createElement("span");
    summary.id = "undoSummary";
    summary.textContent = "Đang đọc…";

    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Tải lại";
    refresh.addEventListener("click", () => loadUndo());

    toolbar.append(summary, refresh);

    const feedback = document.createElement("div");
    feedback.id = "undoFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    const list = document.createElement("div");
    list.id = "undoList";
    list.className = "v097-undo-list";
    list.setAttribute("role", "list");

    const empty = document.createElement("div");
    empty.id = "undoEmpty";
    empty.className = "v097-undo-empty";
    empty.hidden = true;
    empty.innerHTML =
      "<strong>Chưa có hành động có thể hoàn tác</strong>" +
      "<p>Các thao tác tệp đủ điều kiện sẽ xuất hiện tại đây sau khi chạy thành công.</p>";

    const note = document.createElement("p");
    note.className = "security-copy";
    note.textContent =
      "v0.9.7 hỗ trợ hoàn tác có guard cho tạo/ghi tệp, xóa tệp nhỏ, tạo/xóa thư mục rỗng và di chuyển tệp. Di chuyển thư mục và các thao tác không có pre-state an toàn chưa được tự động hoàn tác.";

    card.append(header, intro, toolbar, feedback, list, empty, note);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
    document.body.appendChild(dialog);
    return dialog;
  }

  async function openUndoDialog() {
    const dialog = ensureUndoDialog();
    dialog.showModal();
    await loadUndo();
  }

  async function refreshUndoCount() {
    const response = await fetch("/api/undo?limit=100", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) return;

    const available = Array.isArray(payload.items)
      ? payload.items.filter(item => item.status === "available").length
      : 0;
    const count = document.querySelector("#undoSidebarCount");
    if (count) count.textContent = String(available);
  }

  async function loadUndo() {
    if (loading) return;
    loading = true;
    setFeedback("");

    try {
      const response = await fetch("/api/undo?limit=100", {
        cache: "no-store"
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không thể đọc danh sách hoàn tác.");
      }

      renderUndo(payload);
      await refreshUndoCount();
    } catch (error) {
      setFeedback(
        error.message || "Không thể đọc danh sách hoàn tác.",
        true);
    } finally {
      loading = false;
    }
  }

  function renderUndo(payload) {
    const list = document.querySelector("#undoList");
    const empty = document.querySelector("#undoEmpty");
    const summary = document.querySelector("#undoSummary");
    if (!list || !empty || !summary) return;

    const items = Array.isArray(payload.items) ? payload.items : [];
    const available = items.filter(item => item.status === "available").length;
    list.replaceChildren();

    summary.textContent =
      `${available} có thể hoàn tác · hiệu lực ${Number(payload.availabilityDays) || 7} ngày · snapshot tối đa ${formatBytes(Number(payload.maximumSnapshotBytes) || 0)}`;

    items.forEach(item => list.appendChild(createUndoItem(item)));
    empty.hidden = items.length > 0;
  }

  function createUndoItem(item) {
    const article = document.createElement("article");
    article.className = "v097-undo-item";
    article.setAttribute("role", "listitem");

    const top = document.createElement("div");
    top.className = "v097-undo-item-top";

    const title = document.createElement("strong");
    title.textContent = operationLabel(item.operation);

    const status = document.createElement("span");
    status.className = `v097-undo-status status-${safeClass(item.status)}`;
    status.textContent = statusLabel(item.status);
    top.append(title, status);

    const path = document.createElement("p");
    path.className = "v097-undo-path";
    path.textContent = item.secondaryPath
      ? `${item.primaryPath} → ${item.secondaryPath}`
      : item.primaryPath || "—";

    const meta = document.createElement("p");
    meta.className = "v097-undo-meta";
    meta.textContent =
      `${toolLabel(item.toolName)} · hết hạn ${formatTime(item.expiresAt)}`;

    const actions = document.createElement("div");
    actions.className = "v097-undo-actions";

    if (item.status === "available") {
      const undo = document.createElement("button");
      undo.type = "button";
      undo.className = "secondary-button";
      undo.textContent = "Kiểm tra & hoàn tác";
      undo.disabled = executing;
      undo.addEventListener("click", () => executeUndo(item));
      actions.appendChild(undo);
    }

    article.append(top, path, meta, actions);
    return article;
  }

  async function executeUndo(item) {
    if (executing) return;
    executing = true;
    setFeedback("Đang kiểm tra trạng thái hiện tại…");

    try {
      const assessmentResponse = await fetch(
        `/api/undo/${encodeURIComponent(item.undoId)}/assessment`,
        { cache: "no-store" });
      const assessment = await assessmentResponse.json().catch(() => ({}));

      if (!assessmentResponse.ok) {
        throw new Error(
          assessment.error || "Không kiểm tra được điều kiện hoàn tác.");
      }

      if (!assessment.canUndo) {
        setFeedback(
          assessment.reason || "Hành động này hiện không thể hoàn tác an toàn.",
          true);
        await loadUndo();
        return;
      }

      const ask = window.PersonalAiUi?.confirm;
      if (typeof ask !== "function") {
        throw new Error("Không mở được hộp xác nhận an toàn.");
      }

      const confirmed = await ask(
        `${operationLabel(item.operation)}?\n\nĐối tượng: ${item.primaryPath}${item.secondaryPath ? `\nVị trí liên quan: ${item.secondaryPath}` : ""}\n\nKiểm tra an toàn: ${assessment.reason}\n\nPersonalAI sẽ kiểm tra lại điều kiện ngay trước khi thay đổi tệp.`,
        {
          title: "Xác nhận hoàn tác",
          confirmText: "Xác nhận hoàn tác",
          danger: item.operation === "delete-created-file"
            || item.operation === "delete-created-directory"
        });

      if (!confirmed) {
        setFeedback("Đã hủy yêu cầu hoàn tác.");
        return;
      }

      setFeedback("Đang hoàn tác…");
      const response = await fetch(
        `/api/undo/${encodeURIComponent(item.undoId)}/execute`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ confirmed: true })
        });
      const payload = await response.json().catch(() => ({}));

      if (!response.ok) {
        throw new Error(payload.error || "Không thể hoàn tác hành động.");
      }

      setFeedback(payload.message || "Đã hoàn tác.");
      await loadUndo();
      document.dispatchEvent(new CustomEvent("personalai:undo-completed"));
    } catch (error) {
      setFeedback(
        error.message || "Không thể hoàn tác hành động.",
        true);
      await loadUndo();
    } finally {
      executing = false;
    }
  }

  function setFeedback(message, isError = false) {
    const node = document.querySelector("#undoFeedback");
    if (!node) return;
    node.textContent = message;
    node.className = isError
      ? "settings-feedback error"
      : "settings-feedback";
  }

  function operationLabel(operation) {
    return {
      "delete-created-file": "Xóa tệp vừa tạo",
      "restore-file": "Khôi phục nội dung tệp",
      "restore-deleted-file": "Khôi phục tệp đã xóa",
      "delete-created-directory": "Xóa thư mục vừa tạo",
      "restore-empty-directory": "Khôi phục thư mục rỗng",
      "move-file-back": "Di chuyển tệp về vị trí cũ"
    }[operation] || operation || "Hoàn tác";
  }

  function statusLabel(status) {
    return {
      available: "Có thể hoàn tác",
      pending: "Đang chuẩn bị",
      undone: "Đã hoàn tác",
      expired: "Hết hạn",
      abandoned: "Không khả dụng"
    }[status] || status || "Không rõ";
  }

  function toolLabel(toolName) {
    return {
      "workspace.write_text": "Ghi tệp",
      "workspace.create_directory": "Tạo thư mục",
      "workspace.move": "Di chuyển",
      "workspace.delete": "Xóa"
    }[toolName] || toolName || "Công cụ";
  }

  function formatTime(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? "không rõ"
      : new Intl.DateTimeFormat("vi-VN", {
          dateStyle: "short",
          timeStyle: "short"
        }).format(date);
  }

  function formatBytes(bytes) {
    const value = Math.max(0, Number(bytes) || 0);
    if (value < 1024) return `${value} B`;
    return `${Math.round(value / 1024)} KB`;
  }

  function safeClass(value) {
    return String(value || "unknown")
      .toLowerCase()
      .replace(/[^a-z0-9-]+/g, "-");
  }
})();
