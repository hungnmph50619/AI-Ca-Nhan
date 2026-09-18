(() => {
  const VERSION = "0.8.8";
  let loading = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersionLabels();
    ensureToolsButton();
    ensureToolsDialog();
    refreshCount().catch(() => {});
  }

  function updateVersionLabels() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;
  }

  function ensureToolsButton() {
    if (document.querySelector("#toolsButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "toolsButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "⌘";
    const label = document.createElement("span");
    label.textContent = "Công cụ";
    const count = document.createElement("span");
    count.id = "toolsSidebarCount";
    count.className = "tool-count";
    count.setAttribute("aria-label", "Số công cụ đã đăng ký");
    count.textContent = "0";
    button.append(icon, label, count);

    const settings = document.querySelector("#settingsButton");
    container.insertBefore(button, settings || null);
    button.addEventListener("click", openToolsDialog);
  }

  function ensureToolsDialog() {
    let dialog = document.querySelector("#toolsDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "toolsDialog";
    dialog.className = "settings-dialog v080-tool-dialog";
    dialog.setAttribute("aria-labelledby", "toolsTitle");

    const card = document.createElement("div");
    card.className = "settings-card v080-tool-card";

    const header = document.createElement("header");
    header.className = "settings-header";
    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "INTERNAL TOOLS TRÊN MÁY NÀY";
    const title = document.createElement("h2");
    title.id = "toolsTitle";
    title.textContent = "Công cụ";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Công cụ");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v080-framework-intro";
    intro.innerHTML = "<strong>Native Tool Result Round-trip v0.8.8</strong><p>Sau khi một native function proposal được người dùng cho phép chạy, PersonalAI có thể gửi kết quả trở lại đúng provider/model để tạo câu trả lời cuối. Lượt tiếp tục không cấp thêm tool.</p>";

    const permissionLegend = document.createElement("div");
    permissionLegend.className = "v080-permission-legend";
    permissionLegend.innerHTML = "<span>READ · đọc</span><span>WRITE · ghi</span><span>DELETE · xóa</span><span>EXTERNAL · bên ngoài</span><span>SENSITIVE · nhạy cảm</span>";

    const status = document.createElement("p");
    status.id = "toolsStatus";
    status.className = "field-hint v080-tool-status";
    status.textContent = "Đang đọc danh mục công cụ…";

    const list = document.createElement("div");
    list.id = "toolsList";
    list.className = "v080-tool-list";
    list.setAttribute("role", "list");

    const note = document.createElement("p");
    note.className = "security-copy";
    note.textContent = "v0.8.8 giữ proposal → permission → confirmation → execution. Tool result được tóm tắt local trước; native round-trip chỉ xảy ra khi bạn xác nhận gửi output ra đúng provider/model, và lượt tiếp tục bị khóa không cho gọi thêm tool.";

    card.append(header, intro, permissionLegend, status, list, note);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
    document.body.appendChild(dialog);
    return dialog;
  }

  async function openToolsDialog() {
    const dialog = ensureToolsDialog();
    dialog.showModal();
    await loadCatalog();
  }

  async function refreshCount() {
    const payload = await fetchCatalog();
    const count = document.querySelector("#toolsSidebarCount");
    if (count) count.textContent = String(Array.isArray(payload.tools) ? payload.tools.length : 0);
  }

  async function loadCatalog() {
    if (loading) return;
    loading = true;
    const status = document.querySelector("#toolsStatus");
    if (status) status.textContent = "Đang đọc danh mục công cụ…";

    try {
      const payload = await fetchCatalog();
      renderCatalog(payload);
    } catch (error) {
      if (status) status.textContent = error.message || "Không đọc được danh mục công cụ.";
    } finally {
      loading = false;
    }
  }

  async function fetchCatalog() {
    const response = await fetch("/api/tools", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.error || "Không đọc được danh mục công cụ.");
    return payload;
  }

  function renderCatalog(payload) {
    const tools = Array.isArray(payload.tools) ? payload.tools : [];
    const count = document.querySelector("#toolsSidebarCount");
    if (count) count.textContent = String(tools.length);

    const status = document.querySelector("#toolsStatus");
    if (status) {
      status.textContent = `${tools.length} công cụ đã đăng ký · framework ${payload.frameworkVersion || VERSION}`;
    }

    const list = document.querySelector("#toolsList");
    if (!list) return;
    list.replaceChildren();

    if (tools.length === 0) {
      const empty = document.createElement("p");
      empty.className = "field-hint";
      empty.textContent = "Chưa có công cụ nào được đăng ký.";
      list.appendChild(empty);
      return;
    }

    tools.forEach(tool => list.appendChild(createToolCard(tool)));
  }

  function createToolCard(tool) {
    const article = document.createElement("article");
    article.className = "v080-tool-item";
    article.setAttribute("role", "listitem");

    const header = document.createElement("div");
    header.className = "v080-tool-item-header";
    const name = document.createElement("strong");
    name.textContent = tool.name || "Công cụ";
    const version = document.createElement("span");
    version.textContent = `v${tool.version || "?"}`;
    header.append(name, version);

    const description = document.createElement("p");
    description.textContent = tool.description || "";

    const meta = document.createElement("div");
    meta.className = "v080-tool-meta";
    const permissions = Array.isArray(tool.requiredPermissions) ? tool.requiredPermissions : [];
    permissions.forEach(permission => {
      const badge = document.createElement("span");
      badge.className = "v080-permission-badge";
      badge.textContent = permission;
      meta.appendChild(badge);
    });

    const timeout = document.createElement("span");
    timeout.textContent = `${tool.timeoutMs || 0} ms`;
    meta.appendChild(timeout);
    const locality = document.createElement("span");
    locality.textContent = tool.localOnly ? "local" : "external-capable";
    meta.appendChild(locality);
    if (tool.requiresConfirmation) {
      const confirm = document.createElement("span");
      confirm.textContent = "cần xác nhận";
      meta.appendChild(confirm);
    }

    article.append(header, description, meta);
    return article;
  }
})();
