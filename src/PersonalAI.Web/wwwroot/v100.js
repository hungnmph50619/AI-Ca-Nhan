(() => {
  const VERSION = "1.0.0";
  let loading = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureCoreButton();
    ensureCoreDialog();
    refreshCoreBadge().catch(() => {});
  }

  function updateVersion() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) {
      brandVersion.textContent = `Phiên bản ${VERSION}`;
    }
  }

  function ensureCoreButton() {
    if (document.querySelector("#coreHealthButton")) return;

    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "coreHealthButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "◈";

    const label = document.createElement("span");
    label.textContent = "Lõi v1.0";

    const status = document.createElement("span");
    status.id = "coreHealthBadge";
    status.className = "tool-count v100-core-badge";
    status.setAttribute("aria-label", "Trạng thái lõi PersonalAI");
    status.textContent = "…";

    button.append(icon, label, status);

    const undo = document.querySelector("#undoButton");
    const audit = document.querySelector("#auditButton");
    const settings = document.querySelector("#settingsButton");
    const reference = undo || audit || settings;
    container.insertBefore(button, reference || null);

    button.addEventListener("click", openCoreDialog);
  }

  function ensureCoreDialog() {
    let dialog = document.querySelector("#coreHealthDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "coreHealthDialog";
    dialog.className = "settings-dialog v100-core-dialog";
    dialog.setAttribute("aria-labelledby", "coreHealthTitle");

    const card = document.createElement("div");
    card.className = "settings-card v100-core-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "STABLE CORE · API CONTRACT 1";

    const title = document.createElement("h2");
    title.id = "coreHealthTitle";
    title.textContent = "Tình trạng lõi PersonalAI";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng tình trạng lõi");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v100-core-intro";
    intro.innerHTML =
      "<strong>Stable Personal AI Core v1.0.0</strong>" +
      "<p>Kiểm tra readiness của dữ liệu và các nền tảng cục bộ. Kiểm tra này không gửi prompt hay gọi nhà cung cấp AI bên ngoài.</p>";

    const summary = document.createElement("div");
    summary.id = "coreHealthSummary";
    summary.className = "v100-core-summary";
    summary.textContent = "Đang kiểm tra…";

    const list = document.createElement("div");
    list.id = "coreHealthList";
    list.className = "v100-core-list";
    list.setAttribute("role", "list");

    const safety = document.createElement("div");
    safety.id = "coreSafety";
    safety.className = "v100-core-safety";

    const feedback = document.createElement("div");
    feedback.id = "coreHealthFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    const actions = document.createElement("div");
    actions.className = "v100-core-actions";

    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Kiểm tra lại";
    refresh.addEventListener("click", () => loadCoreHealth());
    actions.appendChild(refresh);

    card.append(header, intro, summary, list, safety, feedback, actions);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });

    document.body.appendChild(dialog);
    return dialog;
  }

  async function openCoreDialog() {
    const dialog = ensureCoreDialog();
    dialog.showModal();
    await loadCoreHealth();
  }

  async function refreshCoreBadge() {
    const response = await fetch("/api/system/health", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    updateBadge(payload, response.ok || response.status === 503);
  }

  async function loadCoreHealth() {
    if (loading) return;
    loading = true;
    setFeedback("");

    const summary = document.querySelector("#coreHealthSummary");
    if (summary) summary.textContent = "Đang kiểm tra readiness…";

    try {
      const [healthResponse, capabilitiesResponse] = await Promise.all([
        fetch("/api/system/health", { cache: "no-store" }),
        fetch("/api/system/capabilities", { cache: "no-store" })
      ]);

      const health = await healthResponse.json().catch(() => ({}));
      const capabilities = await capabilitiesResponse.json().catch(() => ({}));

      if (
        (!healthResponse.ok && healthResponse.status !== 503)
        || !capabilitiesResponse.ok
      ) {
        throw new Error(
          health.error
          || capabilities.error
          || "Không thể đọc tình trạng lõi.");
      }

      renderHealth(health);
      renderSafety(capabilities);
      updateBadge(health, true);
    } catch (error) {
      setFeedback(
        error.message || "Không thể đọc tình trạng lõi.",
        true);
      updateBadge({ ready: false, status: "unavailable" }, false);
    } finally {
      loading = false;
    }
  }

  function renderHealth(health) {
    const summary = document.querySelector("#coreHealthSummary");
    const list = document.querySelector("#coreHealthList");
    if (!summary || !list) return;

    const ready = health.ready === true;
    const status = health.status || "unavailable";
    summary.textContent =
      `${ready ? "Lõi sẵn sàng" : "Lõi chưa sẵn sàng"} · ${statusLabel(status)} · workspace ${health.workspaceId || "personal"} · v${health.version || VERSION}`;

    list.replaceChildren();
    const modules = Array.isArray(health.modules) ? health.modules : [];
    modules.forEach(module => {
      const row = document.createElement("article");
      row.className = "v100-core-item";
      row.setAttribute("role", "listitem");

      const top = document.createElement("div");
      top.className = "v100-core-item-top";

      const name = document.createElement("strong");
      name.textContent = moduleLabel(module.module);

      const badge = document.createElement("span");
      badge.className =
        `v100-module-status status-${safeClass(module.status)}`;
      badge.textContent = statusLabel(module.status);

      top.append(name, badge);

      const detail = document.createElement("p");
      detail.textContent = module.detail || "Không có thông tin.";

      if (Number.isFinite(Number(module.itemCount))) {
        const count = document.createElement("small");
        count.textContent = `Số mục kiểm tra: ${Number(module.itemCount)}`;
        row.append(top, detail, count);
      } else {
        row.append(top, detail);
      }

      list.appendChild(row);
    });
  }

  function renderSafety(capabilities) {
    const node = document.querySelector("#coreSafety");
    if (!node) return;

    const safety = capabilities.safety || {};
    const limits = capabilities.limits || {};
    node.replaceChildren();

    const title = document.createElement("strong");
    title.textContent = "Guardrails v1.0";

    const copy = document.createElement("p");
    copy.textContent =
      `Workspace isolation: ${yesNo(safety.workspaceIsolation)} · WRITE confirm: ${yesNo(safety.writeRequiresConfirmation)} · DELETE confirm: ${yesNo(safety.deleteRequiresConfirmation)} · Undo confirm: ${yesNo(safety.undoRequiresConfirmation)} · Computer confirm: ${yesNo(safety.computerControlRequiresConfirmation)} · Browser confirm: ${yesNo(safety.browserUseRequiresConfirmation)} · Connector confirm: ${yesNo(safety.connectorUseRequiresConfirmation)} · Development confirm: ${yesNo(safety.developmentUseRequiresConfirmation)} · Android pairing: ${yesNo(safety.companionPairingRequired)} · Device token hashed: ${yesNo(safety.companionDeviceTokensHashed)} · Remote tool exec: ${yesNo(safety.companionRemoteToolExecutionEnabled)} · Life Context consent: ${yesNo(safety.lifeContextExplicitConsentRequired)} · Life auto collect: ${yesNo(safety.lifeContextAutomaticCollectionEnabled)} · Life encrypted: ${yesNo(safety.lifeContextContentEncrypted)} · Decision user decides: ${yesNo(safety.decisionEngineRequiresUserDecision)} · Decision auto-action: ${yesNo(safety.decisionEngineAutoActionEnabled)} · Decision tool exec: ${yesNo(safety.decisionEngineToolExecutionEnabled)} · Agent loop tự trị: ${yesNo(safety.autonomousAgentLoop)} · Scheduler nền: ${yesNo(safety.backgroundScheduler)}`;

    const limitsCopy = document.createElement("p");
    limitsCopy.textContent =
      `Giới hạn lõi: ${Number(limits.maximumWorkspaces) || 0} workspace · ${Number(limits.maximumTasksPerWorkspace) || 0} task/workspace · context ${Number(limits.maximumContextCharacters) || 0} ký tự · undo ${Number(limits.undoAvailabilityDays) || 0} ngày.`;

    node.append(title, copy, limitsCopy);
  }

  function updateBadge(health, parsed) {
    const badge = document.querySelector("#coreHealthBadge");
    if (!badge) return;

    const ready = parsed && health.ready === true;
    badge.textContent = ready ? "OK" : "!";
    badge.className =
      `tool-count v100-core-badge ${ready ? "is-ready" : "is-warning"}`;
  }

  function setFeedback(message, isError = false) {
    const node = document.querySelector("#coreHealthFeedback");
    if (!node) return;
    node.textContent = message;
    node.className = isError
      ? "settings-feedback error"
      : "settings-feedback";
  }

  function moduleLabel(module) {
    return {
      workspace: "Workspace",
      memory: "Memory",
      documents: "Documents / RAG",
      tasks: "Tasks",
      files: "Files sandbox",
      tools: "Tool Registry",
      audit: "Audit",
      undo: "Undo",
      "computer-use": "Computer Use",
      "browser-agent": "Browser Agent",
      connectors: "Connectors",
      "software-development": "Software Development",
      "android-companion": "Android Companion",
      "life-context": "Life Context",
      "decision-engine": "Decision Engine",
      "ai-provider": "AI Provider"
    }[module] || module || "Module";
  }

  function statusLabel(status) {
    return {
      healthy: "Khỏe",
      degraded: "Giảm chức năng",
      unavailable: "Không khả dụng",
      unconfigured: "Chưa cấu hình"
    }[status] || status || "Không rõ";
  }

  function yesNo(value) {
    return value === true ? "Có" : "Không";
  }

  function safeClass(value) {
    return String(value || "unknown")
      .toLowerCase()
      .replace(/[^a-z0-9-]+/g, "-");
  }
})();
