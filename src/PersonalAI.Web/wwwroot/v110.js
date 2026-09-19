(() => {
  const VERSION = "1.1.0";
  let loading = false;
  let pendingKeyboardTimer = null;

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
    // Đặt kích thước ngay trên dialog: CSS cũ có thể vẫn nằm trong bộ nhớ
    // đệm trình duyệt và giữ khung ở 560px trong khi nội dung rộng 820px.
    dialog.style.setProperty("box-sizing", "border-box");
    dialog.style.setProperty("width", "min(820px, calc(100vw - 32px))", "important");
    dialog.style.setProperty("max-width", "calc(100vw - 32px)", "important");
    dialog.style.setProperty("max-height", "calc(100dvh - 24px)");
    dialog.style.setProperty("overflow-x", "hidden");
    dialog.style.setProperty("overflow-y", "auto");
    dialog.setAttribute("aria-labelledby", "computerUseTitle");

    const card = document.createElement("div");
    card.className = "settings-card v110-computer-card";
    card.style.setProperty("box-sizing", "border-box");
    card.style.setProperty("width", "100%", "important");
    card.style.setProperty("max-width", "100%", "important");
    card.style.setProperty("min-width", "0");
    card.style.setProperty("overflow-wrap", "anywhere");

    const header = document.createElement("header");
    header.className = "settings-header";
    header.style.minWidth = "0";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "KHẢ NĂNG ĐIỀU KHIỂN CÓ XÁC NHẬN · WINDOWS";

    const title = document.createElement("h2");
    title.id = "computerUseTitle";
    title.textContent = "Điều khiển máy tính · Bản thử nghiệm";
    heading.style.minWidth = "0";
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
      "Bản thử nghiệm hỗ trợ di chuyển chuột, nhấp trái và nhập một dòng tối đa 32 ký tự vào Notepad. Mỗi lần bật có tối đa 60 giây và 5 thao tác. Chưa hỗ trợ tổ hợp phím, chụp màn hình hay AI tự thao tác liên tục. Chỉ dùng trên dữ liệu thử nghiệm.";
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
    actions.style.flexWrap = "wrap";
    actions.style.justifyContent = "flex-start";
    actions.style.gap = "8px";

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

    const keyboard = document.createElement("section");
    keyboard.id = "computerKeyboardPreview";
    keyboard.className = "v110-computer-intro";
    const keyboardTitle = document.createElement("strong");
    keyboardTitle.textContent = "Nhập văn bản thử nghiệm vào Notepad";
    const keyboardDescription = document.createElement("p");
    keyboardDescription.textContent =
      "Chỉ dùng trên Notepad trống, không nhập mật khẩu hoặc dữ liệu nhạy cảm. Sau khi xác nhận, bạn có 5 giây để chuyển sang đúng cửa sổ Notepad đã chọn. Bản thử nghiệm không ghi nội dung nhập vào nhật ký công cụ.";
    const choose = document.createElement("button");
    choose.type = "button";
    choose.className = "secondary-button";
    choose.textContent = "Tìm cửa sổ Notepad";
    choose.addEventListener("click", loadNotepadWindows);

    const selector = document.createElement("select");
    selector.id = "computerNotepadWindow";
    selector.setAttribute("aria-label", "Cửa sổ Notepad thử nghiệm");
    selector.style.width = "100%";
    selector.style.marginTop = "8px";
    const emptyOption = document.createElement("option");
    emptyOption.value = "";
    emptyOption.textContent = "Nhấn Tìm cửa sổ Notepad";
    selector.append(emptyOption);

    const line = document.createElement("input");
    line.id = "computerNotepadText";
    line.type = "text";
    line.maxLength = 32;
    line.autocomplete = "off";
    line.spellcheck = false;
    line.placeholder = "Một dòng thử nghiệm, tối đa 32 ký tự";
    line.setAttribute("aria-label", "Văn bản thử nghiệm");
    line.style.boxSizing = "border-box";
    line.style.width = "100%";
    line.style.marginTop = "8px";

    const typeButton = document.createElement("button");
    typeButton.type = "button";
    typeButton.className = "secondary-button";
    typeButton.textContent = "Xác nhận nhập sau 5 giây";
    typeButton.addEventListener("click", scheduleNotepadTyping);

    keyboard.append(keyboardTitle, keyboardDescription, choose, selector, line, typeButton);

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
      actions,
      keyboard);
    dialog.appendChild(card);
    dialog.addEventListener("close", () => {
      if (pendingKeyboardTimer !== null) clearTimeout(pendingKeyboardTimer);
      pendingKeyboardTimer = null;
      line.value = "";
      selector.replaceChildren();
    });
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

  async function loadComputerStatus(clearFeedback = true) {
    if (loading) return;
    loading = true;
    if (clearFeedback) setFeedback("");

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

  async function loadNotepadWindows() {
    const select = document.getElementById("computerNotepadWindow");
    if (!select) return;
    select.replaceChildren();
    try {
      const response = await fetch("/api/computer/keyboard/notepad-windows", {
        headers: { "X-PersonalAI-Manual-Approval": "dong-y" },
        cache: "no-store"
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không đọc được danh sách Notepad.");
      const windows = Array.isArray(payload.windows) ? payload.windows : [];
      const blank = document.createElement("option");
      blank.value = "";
      blank.textContent = windows.length ? "Chọn cửa sổ Notepad" : "Chưa tìm thấy cửa sổ Notepad";
      select.append(blank);
      windows.forEach(item => {
        const option = document.createElement("option");
        option.value = item.windowId;
        option.textContent = item.title || "Notepad";
        select.append(option);
      });
      setFeedback(windows.length
        ? "Hãy chọn cửa sổ Notepad thử nghiệm và nhập một dòng văn bản."
        : "Hãy mở Notepad trống rồi nhấn Tìm cửa sổ Notepad.");
    } catch (error) {
      setFeedback(error.message || "Không tìm được cửa sổ Notepad.", true);
    }
  }

  async function scheduleNotepadTyping() {
    if (pendingKeyboardTimer !== null) {
      setFeedback("Đã có lệnh nhập đang chờ. Hãy dừng hoặc chờ lệnh hoàn tất.", true);
      return;
    }
    const select = document.getElementById("computerNotepadWindow");
    const input = document.getElementById("computerNotepadText");
    const windowId = select?.value || "";
    const text = input?.value || "";
    if (!windowId || !text || text.length > 32 || /[\r\n\t]/.test(text)) {
      setFeedback("Hãy chọn cửa sổ Notepad và nhập một dòng 1–32 ký tự.", true);
      return;
    }
    if (!window.confirm(
      "Sau 5 giây, AI Cá Nhân sẽ nhập đúng dòng bạn vừa viết vào cửa sổ Notepad đã chọn. Hãy chuyển sang Notepad trống ngay sau khi đồng ý. Bạn xác nhận không?")) return;
    input.value = "";
    setFeedback("Hãy chuyển sang đúng cửa sổ Notepad trong 5 giây. Ctrl + Shift + F12 để dừng.");
    pendingKeyboardTimer = setTimeout(async () => {
      pendingKeyboardTimer = null;
      try {
        const response = await fetch("/api/computer/keyboard/type-notepad", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-PersonalAI-Manual-Approval": "dong-y"
          },
          body: JSON.stringify({ windowId, text }),
          cache: "no-store"
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(payload.error || "Windows đã từ chối nhập văn bản.");
        setFeedback(payload.detail || "Đã gửi văn bản tới Notepad.");
      } catch (error) {
        setFeedback(error.message || "Không nhập được văn bản.", true);
      } finally {
        await loadComputerStatus(false);
      }
    }, 5000);
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
      "click-left": "Nhấp chuột trái một lần",
      "type-notepad-text": "Nhập một dòng thử nghiệm vào Notepad"
    }[value] || value || "Chức năng";
  }
})();
