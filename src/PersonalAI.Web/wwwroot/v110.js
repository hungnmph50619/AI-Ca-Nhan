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
    eyebrow.textContent = "KHẢ NĂNG ĐIỀU KHIỂN CÓ XÁC NHẬN · WINDOWS";

    const title = document.createElement("h2");
    title.id = "computerUseTitle";
    title.textContent = "Điều khiển máy tính · Bản thử nghiệm";
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
      "Bản thử nghiệm hỗ trợ đọc thông tin máy, chuyển cửa sổ, di chuyển chuột và nhấp trái từng lần có xác nhận. Mỗi lần bật chỉ có 60 giây và tối đa 5 thao tác, kể cả thao tác thất bại. Chưa hỗ trợ gõ phím, chụp ảnh màn hình hay AI tự điều khiển liên tục. Chỉ nhấp trên cửa sổ thử nghiệm không chứa dữ liệu quan trọng.";
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
      "Nhấn Ctrl + Shift + F12 để dừng điều khiển từ bất kỳ cửa sổ thông thường nào của Windows khi phím tắt đã sẵn sàng. Mỗi thao tác vẫn cần quyền và xác nhận riêng. Phím dừng không hoàn tác thao tác đã thực hiện và không hoạt động trên màn hình bảo mật Windows.";

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

    const stop = document.createElement("button");
    stop.type = "button";
    stop.id = "computerControlStop";
    stop.className = "secondary-button";
    stop.textContent = "Dừng điều khiển";
    stop.addEventListener("click", () => changeControl("stop"));

    const enable = document.createElement("button");
    enable.type = "button";
    enable.id = "computerControlEnable";
    enable.className = "secondary-button";
    enable.textContent = "Cho phép điều khiển";
    enable.addEventListener("click", () => {
      if (!window.confirm("Bật tối đa 5 thao tác trong 60 giây? Dừng bằng Ctrl + Shift + F12 hoặc nút Dừng. Mỗi thao tác vẫn cần xác nhận riêng. Chỉ thử trên cửa sổ không chứa dữ liệu quan trọng. Bạn đồng ý không?")) return;
      changeControl("enable");
    });
    actions.append(stop, enable);

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

    updateBadge(payload.supported === true && payload.interactiveSession === true,
      payload.desktopActionsPaused !== false);
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
      updateBadge(payload.supported === true && payload.interactiveSession === true,
        payload.desktopActionsPaused !== false);
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
      `${usable ? (status.desktopActionsPaused === false ? "Đã cho phép điều khiển giới hạn" : "Điều khiển đang tạm dừng") : "Chưa khả dụng"} · ${status.platform || "không rõ nền tảng"} · v${status.version || VERSION}`;
    const stop = document.getElementById("computerControlStop");
    const enable = document.getElementById("computerControlEnable");
    if (stop) stop.disabled = !usable || status.desktopActionsPaused !== false;
    if (enable) enable.disabled =
      !usable || status.desktopActionsPaused !== true || status.stopHotkeyAvailable !== true;
    summary.textContent += status.stopHotkeyAvailable === true
      ? " · Phím dừng Ctrl + Shift + F12 đã sẵn sàng."
      : " · Không đăng ký được phím dừng Ctrl + Shift + F12; không thể bật điều khiển.";
    if (usable && status.desktopActionsPaused === false) {
      const expire = status.desktopSessionExpiresAt
        ? new Date(status.desktopSessionExpiresAt).toLocaleTimeString("vi-VN")
        : "không xác định";
      summary.textContent += ` · Còn ${status.desktopRemainingActions ?? 0} thao tác · Hết hạn ${expire}. Nhấn Kiểm tra lại để cập nhật.`;
    }

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

  function updateBadge(available, paused = true) {
    const badge = document.querySelector("#computerUseBadge");
    if (!badge) return;
    badge.textContent = available ? (paused ? "DỪNG" : "BẬT") : "TẮT";
    badge.className =
      `tool-count v110-computer-badge ${available && !paused ? "is-ready" : "is-off"}`;
  }

  async function changeControl(action) {
    const stop = document.getElementById("computerControlStop");
    const enable = document.getElementById("computerControlEnable");
    if (stop) stop.disabled = true;
    if (enable) enable.disabled = true;
    try {
      const response = await fetch("/api/computer/control/" + action, {
        method: "POST",
        headers: { "X-PersonalAI-Manual-Approval": "dong-y" },
        cache: "no-store"
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không thay đổi được trạng thái điều khiển máy.");
      setFeedback(payload.message || "Đã cập nhật trạng thái điều khiển máy.");
    } catch (error) {
      setFeedback(error.message || "Không thay đổi được trạng thái điều khiển máy.", true);
    } finally {
      await loadComputerStatus();
    }
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
      "active-window": "Cửa sổ đang sử dụng",
      "focus-window": "Chuyển cửa sổ",
      "move-cursor": "Di chuyển con trỏ",
      "click-left": "Nhấp chuột trái một lần"
    }[value] || value || "Chức năng";
  }
})();
