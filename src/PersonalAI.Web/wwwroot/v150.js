(() => {
  const VERSION = "1.5.0";
  let loading = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureButton();
    ensureDialog();
    refreshBadge().catch(() => {});
  }

  function updateVersion() {
    const node = document.querySelector(".brand > div:last-child > span");
    if (node) node.textContent = `Phiên bản ${VERSION}`;
  }

  function ensureButton() {
    if (document.querySelector("#androidCompanionButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "androidCompanionButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "▯";

    const label = document.createElement("span");
    label.textContent = "Android";

    const badge = document.createElement("span");
    badge.id = "androidCompanionBadge";
    badge.className = "tool-count v150-companion-badge";
    badge.textContent = "…";

    button.append(icon, label, badge);

    const dev = document.querySelector("#developmentAgentButton");
    const connector = document.querySelector("#connectorButton");
    const browser = document.querySelector("#browserAgentButton");
    const reference = dev || connector || browser;
    container.insertBefore(button, reference || null);

    button.addEventListener("click", openDialog);
  }

  function ensureDialog() {
    let dialog = document.querySelector("#androidCompanionDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "androidCompanionDialog";
    dialog.className = "settings-dialog v150-companion-dialog";
    dialog.setAttribute("aria-labelledby", "androidCompanionTitle");

    const card = document.createElement("div");
    card.className = "settings-card v150-companion-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "ANDROID COMPANION · PAIRED DEVICE";

    const title = document.createElement("h2");
    title.id = "androidCompanionTitle";
    title.textContent = "Android Companion v1.5";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Android Companion");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v150-companion-intro";
    intro.innerHTML =
      "<strong>Pair once, token stays on-device</strong>" +
      "<p>Desktop chỉ tạo mã ghép nối tạm thời. Android nhận device token một lần; backend chỉ lưu SHA-256 hash. Chat từ Android bị khóa tool proposal/execution và task chỉ đọc.</p>";

    const summary = document.createElement("div");
    summary.id = "androidCompanionSummary";
    summary.className = "v150-companion-summary";
    summary.textContent = "Đang kiểm tra…";

    const pairing = document.createElement("div");
    pairing.className = "v150-pairing-box";

    const pairHeader = document.createElement("div");
    pairHeader.className = "v150-pairing-header";
    const pairText = document.createElement("div");
    pairText.innerHTML =
      "<strong>Mã ghép nối</strong><span>Dùng app Android để claim trong 5 phút.</span>";

    const pairButton = document.createElement("button");
    pairButton.id = "androidPairingButton";
    pairButton.type = "button";
    pairButton.className = "secondary-button";
    pairButton.textContent = "Tạo mã";
    pairButton.addEventListener("click", startPairing);

    pairHeader.append(pairText, pairButton);

    const code = document.createElement("div");
    code.id = "androidPairingCode";
    code.className = "v150-pairing-code";
    code.textContent = "—";

    const expiry = document.createElement("div");
    expiry.id = "androidPairingExpiry";
    expiry.className = "v150-pairing-expiry";
    expiry.textContent = "Chưa có mã đang hoạt động.";

    pairing.append(pairHeader, code, expiry);

    const deviceTitle = document.createElement("h3");
    deviceTitle.className = "v150-section-title";
    deviceTitle.textContent = "Thiết bị đã ghép nối";

    const devices = document.createElement("div");
    devices.id = "androidCompanionDevices";
    devices.className = "v150-device-list";

    const empty = document.createElement("div");
    empty.id = "androidCompanionEmpty";
    empty.className = "v150-device-empty";
    empty.textContent = "Chưa có thiết bị Android trong workspace hiện tại.";
    empty.hidden = true;

    const security = document.createElement("div");
    security.className = "v150-companion-security";
    security.innerHTML =
      "<strong>Network boundary</strong>" +
      "<p>Mặc định client API yêu cầu HTTPS. Nếu chủ động bật Companion:AllowInsecureHttp=true để dùng HTTP LAN, token có thể bị nghe lén trên mạng không tin cậy. Pairing/admin endpoint chỉ cho desktop local.</p>";

    const feedback = document.createElement("div");
    feedback.id = "androidCompanionFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    const actions = document.createElement("div");
    actions.className = "v150-actions";
    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Làm mới";
    refresh.addEventListener("click", load);
    actions.appendChild(refresh);

    card.append(
      header,
      intro,
      summary,
      pairing,
      deviceTitle,
      devices,
      empty,
      security,
      feedback,
      actions);
    dialog.appendChild(card);

    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });

    document.body.appendChild(dialog);
    return dialog;
  }

  async function openDialog() {
    const dialog = ensureDialog();
    dialog.showModal();
    await load();
  }

  async function refreshBadge() {
    try {
      const response = await fetch("/api/companion/admin/devices", { cache: "no-store" });
      if (!response.ok) {
        updateBadge("OFF");
        return;
      }
      const payload = await response.json();
      const devices = Array.isArray(payload.devices) ? payload.devices : [];
      updateBadge(String(devices.length));
    } catch {
      updateBadge("OFF");
    }
  }

  async function load() {
    if (loading) return;
    loading = true;
    setFeedback("");

    try {
      const [statusResponse, devicesResponse] = await Promise.all([
        fetch("/api/companion/status", { cache: "no-store" }),
        fetch("/api/companion/admin/devices", { cache: "no-store" })
      ]);

      const status = await statusResponse.json().catch(() => ({}));
      const devices = await devicesResponse.json().catch(() => ({}));

      if (!statusResponse.ok) {
        throw new Error(status.error || "Không đọc được Android Companion status.");
      }
      if (!devicesResponse.ok) {
        throw new Error(
          devices.error
          || "Trang quản lý Android Companion chỉ dùng được từ desktop local.");
      }

      render(status, devices);
      updateBadge(String(Array.isArray(devices.devices) ? devices.devices.length : 0));
    } catch (error) {
      setFeedback(error.message || "Không đọc được Android Companion.", true);
      updateBadge("OFF");
    } finally {
      loading = false;
    }
  }

  function render(status, response) {
    const summary = document.querySelector("#androidCompanionSummary");
    const list = document.querySelector("#androidCompanionDevices");
    const empty = document.querySelector("#androidCompanionEmpty");
    if (!summary || !list || !empty) return;

    const devices = Array.isArray(response.devices) ? response.devices : [];
    summary.textContent =
      `${status.enabled ? "Bật" : "Tắt"} · workspace ${response.workspaceId || "personal"} · ${devices.length}/${response.maximumDevices || status.maximumDevicesPerWorkspace || 10} thiết bị · ${status.secureTransportRequired ? "HTTPS bắt buộc" : "HTTP opt-in đang bật"}`;

    list.replaceChildren();
    devices.forEach(device => list.appendChild(deviceCard(device)));
    empty.hidden = devices.length > 0;

    const pairButton = document.querySelector("#androidPairingButton");
    if (pairButton) pairButton.disabled = status.enabled !== true;
  }

  function deviceCard(device) {
    const article = document.createElement("article");
    article.className = "v150-device";

    const main = document.createElement("div");
    main.className = "v150-device-main";

    const name = document.createElement("strong");
    name.textContent = device.name || "Android device";

    const meta = document.createElement("span");
    meta.textContent =
      `${device.workspaceId || "personal"} · last seen ${formatDate(device.lastSeenAt)}`;

    main.append(name, meta);

    const revoke = document.createElement("button");
    revoke.type = "button";
    revoke.className = "secondary-button";
    revoke.textContent = "Thu hồi";
    revoke.addEventListener("click", () => revokeDevice(device));

    article.append(main, revoke);
    return article;
  }

  async function startPairing() {
    setFeedback("Đang tạo mã ghép nối…");

    try {
      const response = await fetch("/api/companion/admin/pairing/start", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ confirmed: true })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không thể tạo mã ghép nối.");
      }

      const code = document.querySelector("#androidPairingCode");
      const expiry = document.querySelector("#androidPairingExpiry");
      if (code) code.textContent = payload.code || "—";
      if (expiry) {
        expiry.textContent =
          `Workspace: ${payload.workspaceId || "personal"} · hết hạn ${formatDate(payload.expiresAt)}`;
      }

      setFeedback("Mã chỉ dùng một lần. Nhập mã này trên app Android.");
    } catch (error) {
      setFeedback(error.message || "Không thể tạo mã ghép nối.", true);
    }
  }

  async function revokeDevice(device) {
    const confirmFn = window.PersonalAiUi?.confirm;
    if (typeof confirmFn !== "function") {
      setFeedback("Không mở được hộp xác nhận an toàn.", true);
      return;
    }

    const confirmed = await confirmFn(
      `Thu hồi Android device "${device.name || "Android device"}"?\n\nToken trên điện thoại sẽ mất hiệu lực ngay.`,
      {
        title: "Thu hồi Android Companion",
        confirmText: "Thu hồi",
        danger: true
      });

    if (!confirmed) return;

    try {
      const response = await fetch(
        `/api/companion/admin/devices/${encodeURIComponent(device.id)}?confirmed=true`,
        { method: "DELETE" });
      if (!response.ok) {
        const payload = await response.json().catch(() => ({}));
        throw new Error(payload.error || "Không thể thu hồi thiết bị.");
      }

      setFeedback("Đã thu hồi device token.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thể thu hồi thiết bị.", true);
    }
  }

  function updateBadge(value) {
    const badge = document.querySelector("#androidCompanionBadge");
    if (badge) badge.textContent = value;
  }

  function setFeedback(message, isError = false) {
    const node = document.querySelector("#androidCompanionFeedback");
    if (!node) return;
    node.textContent = message;
    node.className = isError
      ? "settings-feedback error"
      : "settings-feedback";
  }

  function formatDate(value) {
    if (!value) return "—";
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? String(value)
      : date.toLocaleString("vi-VN");
  }
})();
