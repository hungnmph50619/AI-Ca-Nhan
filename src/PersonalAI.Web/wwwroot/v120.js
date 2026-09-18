(() => {
  const VERSION = "1.2.0";
  let loading = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureBrowserButton();
    ensureBrowserDialog();
    refreshBrowserBadge().catch(() => {});
  }

  function updateVersion() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;
  }

  function ensureBrowserButton() {
    if (document.querySelector("#browserAgentButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "browserAgentButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "◎";

    const label = document.createElement("span");
    label.textContent = "Trình duyệt";

    const status = document.createElement("span");
    status.id = "browserAgentBadge";
    status.className = "tool-count v120-browser-badge";
    status.textContent = "…";

    button.append(icon, label, status);

    const computer = document.querySelector("#computerUseButton");
    const core = document.querySelector("#coreHealthButton");
    const undo = document.querySelector("#undoButton");
    const reference = computer || core || undo;
    container.insertBefore(button, reference || null);

    button.addEventListener("click", openBrowserDialog);
  }

  function ensureBrowserDialog() {
    let dialog = document.querySelector("#browserAgentDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "browserAgentDialog";
    dialog.className = "settings-dialog v120-browser-dialog";
    dialog.setAttribute("aria-labelledby", "browserAgentTitle");

    const card = document.createElement("div");
    card.className = "settings-card v120-browser-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "CONTROLLED CAPABILITY · HTTP/HTML";

    const title = document.createElement("h2");
    title.id = "browserAgentTitle";
    title.textContent = "Browser Agent v1.2";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Browser Agent");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v120-browser-intro";
    intro.innerHTML =
      "<strong>Điều hướng web có guard</strong>" +
      "<p>Browser Agent v1.2 dùng HTTP GET và snapshot text/links theo workspace. Không chạy JavaScript, không giữ cookie/login, không submit form và không tải tệp.</p>";

    const summary = document.createElement("div");
    summary.id = "browserAgentSummary";
    summary.className = "v120-browser-summary";
    summary.textContent = "Đang kiểm tra…";

    const capabilities = document.createElement("div");
    capabilities.id = "browserAgentCapabilities";
    capabilities.className = "v120-browser-capabilities";

    const limits = document.createElement("div");
    limits.id = "browserAgentLimits";
    limits.className = "v120-browser-limits";

    const limitations = document.createElement("ul");
    limitations.id = "browserAgentLimitations";
    limitations.className = "v120-browser-limitations";

    const safety = document.createElement("div");
    safety.className = "v120-browser-safety";
    safety.textContent =
      "Mọi browser tool dùng quyền TRÌNH DUYỆT và cần xác nhận. Navigation còn cần BÊN NGOÀI. Private/reserved IP, cổng không chuẩn, download và form side effect bị chặn.";

    const feedback = document.createElement("div");
    feedback.id = "browserAgentFeedback";
    feedback.className = "settings-feedback";

    const actions = document.createElement("div");
    actions.className = "v120-browser-actions";
    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Kiểm tra lại";
    refresh.addEventListener("click", () => loadStatus());
    actions.appendChild(refresh);

    card.append(
      header,
      intro,
      summary,
      capabilities,
      limits,
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

  async function openBrowserDialog() {
    const dialog = ensureBrowserDialog();
    dialog.showModal();
    await loadStatus();
  }

  async function refreshBrowserBadge() {
    const response = await fetch("/api/browser/status", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    updateBadge(response.ok && payload.supported === true);
  }

  async function loadStatus() {
    if (loading) return;
    loading = true;
    setFeedback("");

    try {
      const response = await fetch("/api/browser/status", { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không đọc được trạng thái Browser Agent.");
      }

      renderStatus(payload);
      updateBadge(payload.supported === true);
    } catch (error) {
      setFeedback(error.message || "Không đọc được trạng thái Browser Agent.", true);
      updateBadge(false);
    } finally {
      loading = false;
    }
  }

  function renderStatus(status) {
    const summary = document.querySelector("#browserAgentSummary");
    const capabilities = document.querySelector("#browserAgentCapabilities");
    const limits = document.querySelector("#browserAgentLimits");
    const limitations = document.querySelector("#browserAgentLimitations");
    if (!summary || !capabilities || !limits || !limitations) return;

    summary.textContent =
      `${status.supported ? "Sẵn sàng" : "Không khả dụng"} · engine ${status.engine || "không rõ"} · v${status.version || VERSION}`;

    capabilities.replaceChildren();
    const list = Array.isArray(status.availableCapabilities)
      ? status.availableCapabilities
      : [];
    list.forEach(item => {
      const badge = document.createElement("span");
      badge.className = "v120-browser-capability";
      badge.textContent = capabilityLabel(item);
      capabilities.appendChild(badge);
    });

    limits.textContent =
      `Response tối đa ${formatBytes(status.maximumResponseBytes)} · text ${Number(status.maximumPageTextCharacters) || 0} ký tự · ${Number(status.maximumLinks) || 0} link · ${Number(status.maximumRedirects) || 0} redirect.`;

    limitations.replaceChildren();
    (Array.isArray(status.limitations) ? status.limitations : []).forEach(note => {
      const li = document.createElement("li");
      li.textContent = String(note);
      limitations.appendChild(li);
    });
  }

  function updateBadge(available) {
    const badge = document.querySelector("#browserAgentBadge");
    if (!badge) return;
    badge.textContent = available ? "ON" : "OFF";
    badge.className =
      `tool-count v120-browser-badge ${available ? "is-ready" : "is-off"}`;
  }

  function setFeedback(message, isError = false) {
    const node = document.querySelector("#browserAgentFeedback");
    if (!node) return;
    node.textContent = message;
    node.className = isError ? "settings-feedback error" : "settings-feedback";
  }

  function capabilityLabel(value) {
    return {
      "session-info": "Session",
      navigate: "Điều hướng",
      "page-observe": "Đọc trang",
      "link-open": "Mở liên kết"
    }[value] || value || "Capability";
  }

  function formatBytes(value) {
    const bytes = Math.max(0, Number(value) || 0);
    if (bytes < 1024) return `${bytes} B`;
    return `${Math.round(bytes / 1024)} KB`;
  }
})();
