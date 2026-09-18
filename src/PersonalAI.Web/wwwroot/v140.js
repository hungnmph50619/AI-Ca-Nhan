(() => {
  const VERSION = "1.4.0";
  let loading = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureDevelopmentButton();
    ensureDevelopmentDialog();
    refreshDevelopmentBadge().catch(() => {});
  }

  function updateVersion() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) {
      brandVersion.textContent = `Phiên bản ${VERSION}`;
    }
  }

  function ensureDevelopmentButton() {
    if (document.querySelector("#developmentAgentButton")) return;

    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "developmentAgentButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "</>";

    const label = document.createElement("span");
    label.textContent = "Phát triển";

    const status = document.createElement("span");
    status.id = "developmentAgentBadge";
    status.className = "tool-count v140-development-badge";
    status.textContent = "…";
    status.setAttribute("aria-label", "Trạng thái Software Development Agent");

    button.append(icon, label, status);

    const connector = document.querySelector("#connectorButton");
    const browser = document.querySelector("#browserAgentButton");
    const computer = document.querySelector("#computerUseButton");
    const core = document.querySelector("#coreHealthButton");
    const reference = connector || browser || computer || core;
    container.insertBefore(button, reference || null);

    button.addEventListener("click", openDevelopmentDialog);
  }

  function ensureDevelopmentDialog() {
    let dialog = document.querySelector("#developmentAgentDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "developmentAgentDialog";
    dialog.className = "settings-dialog v140-development-dialog";
    dialog.setAttribute("aria-labelledby", "developmentAgentTitle");

    const card = document.createElement("div");
    card.className = "settings-card v140-development-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "CONTROLLED CAPABILITY · NO ARBITRARY SHELL";

    const title = document.createElement("h2");
    title.id = "developmentAgentTitle";
    title.textContent = "Software Development Agent v1.4";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Software Development Agent");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v140-development-intro";
    intro.innerHTML =
      "<strong>Source-aware, process-limited</strong>" +
      "<p>v1.4.0 có thể inspect/search source, đọc Git status/diff và chạy dotnet restore/build/test đã đóng khung. Không có shell tùy ý, arbitrary args hay Git write actions.</p>";

    const summary = document.createElement("div");
    summary.id = "developmentAgentSummary";
    summary.className = "v140-development-summary";
    summary.textContent = "Đang kiểm tra…";

    const runtime = document.createElement("div");
    runtime.id = "developmentAgentRuntime";
    runtime.className = "v140-development-runtime";

    const capabilities = document.createElement("div");
    capabilities.id = "developmentAgentCapabilities";
    capabilities.className = "v140-development-capabilities";

    const limits = document.createElement("div");
    limits.id = "developmentAgentLimits";
    limits.className = "v140-development-limits";

    const limitationTitle = document.createElement("h3");
    limitationTitle.className = "v140-section-title";
    limitationTitle.textContent = "Guardrails";

    const limitations = document.createElement("ul");
    limitations.id = "developmentAgentLimitations";
    limitations.className = "v140-development-limitations";

    const safety = document.createElement("div");
    safety.className = "v140-development-safety";
    safety.textContent =
      "Development tools dùng quyền PHÁT TRIỂN PHẦN MỀM + NHẠY CẢM và luôn cần xác nhận. dotnet restore còn cần BÊN NGOÀI; build/test có thể chạy code do project định nghĩa nhưng không nhận command string tùy ý.";

    const feedback = document.createElement("div");
    feedback.id = "developmentAgentFeedback";
    feedback.className = "settings-feedback";

    const actions = document.createElement("div");
    actions.className = "v140-development-actions";

    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Kiểm tra lại";
    refresh.addEventListener("click", () => loadDevelopmentStatus());
    actions.appendChild(refresh);

    card.append(
      header,
      intro,
      summary,
      runtime,
      capabilities,
      limits,
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

  async function openDevelopmentDialog() {
    const dialog = ensureDevelopmentDialog();
    dialog.showModal();
    await loadDevelopmentStatus();
  }

  async function refreshDevelopmentBadge() {
    const response = await fetch("/api/development/status", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    updateBadge(
      response.ok
      && (payload.gitAvailable === true || payload.dotnetAvailable === true));
  }

  async function loadDevelopmentStatus() {
    if (loading) return;
    loading = true;
    setFeedback("");

    try {
      const response = await fetch("/api/development/status", { cache: "no-store" });
      const status = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(
          status.error || "Không đọc được trạng thái Development Agent.");
      }

      renderStatus(status);
      updateBadge(status.gitAvailable === true || status.dotnetAvailable === true);
    } catch (error) {
      setFeedback(
        error.message || "Không đọc được trạng thái Development Agent.",
        true);
      updateBadge(false);
    } finally {
      loading = false;
    }
  }

  function renderStatus(status) {
    const summary = document.querySelector("#developmentAgentSummary");
    const runtime = document.querySelector("#developmentAgentRuntime");
    const capabilities = document.querySelector("#developmentAgentCapabilities");
    const limits = document.querySelector("#developmentAgentLimits");
    const limitations = document.querySelector("#developmentAgentLimitations");
    if (!summary || !runtime || !capabilities || !limits || !limitations) return;

    summary.textContent =
      `Workspace ${status.workspaceId || "personal"} · v${status.version || VERSION}`;

    runtime.replaceChildren(
      runtimeBadge("Git", status.gitAvailable === true),
      runtimeBadge(".NET", status.dotnetAvailable === true),
      runtimeBadge("Shell tùy ý", status.arbitraryShellEnabled === true),
      runtimeBadge("Git write", status.gitWriteActionsEnabled === true));

    capabilities.replaceChildren();
    (Array.isArray(status.availableCapabilities)
      ? status.availableCapabilities
      : []).forEach(item => {
        const badge = document.createElement("span");
        badge.className = "v140-development-capability";
        badge.textContent = capabilityLabel(item);
        capabilities.appendChild(badge);
      });

    limits.textContent =
      `Quét tối đa ${Number(status.maximumScannedFiles) || 0} file · ${Number(status.maximumSearchHits) || 0} search hit · process output tối đa ${Number(status.maximumProcessOutputCharacters) || 0} ký tự.`;

    limitations.replaceChildren();
    (Array.isArray(status.limitations) ? status.limitations : []).forEach(note => {
      const li = document.createElement("li");
      li.textContent = String(note);
      limitations.appendChild(li);
    });
  }

  function runtimeBadge(label, enabled) {
    const node = document.createElement("span");
    node.className =
      `v140-runtime-badge ${enabled ? "is-on" : "is-off"}`;
    node.textContent = `${label}: ${enabled ? "Có" : "Không"}`;
    return node;
  }

  function updateBadge(available) {
    const badge = document.querySelector("#developmentAgentBadge");
    if (!badge) return;
    badge.textContent = available ? "ON" : "OFF";
    badge.className =
      `tool-count v140-development-badge ${available ? "is-ready" : "is-off"}`;
  }

  function setFeedback(message, isError = false) {
    const node = document.querySelector("#developmentAgentFeedback");
    if (!node) return;
    node.textContent = message;
    node.className = isError
      ? "settings-feedback error"
      : "settings-feedback";
  }

  function capabilityLabel(value) {
    return {
      "workspace-inspect": "Inspect source",
      "text-search": "Tìm source",
      "git-status": "Git status",
      "git-diff": "Git diff",
      "dotnet-restore": "dotnet restore",
      "dotnet-build": "dotnet build",
      "dotnet-test": "dotnet test"
    }[value] || value || "Capability";
  }
})();
