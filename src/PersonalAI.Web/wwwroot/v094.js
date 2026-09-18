(() => {
  const VERSION = "0.9.4";
  const ACTIVE_KEY = "personal-ai-v0.9.4-active-workspace";
  const HEADER = "X-PersonalAI-Workspace";
  const DEFAULT_WORKSPACE = "personal";
  const nativeFetch = window.fetch.bind(window);

  let activeWorkspaceId = readActiveWorkspace();
  let workspaces = [];
  let managementBusy = false;

  window.PersonalAiWorkspace = {
    get currentId() {
      return activeWorkspaceId;
    },
    headerName: HEADER,
    nativeFetch,
    setActive: activateWorkspace,
    refresh: loadWorkspaces
  };

  window.fetch = function workspaceAwareFetch(input, init = {}) {
    const requestUrl = resolveRequestUrl(input);
    if (!requestUrl
        || requestUrl.origin !== window.location.origin
        || !requestUrl.pathname.startsWith("/api/")) {
      return nativeFetch(input, init);
    }

    const requestHeaders = input instanceof Request
      ? input.headers
      : undefined;
    const headers = new Headers(init.headers || requestHeaders || {});
    if (!headers.has(HEADER)) {
      headers.set(HEADER, activeWorkspaceId);
    }

    return nativeFetch(input, { ...init, headers });
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureWorkspaceSwitcher();
    ensureManagementDialog();
    loadWorkspaces().catch(error => {
      setSwitcherStatus(error.message || "Không tải được không gian làm việc.", true);
    });
  }

  function updateVersion() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) {
      brandVersion.textContent = `Phiên bản ${VERSION}`;
    }
  }

  function ensureWorkspaceSwitcher() {
    if (document.querySelector("#workspaceSwitcher")) return;

    const sidebar = document.querySelector("#sidebar");
    const brand = sidebar?.querySelector(".brand");
    if (!sidebar || !brand) return;

    const section = document.createElement("section");
    section.id = "workspaceSwitcher";
    section.className = "v094-workspace-switcher";
    section.setAttribute("aria-label", "Không gian làm việc");

    const label = document.createElement("label");
    label.setAttribute("for", "workspaceSelect");
    label.textContent = "KHÔNG GIAN";

    const row = document.createElement("div");
    row.className = "v094-workspace-row";

    const select = document.createElement("select");
    select.id = "workspaceSelect";
    select.setAttribute("aria-label", "Chọn không gian làm việc");
    select.addEventListener("change", () => {
      if (select.value && select.value !== activeWorkspaceId) {
        activateWorkspace(select.value);
      }
    });

    const manage = document.createElement("button");
    manage.id = "workspaceManageButton";
    manage.type = "button";
    manage.className = "v094-workspace-manage";
    manage.setAttribute("aria-label", "Quản lý không gian làm việc");
    manage.title = "Quản lý không gian làm việc";
    manage.textContent = "•••";
    manage.addEventListener("click", openManagementDialog);

    const status = document.createElement("small");
    status.id = "workspaceSwitcherStatus";
    status.textContent = "Đang tải…";

    row.append(select, manage);
    section.append(label, row, status);
    brand.after(section);
  }

  function ensureManagementDialog() {
    if (document.querySelector("#workspaceDialog")) return;

    const dialog = document.createElement("dialog");
    dialog.id = "workspaceDialog";
    dialog.className = "settings-dialog v094-workspace-dialog";
    dialog.setAttribute("aria-labelledby", "workspaceDialogTitle");

    const card = document.createElement("div");
    card.className = "settings-card v094-workspace-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "MEMORY · DOCUMENTS · TASKS · FILES · CONVERSATIONS";
    const title = document.createElement("h2");
    title.id = "workspaceDialogTitle";
    title.textContent = "Không gian làm việc";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Không gian làm việc");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v094-workspace-intro";
    intro.innerHTML = "<strong>Workspace v0.9.4</strong><p>Mỗi không gian tách riêng trí nhớ, tài liệu, tác vụ, tệp làm việc và lịch sử hội thoại. Agents và Policies đã có chỗ trong mô hình workspace nhưng được giữ ở trạng thái dành trước cho các phiên bản sau.</p>";

    const form = document.createElement("form");
    form.id = "workspaceCreateForm";
    form.className = "v094-workspace-form";
    form.noValidate = true;

    const nameLabel = document.createElement("label");
    nameLabel.className = "field-label";
    nameLabel.setAttribute("for", "workspaceNameInput");
    nameLabel.textContent = "Tên không gian";

    const name = document.createElement("input");
    name.id = "workspaceNameInput";
    name.type = "text";
    name.maxLength = 60;
    name.placeholder = "Ví dụ: Dự án tốt nghiệp";

    const descriptionLabel = document.createElement("label");
    descriptionLabel.className = "field-label";
    descriptionLabel.setAttribute("for", "workspaceDescriptionInput");
    descriptionLabel.textContent = "Mô tả";

    const description = document.createElement("textarea");
    description.id = "workspaceDescriptionInput";
    description.rows = 2;
    description.maxLength = 300;
    description.placeholder = "Mô tả ngắn về dữ liệu và công việc trong không gian này.";

    const actions = document.createElement("div");
    actions.className = "v094-workspace-form-actions";
    const create = document.createElement("button");
    create.id = "workspaceCreateButton";
    create.type = "submit";
    create.className = "primary-button";
    create.textContent = "Tạo không gian";
    actions.appendChild(create);

    form.append(
      nameLabel,
      name,
      descriptionLabel,
      description,
      actions);
    form.addEventListener("submit", createWorkspace);

    const feedback = document.createElement("div");
    feedback.id = "workspaceFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    const listHeader = document.createElement("div");
    listHeader.className = "v094-workspace-list-header";
    const listTitle = document.createElement("strong");
    listTitle.textContent = "Các không gian";
    const count = document.createElement("span");
    count.id = "workspaceCount";
    listHeader.append(listTitle, count);

    const list = document.createElement("div");
    list.id = "workspaceList";
    list.className = "v094-workspace-list";

    const note = document.createElement("p");
    note.className = "security-copy";
    note.textContent = "Đổi workspace không di chuyển dữ liệu giữa các không gian. v0.9.4 cũng chưa cho xóa workspace để tránh vô tình làm mất hoặc bỏ mồ côi dữ liệu.";

    card.append(header, intro, form, feedback, listHeader, list, note);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
    document.body.appendChild(dialog);
  }

  async function loadWorkspaces() {
    const response = await window.fetch("/api/workspaces", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.error || "Không đọc được danh sách không gian làm việc.");
    }

    workspaces = Array.isArray(payload.workspaces) ? payload.workspaces : [];
    const serverWorkspace = typeof payload.currentWorkspaceId === "string"
      ? payload.currentWorkspaceId
      : DEFAULT_WORKSPACE;

    if (!workspaces.some(item => item.id === activeWorkspaceId)
        && workspaces.some(item => item.id === serverWorkspace)) {
      activeWorkspaceId = serverWorkspace;
      persistActiveWorkspace();
    }

    renderSwitcher();
    renderManagementList(payload.maximumWorkspaces);
    return payload;
  }

  function renderSwitcher() {
    const select = document.querySelector("#workspaceSelect");
    if (!select) return;

    select.replaceChildren();
    workspaces.forEach(workspace => {
      const option = document.createElement("option");
      option.value = workspace.id;
      option.textContent = workspace.name;
      option.selected = workspace.id === activeWorkspaceId;
      select.appendChild(option);
    });

    const current = workspaces.find(item => item.id === activeWorkspaceId);
    setSwitcherStatus(
      current
        ? `${moduleCount(current)} mô-đun hoạt động · dữ liệu tách riêng`
        : "Không gian cá nhân",
      false);
  }

  function renderManagementList(maximumWorkspaces = 20) {
    const list = document.querySelector("#workspaceList");
    if (!list) return;

    list.replaceChildren();
    workspaces.forEach(workspace => {
      const article = document.createElement("article");
      article.className = "v094-workspace-item";
      if (workspace.id === activeWorkspaceId) {
        article.classList.add("active");
      }

      const content = document.createElement("div");
      content.className = "v094-workspace-item-content";

      const heading = document.createElement("div");
      heading.className = "v094-workspace-item-heading";
      const name = document.createElement("strong");
      name.textContent = workspace.name || workspace.id;
      const badge = document.createElement("span");
      badge.textContent = workspace.id === activeWorkspaceId
        ? "ĐANG DÙNG"
        : workspace.isBuiltIn ? "CÓ SẴN" : "TÙY CHỈNH";
      heading.append(name, badge);

      const description = document.createElement("p");
      description.textContent = workspace.description || "Không có mô tả.";

      const modules = document.createElement("p");
      modules.className = "v094-workspace-modules";
      const active = Array.isArray(workspace.activeModules)
        ? workspace.activeModules
        : [];
      const reserved = Array.isArray(workspace.reservedModules)
        ? workspace.reservedModules
        : [];
      modules.textContent =
        `Hoạt động: ${active.join(", ") || "—"} · Dành trước: ${reserved.join(", ") || "—"}`;

      content.append(heading, description, modules);

      const actions = document.createElement("div");
      actions.className = "v094-workspace-item-actions";

      if (workspace.id !== activeWorkspaceId) {
        const activate = document.createElement("button");
        activate.type = "button";
        activate.className = "secondary-button";
        activate.textContent = "Mở";
        activate.disabled = managementBusy;
        activate.addEventListener("click", () => activateWorkspace(workspace.id));
        actions.appendChild(activate);
      }

      const rename = document.createElement("button");
      rename.type = "button";
      rename.className = "secondary-button";
      rename.textContent = "Đổi tên";
      rename.disabled = managementBusy;
      rename.addEventListener("click", () => renameWorkspace(workspace));
      actions.appendChild(rename);

      article.append(content, actions);
      list.appendChild(article);
    });

    const count = document.querySelector("#workspaceCount");
    if (count) {
      count.textContent = `${workspaces.length}/${maximumWorkspaces || 20}`;
    }
  }

  async function openManagementDialog() {
    const dialog = ensureManagementDialog();
    dialog.showModal();
    setWorkspaceFeedback("Đang tải…");
    try {
      await loadWorkspaces();
      setWorkspaceFeedback("");
    } catch (error) {
      setWorkspaceFeedback(error.message, true);
    }
  }

  async function createWorkspace(event) {
    event.preventDefault();
    if (managementBusy) return;

    const name = document.querySelector("#workspaceNameInput")?.value.trim() || "";
    const description = document.querySelector("#workspaceDescriptionInput")?.value.trim() || "";
    if (name.length < 2) {
      setWorkspaceFeedback("Tên không gian cần có ít nhất 2 ký tự.", true);
      return;
    }

    setManagementBusy(true);
    setWorkspaceFeedback("Đang tạo không gian…");
    try {
      const response = await window.fetch("/api/workspaces", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name, description })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không tạo được không gian làm việc.");
      }

      const nameInput = document.querySelector("#workspaceNameInput");
      const descriptionInput = document.querySelector("#workspaceDescriptionInput");
      if (nameInput) nameInput.value = "";
      if (descriptionInput) descriptionInput.value = "";
      await loadWorkspaces();
      setWorkspaceFeedback(`Đã tạo “${payload.name}”.`);
    } catch (error) {
      setWorkspaceFeedback(error.message || "Không tạo được không gian làm việc.", true);
    } finally {
      setManagementBusy(false);
    }
  }

  async function renameWorkspace(workspace) {
    if (managementBusy) return;

    const promptUi = window.PersonalAiUi?.prompt;
    let name;
    if (typeof promptUi === "function") {
      name = await promptUi(
        "Tên mới của không gian làm việc:",
        workspace.name || "",
        {
          title: "Đổi tên không gian",
          confirmText: "Lưu",
          maxLength: 60
        });
    } else {
      name = window.prompt(
        "Tên mới của không gian làm việc:",
        workspace.name || "");
    }

    if (name === null) return;
    name = String(name).trim();
    if (name.length < 2) {
      setWorkspaceFeedback("Tên không gian cần có ít nhất 2 ký tự.", true);
      return;
    }

    setManagementBusy(true);
    setWorkspaceFeedback(`Đang đổi tên “${workspace.name}”…`);
    try {
      const response = await window.fetch(
        `/api/workspaces/${encodeURIComponent(workspace.id)}`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            name,
            description: workspace.description || ""
          })
        });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không đổi tên được không gian làm việc.");
      }

      await loadWorkspaces();
      setWorkspaceFeedback(`Đã đổi tên thành “${payload.name}”.`);
    } catch (error) {
      setWorkspaceFeedback(error.message || "Không đổi tên được không gian làm việc.", true);
    } finally {
      setManagementBusy(false);
    }
  }

  function activateWorkspace(workspaceId) {
    const normalized = normalizeWorkspaceId(workspaceId);
    if (!normalized || normalized === activeWorkspaceId) return;

    if (workspaces.length > 0
        && !workspaces.some(item => item.id === normalized)) {
      setWorkspaceFeedback("Không gian làm việc không tồn tại.", true);
      return;
    }

    activeWorkspaceId = normalized;
    persistActiveWorkspace();
    window.location.reload();
  }

  function readActiveWorkspace() {
    try {
      return normalizeWorkspaceId(localStorage.getItem(ACTIVE_KEY))
        || DEFAULT_WORKSPACE;
    } catch {
      return DEFAULT_WORKSPACE;
    }
  }

  function persistActiveWorkspace() {
    try {
      localStorage.setItem(ACTIVE_KEY, activeWorkspaceId);
    } catch {
      // Browser persistence is best-effort. The current page can still use the workspace.
    }
  }

  function normalizeWorkspaceId(value) {
    const normalized = String(value || "").trim().toLowerCase();
    return /^[a-z0-9][a-z0-9-]{0,79}$/.test(normalized)
      ? normalized
      : "";
  }

  function resolveRequestUrl(input) {
    try {
      const raw = input instanceof Request ? input.url : String(input);
      return new URL(raw, window.location.href);
    } catch {
      return null;
    }
  }

  function moduleCount(workspace) {
    return Array.isArray(workspace?.activeModules)
      ? workspace.activeModules.length
      : 0;
  }

  function setSwitcherStatus(message, error = false) {
    const status = document.querySelector("#workspaceSwitcherStatus");
    if (!status) return;
    status.textContent = message || "";
    status.classList.toggle("error", error);
  }

  function setWorkspaceFeedback(message, error = false) {
    const feedback = document.querySelector("#workspaceFeedback");
    if (!feedback) return;
    feedback.textContent = message || "";
    feedback.className = `settings-feedback ${error ? "error" : ""}`.trim();
  }

  function setManagementBusy(value) {
    managementBusy = value;
    document.querySelectorAll(
      "#workspaceDialog button, #workspaceDialog input, #workspaceDialog textarea")
      .forEach(element => {
        element.disabled = value;
      });
    renderManagementList();
  }
})();
