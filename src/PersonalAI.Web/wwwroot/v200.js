(() => {
  const VERSION = "2.1.7";
  let dialog;
  let content;
  let statusText;

  document.addEventListener("DOMContentLoaded", () => {
    updateVersion();
    ensureUi();
  });

  function updateVersion() {
    document.querySelectorAll(".brand span").forEach(node => {
      node.textContent = `Phiên bản ${VERSION}`;
    });
  }

  function ensureUi() {
    const tools = document.querySelector(".sidebar-tools");
    if (!tools || document.getElementById("osButton")) return;

    const button = document.createElement("button");
    button.className = "settings-button";
    button.id = "osButton";
    button.type = "button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "◎";

    const label = document.createElement("span");
    label.textContent = "Personal AI OS";

    const badge = document.createElement("span");
    badge.className = "tool-count";
    badge.id = "osSidebarBadge";
    badge.textContent = "v2.0";

    button.append(icon, label, badge);
    tools.prepend(button);

    dialog = document.createElement("dialog");
    dialog.className = "settings-dialog";
    dialog.id = "osDialog";
    dialog.setAttribute("aria-labelledby", "osTitle");

    const card = document.createElement("div");
    card.className = "settings-card os-dialog os-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "PERSONAL AI OS · v2.0";

    const title = document.createElement("h2");
    title.id = "osTitle";
    title.textContent = "Bảng điều khiển hệ thống";

    statusText = document.createElement("p");
    statusText.className = "os-status-copy";
    statusText.textContent = "Đang đọc trạng thái hệ thống…";

    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.className = "dialog-close";
    close.type = "button";
    close.setAttribute("aria-label", "Đóng");
    close.textContent = "×";

    header.append(heading, close);

    content = document.createElement("div");
    content.className = "os-loading";
    content.textContent = "Đang tải capability manifest…";

    card.append(header, statusText, content);
    dialog.append(card);
    document.body.append(dialog);

    button.addEventListener("click", open);
    close.addEventListener("click", () => dialog.close());
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
  }

  async function open() {
    if (!dialog) return;
    dialog.showModal();
    await refresh();
  }

  async function refresh() {
    content.className = "os-loading";
    content.textContent = "Đang tải capability manifest…";

    try {
      const [statusResponse, manifestResponse] = await Promise.all([
        fetch("/api/os/status", { headers: workspaceHeaders() }),
        fetch("/api/os/manifest", { headers: workspaceHeaders() })
      ]);

      if (!statusResponse.ok || !manifestResponse.ok) {
        throw new Error("Không đọc được trạng thái Personal AI OS.");
      }

      const status = await statusResponse.json();
      const manifest = await manifestResponse.json();
      render(status, manifest);
    } catch (error) {
      content.className = "os-error";
      content.textContent = error instanceof Error
        ? error.message
        : "Không đọc được trạng thái Personal AI OS.";
    }
  }

  function render(status, manifest) {
    statusText.textContent =
      `${status.workspaceName} · ${status.readiness.osContractReady ? "OS contract sẵn sàng" : "OS contract cần kiểm tra"} · bước tiếp theo: ${status.nextStage}`;

    const root = document.createElement("div");

    const summary = document.createElement("section");
    summary.className = "os-summary";
    [
      ["Capability", status.readiness.capabilityCount],
      ["Ready", status.readiness.readyCount],
      ["Controlled", status.readiness.controlledCount],
      ["Foundation", status.readiness.foundationCount]
    ].forEach(([label, value]) => {
      const item = document.createElement("div");
      item.className = "os-summary-item";
      const small = document.createElement("span");
      small.textContent = label;
      const strong = document.createElement("strong");
      strong.textContent = String(value);
      item.append(small, strong);
      summary.append(item);
    });

    const byId = new Map(
      (status.capabilities || []).map(item => [item.id, item])
    );

    const layers = document.createElement("section");
    layers.className = "os-layer-list";

    (status.layers || []).forEach(layer => {
      const block = document.createElement("article");
      block.className = "os-layer";

      const layerHeading = document.createElement("div");
      layerHeading.className = "os-layer-heading";
      const h3 = document.createElement("h3");
      h3.textContent = layer.name;
      const count = document.createElement("span");
      count.textContent = `${layer.capabilityIds.length} capability`;
      layerHeading.append(h3, count);

      const purpose = document.createElement("p");
      purpose.textContent = layer.purpose;

      const list = document.createElement("div");
      list.className = "os-capabilities";

      layer.capabilityIds.forEach(id => {
        const capability = byId.get(id);
        if (!capability) return;

        const row = document.createElement("div");
        row.className = "os-capability";

        const name = document.createElement("div");
        name.className = "os-capability-name";
        name.textContent = capability.name;

        const state = document.createElement("div");
        state.className = "os-capability-state";
        state.textContent = capability.state;

        const detail = document.createElement("div");
        detail.className = "os-capability-detail";
        detail.textContent = capability.detail;

        row.append(name, state, detail);
        list.append(row);
      });

      block.append(layerHeading, purpose, list);
      layers.append(block);
    });

    const boundaries = document.createElement("section");
    boundaries.className = "os-boundaries";
    const boundariesTitle = document.createElement("h3");
    boundariesTitle.textContent = "Ranh giới v2.0";
    const ul = document.createElement("ul");
    (manifest.boundaries || []).forEach(value => {
      const li = document.createElement("li");
      li.textContent = value;
      ul.append(li);
    });
    boundaries.append(boundariesTitle, ul);

    root.append(summary, layers, boundaries);
    content.className = "";
    content.replaceChildren(root);
  }

  function workspaceHeaders() {
    const workspace =
      window.PersonalAiWorkspace?.currentId || "personal";
    return {
      "X-PersonalAI-Workspace": workspace
    };
  }
})();
