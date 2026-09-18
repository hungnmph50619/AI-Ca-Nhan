(() => {
  const VERSION = "1.1.0";
  let loading = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureComputerButton();
    ensureComputerDialog();
    refreshComputerBadge().catch(() => {});
  }

  function updateVersion() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) {
      brandVersion.textContent = `Phiên bản ${VERSION}`;
    }
  }

  function ensureComputerButton() {
    if (document.querySelector("#computerUseButton")) return;

    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "computerUseButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "▣";

    const label = document.createElement("span");
    label.textContent = "Máy tính";

    const status = document.createElement("span");
    status.id = "computerUseBadge";
    status.className = "tool-count v110-computer-badge";
    status.setAttribute("aria-label", "Trạng thái Computer Use");
    status.textContent = "…";

    button.append(icon, label, status);

    const core = document.querySelector("#coreHealthButton");
    const undo = document.querySelector("#undoButton");
    const audit = document.querySelector("#auditButton");
    const settings = document.querySelector("#settingsButton");
    const reference = core || undo || audit || settings;
    container.insertBefore(button, reference || null);

    button.addEventListener("click", openComputerDialog);
  }

  function ensureComputerDialog() {
    let dialog = document.querySelector("#computerUseDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "computerUseDialog";
    dialog.className = "settings-dialog v110-computer-dialog";
    dialog.setAttribute("aria-labelledby", "computerUseTitle");

    const card = document.createElement("div");
    card.className = "settings-card v110-computer-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "CONTROLLED CAPABILITY · WINDOWS";

    const title = document.createElement("h2");
    title.id = "computerUseTitle";
    title.textContent = "Computer Use v1.1";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Computer Use");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v110-computer-intro";
    const introTitle = document.createElement("strong");
    introTitle.textContent = "Quan sát trước, hành động có xác nhận";
    const introCopy = document.createElement("p");
    introCopy.textContent =
      "v1.1.0 chỉ thêm quan sát desktop và hai thao tác điều khiển giới hạn: chuyển focus cửa sổ và di chuyển con trỏ. Không click, không gõ phím, không chụp màn hình và không chạy shell.";
    intro.append(introTitle, introCopy);

    const summary = document.createElement("div");
    summary.id = "computerUseSummary";
    summary.className = "v110-computer-summary";
    summary.textContent = "Đang kiểm tra…";

    const capabilityTitle = document.createElement("h3");
    capabilityTitle.className = "v110-section-title";
    capabilityTitle.textContent = "Khả năng";

    const capabilities = document.createElement("div");
    capabilities.id = "computerUseCapabilities";
    capabilities.className = "v110-capability-list";

    const limitationTitle = document.createElement("h3");
    limitationTitle.className = "v110-section-title";
    limitationTitle.textContent = "Giới hạn";

    const limitations = document.createElement("ul");
    limitations.id = "computerUseLimitations";
    limitations.className = "v110-limitations";

    const safety = document.createElement("div");
    safety.className = "v110-computer-safety";
    safety.textContent =
      "Window title được coi là dữ liệu nhạy cảm. Đọc danh sách/cửa sổ active cần quyền NHẠY CẢM và xác nhận. Focus/cursor move cần quyền ĐIỀU KHIỂN MÁY và xác nhận ở Tool Framework.";

    const feedback = document.createElement("div");
    feedback.id = "computerUseFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    const actions = document.createElement("div");
    actions.className = "v110-computer-actions";

    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Kiểm tra lại";
    refresh.addEventListener("click", () => loadComputerStatus());
    actions.appendChild(refresh);

    card.append(
      header,
      intro,
      summary,
      capabilityTitle,
      capabilities,
      limitationTitle,
      limitations,
      safety,
      feedback,
      actions);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });

    document.body.appendChild(dialog);
    return dialog;
  }

  async function openComputerDialog() {
    const dialog = ensureComputerDialog();
    dialog.showModal();
    await loadComputerStatus();
  }

  async function refreshComputerBadge() {
    const response = await fetch("/api/computer/status", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      updateBadge(false);
      return;
    }

    updateBadge(payload.supported === true && payload.interactiveSession === true);
  }

  async function loadComputerStatus() {
    if (loading) return;
    loading = true;
    setFeedback("");

    try {
      const response = await fetch("/api/computer/status", { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không đọc được trạng thái Computer Use.");
      }

      renderStatus(payload);
      updateBadge(payload.supported === true && payload.interactiveSession === true);
    } catch (error) {
      setFeedback(error.message || "Không đọc được trạng thái Computer Use.", true);
      updateBadge(false);
    } finally {
      loading = false;
    }
  }

  function renderStatus(status) {
    const summary = document.querySelector("#computerUseSummary");
    const capabilities = document.querySelector("#computerUseCapabilities");
    const limitations = document.querySelector("#computerUseLimitations");
    if (!summary || !capabilities || !limitations) return;

    const usable = status.supported === true && status.interactiveSession === true;
    summary.textContent =
      `${usable ? "Sẵn sàng" : "Chưa khả dụng"} · ${status.platform || "không rõ nền tảng"} · v${status.version || VERSION}`;

    capabilities.replaceChildren();
    const items = Array.isArray(status.availableCapabilities)
      ? status.availableCapabilities
      : [];

    if (items.length === 0) {
      const empty = document.createElement("span");
      empty.className = "v110-capability-empty";
      empty.textContent = "Không có capability desktop đang hoạt động trong phiên này.";
      capabilities.appendChild(empty);
    } else {
      items.forEach(item => {
        const badge = document.createElement("span");
        badge.className = "v110-capability";
        badge.textContent = capabilityLabel(item);
        capabilities.appendChild(badge);
      });
    }

    limitations.replaceChildren();
    const notes = Array.isArray(status.limitations) ? status.limitations : [];
    notes.forEach(note => {
      const li = document.createElement("li");
      li.textContent = String(note);
      limitations.appendChild(li);
    });
  }

  function updateBadge(available) {
    const badge = document.querySelector("#computerUseBadge");
    if (!badge) return;
    badge.textContent = available ? "ON" : "OFF";
    badge.className =
      `tool-count v110-computer-badge ${available ? "is-ready" : "is-off"}`;
  }

  function setFeedback(message, isError = false) {
    const node = document.querySelector("#computerUseFeedback");
    if (!node) return;
    node.textContent = message;
    node.className = isError
      ? "settings-feedback error"
      : "settings-feedback";
  }

  function capabilityLabel(value) {
    return {
      "screen-info": "Thông tin màn hình",
      "cursor-position": "Đọc vị trí con trỏ",
      "window-list": "Danh sách cửa sổ",
      "active-window": "Cửa sổ active",
      "focus-window": "Chuyển focus",
      "move-cursor": "Di chuyển con trỏ"
    }[value] || value || "Capability";
  }
})();
