const LEGACY_MESSAGES_STORAGE_KEY = "personal-ai-v0.1-messages";
const LEGACY_WORKSPACE_STORAGE_KEY = "personal-ai-v0.4-workspace";
const ACTIVE_WORKSPACE_ID = window.PersonalAiWorkspace?.currentId || "personal";
const WORKSPACE_STORAGE_KEY = `personal-ai-v0.9.4-workspace-${ACTIVE_WORKSPACE_ID}`;
const MAX_STORED_MESSAGES = 40;
const MAX_TITLE_LENGTH = 42;
const CUSTOM_MODEL_VALUE = "__custom__";
const USE_TOOLS_STORAGE_KEY = "personal-ai-v0.8.5-use-tools";
const DEFAULT_MODELS = {
  Gemini: "gemini-3.1-flash-lite",
  OpenAI: "gpt-5.6-luna"
};
const MODEL_OPTIONS = {
  Gemini: ["gemini-3.1-flash-lite"],
  OpenAI: ["gpt-5.6-luna"]
};

const initialWorkspace = loadWorkspace();
const state = {
  conversations: initialWorkspace.conversations,
  activeConversationId: initialWorkspace.activeConversationId,
  busy: false,
  settingsBusy: false,
  knowledgeBusy: false,
  knowledgeSearchBusy: false,
  knowledgeDocuments: [],
  configured: false,
  provider: "",
  model: ""
};

const elements = {
  form: document.querySelector("#chatForm"),
  input: document.querySelector("#messageInput"),
  send: document.querySelector("#sendButton"),
  messages: document.querySelector("#messages"),
  welcome: document.querySelector("#welcome"),
  statusDot: document.querySelector("#statusDot"),
  statusText: document.querySelector("#statusText"),
  clear: document.querySelector("#clearButton"),
  newChat: document.querySelector("#newChatButton"),
  menu: document.querySelector("#menuButton"),
  sidebar: document.querySelector("#sidebar"),
  conversationList: document.querySelector("#conversationList"),
  conversationCount: document.querySelector("#conversationCount"),
  knowledgeButton: document.querySelector("#knowledgeButton"),
  knowledgeSidebarCount: document.querySelector("#knowledgeSidebarCount"),
  knowledgeDialog: document.querySelector("#knowledgeDialog"),
  knowledgeClose: document.querySelector("#knowledgeCloseButton"),
  knowledgeUploadForm: document.querySelector("#knowledgeUploadForm"),
  knowledgeFile: document.querySelector("#knowledgeFileInput"),
  knowledgeDropzone: document.querySelector("#knowledgeDropzone"),
  knowledgeFeedback: document.querySelector("#knowledgeFeedback"),
  knowledgeSummary: document.querySelector("#knowledgeSummary"),
  knowledgeList: document.querySelector("#knowledgeList"),
  knowledgeEmpty: document.querySelector("#knowledgeEmpty"),
  knowledgeRefresh: document.querySelector("#knowledgeRefreshButton"),
  knowledgeSearchForm: document.querySelector("#knowledgeSearchForm"),
  knowledgeSearchInput: document.querySelector("#knowledgeSearchInput"),
  knowledgeSearchButton: document.querySelector("#knowledgeSearchButton"),
  knowledgeSearchResults: document.querySelector("#knowledgeSearchResults"),
  knowledgeSearchSummary: document.querySelector("#knowledgeSearchSummary"),
  knowledgeSearchList: document.querySelector("#knowledgeSearchList"),
  settingsButton: document.querySelector("#settingsButton"),
  settingsDialog: document.querySelector("#settingsDialog"),
  settingsClose: document.querySelector("#settingsCloseButton"),
  settingsForm: document.querySelector("#settingsForm"),
  provider: document.querySelector("#providerSelect"),
  modelSelect: document.querySelector("#modelSelect"),
  customModel: document.querySelector("#customModelInput"),
  apiKey: document.querySelector("#apiKeyInput"),
  toggleApiKey: document.querySelector("#toggleApiKeyButton"),
  apiKeyHint: document.querySelector("#apiKeyHint"),
  settingsFeedback: document.querySelector("#settingsFeedback"),
  saveSettings: document.querySelector("#saveSettingsButton"),
  testConnection: document.querySelector("#testConnectionButton"),
  useTools: document.querySelector("#useToolsToggle")
};

initialize();

async function initialize() {
  bindEvents();
  persistWorkspace();
  localStorage.removeItem(LEGACY_MESSAGES_STORAGE_KEY);
  renderConversationList();
  renderConversation();
  if (elements.useTools) {
    elements.useTools.checked = loadUseToolsPreference();
  }
  await Promise.all([refreshStatus(), refreshKnowledgeDocuments(false)]);
  elements.input.focus();
}

function bindEvents() {
  elements.form.addEventListener("submit", sendCurrentMessage);
  elements.input.addEventListener("input", resizeInput);
  elements.input.addEventListener("keydown", event => {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      elements.form.requestSubmit();
    }
  });

  elements.clear.addEventListener("click", clearActiveConversation);
  elements.newChat.addEventListener("click", createNewConversation);
  elements.menu.addEventListener("click", () => elements.sidebar.classList.toggle("open"));
  elements.knowledgeButton.addEventListener("click", openKnowledge);
  elements.knowledgeClose.addEventListener("click", () => elements.knowledgeDialog.close());
  elements.knowledgeUploadForm.addEventListener("submit", event => event.preventDefault());
  elements.knowledgeFile.addEventListener("change", () => {
    const file = elements.knowledgeFile.files?.[0];
    if (file) uploadKnowledgeDocument(file);
  });
  elements.knowledgeRefresh.addEventListener("click", () => refreshKnowledgeDocuments(true));
  elements.knowledgeSearchForm.addEventListener("submit", searchKnowledge);
  elements.knowledgeSearchInput.addEventListener("input", () => {
    if (!elements.knowledgeSearchInput.value.trim()) clearKnowledgeSearch();
  });
  ["dragenter", "dragover"].forEach(eventName => {
    elements.knowledgeDropzone.addEventListener(eventName, event => {
      event.preventDefault();
      if (!state.knowledgeBusy) elements.knowledgeDropzone.classList.add("dragging");
    });
  });
  ["dragleave", "drop"].forEach(eventName => {
    elements.knowledgeDropzone.addEventListener(eventName, event => {
      event.preventDefault();
      elements.knowledgeDropzone.classList.remove("dragging");
    });
  });
  elements.knowledgeDropzone.addEventListener("drop", event => {
    const file = event.dataTransfer?.files?.[0];
    if (file && !state.knowledgeBusy) uploadKnowledgeDocument(file);
  });
  elements.settingsButton.addEventListener("click", openSettings);
  elements.settingsClose.addEventListener("click", () => elements.settingsDialog.close());
  elements.settingsForm.addEventListener("submit", event => {
    event.preventDefault();
    saveAiSettings(false);
  });
  elements.testConnection.addEventListener("click", () => saveAiSettings(true));
  elements.useTools?.addEventListener("change", () => {
    try {
      localStorage.setItem(USE_TOOLS_STORAGE_KEY, elements.useTools.checked ? "1" : "0");
    } catch {
      // Preference persistence is best-effort only.
    }
  });
  elements.provider.addEventListener("change", () => {
    const selectedProvider = elements.provider.value;
    populateModelOptions(selectedProvider, DEFAULT_MODELS[selectedProvider]);
    elements.apiKey.value = "";
    elements.apiKeyHint.textContent = "Để trống để giữ khóa truy cập đã lưu cho nhà cung cấp này.";
    resetApiKeyVisibility();
  });
  elements.modelSelect.addEventListener("change", updateCustomModelVisibility);
  elements.toggleApiKey.addEventListener("click", toggleApiKeyVisibility);

  document.querySelectorAll("[data-prompt]").forEach(button => {
    button.addEventListener("click", () => {
      elements.input.value = button.dataset.prompt;
      resizeInput();
      elements.form.requestSubmit();
    });
  });
}

async function openSettings() {
  setSettingsFeedback("Đang tải cài đặt…");
  elements.settingsDialog.showModal();
  elements.sidebar.classList.remove("open");

  try {
    const response = await fetch("/api/settings/ai", { cache: "no-store" });
    const settings = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(settings.error || "Không đọc được cài đặt AI.");

    elements.provider.value = settings.provider;
    populateModelOptions(settings.provider, settings.model);
    elements.apiKey.value = "";
    resetApiKeyVisibility();
    elements.apiKeyHint.textContent = settings.hasApiKey
      ? `Đã có khóa truy cập: ${settings.maskedApiKey}. Để trống để giữ nguyên.`
      : "Chưa có khóa truy cập. Hãy nhập khóa để sử dụng nhà cung cấp này.";
    setSettingsFeedback("");
    elements.provider.focus();
  } catch (error) {
    setSettingsFeedback(error.message, "error");
  }
}

async function saveAiSettings(testAfterSave) {
  if (state.settingsBusy) return;

  const provider = elements.provider.value.trim();
  const model = getSelectedModel();
  if (!provider) {
    setSettingsFeedback("Hãy chọn nhà cung cấp AI.", "error");
    elements.provider.focus();
    return;
  }
  if (model.length < 2 || model.length > 120) {
    setSettingsFeedback("Hãy chọn hoặc nhập tên mô hình hợp lệ, từ 2 đến 120 ký tự.", "error");
    (elements.modelSelect.value === CUSTOM_MODEL_VALUE ? elements.customModel : elements.modelSelect).focus();
    return;
  }

  setSettingsBusy(true);
  setSettingsFeedback(testAfterSave ? "Đang lưu và kiểm tra kết nối…" : "Đang lưu cài đặt…");

  try {
    const response = await fetch("/api/settings/ai", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        provider,
        model,
        apiKey: elements.apiKey.value.trim()
      })
    });
    const settings = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(settings.error || "Không lưu được cài đặt AI.");

    elements.apiKey.value = "";
    elements.apiKeyHint.textContent = settings.hasApiKey
      ? `Đã có khóa truy cập: ${settings.maskedApiKey}. Để trống để giữ nguyên.`
      : "Chưa có khóa truy cập. Hãy nhập khóa để sử dụng nhà cung cấp này.";

    if (testAfterSave) {
      const testResponse = await fetch("/api/settings/ai/test", { method: "POST" });
      const testResult = await testResponse.json().catch(() => ({}));
      if (!testResponse.ok || !testResult.success) {
        throw new Error(testResult.message || "Không kết nối được với AI.");
      }
      setSettingsFeedback(
        `Kết nối thành công · Nhà cung cấp: ${settings.provider} · Mô hình: ${settings.model}.`,
        "success");
    } else {
      setSettingsFeedback("Đã lưu cài đặt an toàn trên máy này.", "success");
    }

    await refreshStatus();
  } catch (error) {
    setSettingsFeedback(error.message, "error");
  } finally {
    setSettingsBusy(false);
  }
}

function setSettingsBusy(value) {
  state.settingsBusy = value;
  elements.saveSettings.disabled = value;
  elements.testConnection.disabled = value;
  elements.provider.disabled = value;
  elements.modelSelect.disabled = value;
  elements.customModel.disabled = value;
  elements.apiKey.disabled = value;
  elements.toggleApiKey.disabled = value;
}

function setSettingsFeedback(message, type = "") {
  elements.settingsFeedback.textContent = message;
  elements.settingsFeedback.className = `settings-feedback ${type}`.trim();
}

function populateModelOptions(provider, selectedModel) {
  const recommendedModels = MODEL_OPTIONS[provider] || [];
  elements.modelSelect.replaceChildren();

  recommendedModels.forEach(model => {
    const option = document.createElement("option");
    option.value = model;
    option.textContent = model;
    elements.modelSelect.appendChild(option);
  });

  const customOption = document.createElement("option");
  customOption.value = CUSTOM_MODEL_VALUE;
  customOption.textContent = "Mô hình tùy chỉnh…";
  elements.modelSelect.appendChild(customOption);

  if (recommendedModels.includes(selectedModel)) {
    elements.modelSelect.value = selectedModel;
    elements.customModel.value = "";
  } else {
    elements.modelSelect.value = CUSTOM_MODEL_VALUE;
    elements.customModel.value = selectedModel || "";
  }

  updateCustomModelVisibility();
}

function updateCustomModelVisibility() {
  const usesCustomModel = elements.modelSelect.value === CUSTOM_MODEL_VALUE;
  elements.customModel.hidden = !usesCustomModel;
  elements.customModel.required = usesCustomModel;
  if (usesCustomModel && !state.settingsBusy) elements.customModel.focus();
}

function getSelectedModel() {
  return elements.modelSelect.value === CUSTOM_MODEL_VALUE
    ? elements.customModel.value.trim()
    : elements.modelSelect.value;
}

function toggleApiKeyVisibility() {
  const shouldShow = elements.apiKey.type === "password";
  elements.apiKey.type = shouldShow ? "text" : "password";
  elements.toggleApiKey.textContent = shouldShow ? "Ẩn" : "Hiện";
  elements.toggleApiKey.setAttribute("aria-label", shouldShow ? "Ẩn khóa truy cập" : "Hiện khóa truy cập");
  elements.toggleApiKey.setAttribute("aria-pressed", String(shouldShow));
}

function resetApiKeyVisibility() {
  elements.apiKey.type = "password";
  elements.toggleApiKey.textContent = "Hiện";
  elements.toggleApiKey.setAttribute("aria-label", "Hiện khóa truy cập");
  elements.toggleApiKey.setAttribute("aria-pressed", "false");
}

async function openKnowledge() {
  elements.knowledgeDialog.showModal();
  elements.sidebar.classList.remove("open");
  await refreshKnowledgeDocuments(true);
}

async function refreshKnowledgeDocuments(showFeedback) {
  if (state.knowledgeBusy) return;
  setKnowledgeBusy(true);
  if (showFeedback) setKnowledgeFeedback("Đang tải danh sách tài liệu…");

  try {
    const response = await fetch("/api/knowledge/documents", { cache: "no-store" });
    const documents = await response.json().catch(() => []);
    if (!response.ok) throw new Error(documents.error || "Không đọc được kho dữ liệu.");

    state.knowledgeDocuments = Array.isArray(documents) ? documents : [];
    renderKnowledgeDocuments();
    if (state.knowledgeDocuments.length === 0) clearKnowledgeSearch();
    if (showFeedback) setKnowledgeFeedback("");
  } catch (error) {
    if (showFeedback) setKnowledgeFeedback(error.message, "error");
  } finally {
    setKnowledgeBusy(false);
  }
}

async function uploadKnowledgeDocument(file) {
  if (state.knowledgeBusy) return;
  if (file.size > 10 * 1024 * 1024) {
    setKnowledgeFeedback("Tệp không được lớn hơn 10 MB.", "error");
    elements.knowledgeFile.value = "";
    return;
  }

  setKnowledgeBusy(true);
  setKnowledgeFeedback(`Đang lưu “${file.name}”…`);

  try {
    const formData = new FormData();
    formData.append("file", file);
    const response = await fetch("/api/knowledge/documents", {
      method: "POST",
      body: formData
    });
    const document = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(document.error || "Không tải được tài liệu.");

    state.knowledgeDocuments = [
      document,
      ...state.knowledgeDocuments.filter(item => item.id !== document.id)
    ];
    renderKnowledgeDocuments();
    clearKnowledgeSearch();
    setKnowledgeFeedback(`Đã lưu “${document.fileName}” trên máy này.`, "success");
  } catch (error) {
    setKnowledgeFeedback(error.message, "error");
  } finally {
    elements.knowledgeFile.value = "";
    setKnowledgeBusy(false);
  }
}

async function deleteKnowledgeDocument(document) {
  if (state.knowledgeBusy) return;
  if (!(await showVietnameseConfirm(`Xóa tài liệu “${document.fileName}” khỏi kho dữ liệu?`, { title: "Xác nhận xóa", confirmText: "Xóa", danger: true }))) return;

  setKnowledgeBusy(true);
  setKnowledgeFeedback(`Đang xóa “${document.fileName}”…`);

  try {
    const response = await fetch(`/api/knowledge/documents/${encodeURIComponent(document.id)}`, {
      method: "DELETE"
    });
    if (!response.ok && response.status !== 404) {
      const payload = await response.json().catch(() => ({}));
      throw new Error(payload.error || "Không xóa được tài liệu.");
    }

    state.knowledgeDocuments = state.knowledgeDocuments.filter(item => item.id !== document.id);
    renderKnowledgeDocuments();
    clearKnowledgeSearch();
    setKnowledgeFeedback(`Đã xóa “${document.fileName}”.`, "success");
  } catch (error) {
    setKnowledgeFeedback(error.message, "error");
  } finally {
    setKnowledgeBusy(false);
  }
}

function renderKnowledgeDocuments() {
  const count = state.knowledgeDocuments.length;
  elements.knowledgeSidebarCount.textContent = String(count);
  elements.knowledgeSummary.textContent = `${count} tài liệu · lưu trên máy này`;
  elements.knowledgeList.replaceChildren();
  elements.knowledgeEmpty.hidden = count > 0;
  elements.knowledgeList.hidden = count === 0;

  state.knowledgeDocuments.forEach(document => {
    elements.knowledgeList.appendChild(createKnowledgeDocumentNode(document));
  });

  updateKnowledgeSearchControls();
}

function createKnowledgeDocumentNode(document) {
  const item = documentElement("article", "knowledge-item");
  item.setAttribute("role", "listitem");

  const type = documentElement("div", "knowledge-type", document.fileType || "TXT");
  const details = documentElement("div", "knowledge-details");
  const name = documentElement("strong", "knowledge-name", document.fileName);
  name.title = document.fileName;
  const metadata = documentElement(
    "span",
    "knowledge-metadata",
    `${formatFileSize(document.fileSize)} · ${Number(document.characterCount || 0).toLocaleString("vi-VN")} ký tự · ${Number(document.chunkCount || 0).toLocaleString("vi-VN")} đoạn · ${formatDocumentDate(document.createdAt)}`);
  details.append(name, metadata);

  const status = documentElement("span", "knowledge-status", document.status === "ready" ? "Sẵn sàng" : "Đang xử lý");
  const deleteButton = documentElement("button", "knowledge-delete", "Xóa");
  deleteButton.type = "button";
  deleteButton.setAttribute("aria-label", `Xóa ${document.fileName}`);
  deleteButton.addEventListener("click", () => deleteKnowledgeDocument(document));

  item.append(type, details, status, deleteButton);
  return item;
}


function createVietnameseDialogShell(title, message) {
  const dialog = document.createElement("dialog");
  dialog.className = "settings-dialog app-message-dialog";
  dialog.setAttribute("aria-modal", "true");

  const card = document.createElement("div");
  card.className = "settings-card app-message-card";

  const header = document.createElement("header");
  header.className = "settings-header";
  const headingWrap = document.createElement("div");
  const eyebrow = documentElement("p", "eyebrow", "THÔNG BÁO TỪ ỨNG DỤNG");
  const heading = documentElement("h2", "app-message-title", title);
  headingWrap.append(eyebrow, heading);
  header.appendChild(headingWrap);

  const body = documentElement("p", "app-message-copy", message);
  const actions = documentElement("div", "settings-actions app-message-actions");

  card.append(header, body, actions);
  dialog.appendChild(card);
  document.body.appendChild(dialog);

  return { dialog, card, actions };
}

function showVietnameseConfirm(message, options = {}) {
  return new Promise(resolve => {
    const title = options.title || "Xác nhận";
    const confirmText = options.confirmText || "Xác nhận";
    const cancelText = options.cancelText || "Hủy";
    const { dialog, actions } = createVietnameseDialogShell(title, message);

    const cancel = documentElement("button", "secondary-button", cancelText);
    cancel.type = "button";
    const confirm = documentElement(
      "button",
      options.danger ? "danger-button" : "primary-button",
      confirmText);
    confirm.type = "button";

    let settled = false;
    const finish = value => {
      if (settled) return;
      settled = true;
      dialog.close();
      dialog.remove();
      resolve(value);
    };

    cancel.addEventListener("click", () => finish(false));
    confirm.addEventListener("click", () => finish(true));
    dialog.addEventListener("cancel", event => {
      event.preventDefault();
      finish(false);
    });
    actions.append(cancel, confirm);
    dialog.showModal();
    confirm.focus();
  });
}

function showVietnamesePrompt(message, initialValue = "", options = {}) {
  return new Promise(resolve => {
    const title = options.title || "Nhập thông tin";
    const confirmText = options.confirmText || "Xác nhận";
    const cancelText = options.cancelText || "Hủy";
    const { dialog, card, actions } = createVietnameseDialogShell(title, message);

    const input = document.createElement("input");
    input.className = "app-message-input";
    input.type = "text";
    input.maxLength = Number(options.maxLength || 180);
    input.value = initialValue || "";
    input.placeholder = options.placeholder || "";
    card.insertBefore(input, actions);

    const cancel = documentElement("button", "secondary-button", cancelText);
    cancel.type = "button";
    const confirm = documentElement("button", "primary-button", confirmText);
    confirm.type = "button";

    let settled = false;
    const finish = value => {
      if (settled) return;
      settled = true;
      dialog.close();
      dialog.remove();
      resolve(value);
    };

    cancel.addEventListener("click", () => finish(null));
    confirm.addEventListener("click", () => finish(input.value));
    input.addEventListener("keydown", event => {
      if (event.key === "Enter") {
        event.preventDefault();
        finish(input.value);
      }
    });
    dialog.addEventListener("cancel", event => {
      event.preventDefault();
      finish(null);
    });
    actions.append(cancel, confirm);
    dialog.showModal();
    input.focus();
    input.select();
  });
}

function showVietnameseNotice(message, options = {}) {
  return new Promise(resolve => {
    const title = options.title || "Thông báo";
    const buttonText = options.buttonText || "Đóng";
    const { dialog, actions } = createVietnameseDialogShell(title, message);
    const close = documentElement("button", "primary-button", buttonText);
    close.type = "button";

    let settled = false;
    const finish = () => {
      if (settled) return;
      settled = true;
      dialog.close();
      dialog.remove();
      resolve();
    };

    close.addEventListener("click", finish);
    dialog.addEventListener("cancel", event => {
      event.preventDefault();
      finish();
    });
    actions.appendChild(close);
    dialog.showModal();
    close.focus();
  });
}

window.PersonalAiUi = {
  confirm: showVietnameseConfirm,
  prompt: showVietnamesePrompt,
  notice: showVietnameseNotice
};

function documentElement(tagName, className, text = "") {
  const node = document.createElement(tagName);
  node.className = className;
  node.textContent = text;
  return node;
}

function formatFileSize(bytes) {
  const value = Number(bytes) || 0;
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function formatDocumentDate(timestamp) {
  const date = new Date(timestamp);
  return Number.isNaN(date.getTime())
    ? "Không rõ thời gian"
    : date.toLocaleString("vi-VN", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit"
      });
}

function setKnowledgeBusy(value) {
  state.knowledgeBusy = value;
  elements.knowledgeFile.disabled = value;
  elements.knowledgeRefresh.disabled = value;
  elements.knowledgeDropzone.classList.toggle("busy", value);
  elements.knowledgeDropzone.setAttribute("aria-busy", String(value));
  updateKnowledgeSearchControls();
}

function setKnowledgeFeedback(message, type = "") {
  elements.knowledgeFeedback.textContent = message;
  elements.knowledgeFeedback.className = `settings-feedback knowledge-feedback ${type}`.trim();
}

async function searchKnowledge(event) {
  event.preventDefault();
  if (state.knowledgeBusy || state.knowledgeSearchBusy) return;
  const query = elements.knowledgeSearchInput.value.trim();
  if (query.length < 2) {
    elements.knowledgeSearchResults.hidden = false;
    elements.knowledgeSearchSummary.textContent = "Hãy nhập ít nhất 2 ký tự để tìm kiếm.";
    elements.knowledgeSearchList.replaceChildren();
    elements.knowledgeSearchInput.focus();
    return;
  }

  setKnowledgeSearchBusy(true);
  elements.knowledgeSearchResults.hidden = false;
  elements.knowledgeSearchSummary.textContent = "Đang tìm…";
  elements.knowledgeSearchList.replaceChildren();

  try {
    const response = await fetch(
      `/api/knowledge/search?query=${encodeURIComponent(query)}&limit=5`,
      { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.error || "Không tìm được trong kho dữ liệu.");

    renderKnowledgeSearchResults(payload);
  } catch (error) {
    elements.knowledgeSearchSummary.textContent = "Không thể tìm kiếm";
    elements.knowledgeSearchList.appendChild(
      documentElement("p", "knowledge-search-empty error", error.message));
  } finally {
    setKnowledgeSearchBusy(false);
  }
}

function renderKnowledgeSearchResults(payload) {
  const results = Array.isArray(payload.results) ? payload.results : [];
  elements.knowledgeSearchResults.hidden = false;
  elements.knowledgeSearchSummary.textContent = results.length > 0
    ? `${results.length} đoạn liên quan nhất`
    : "Không có đoạn phù hợp";
  elements.knowledgeSearchList.replaceChildren();

  if (results.length === 0) {
    elements.knowledgeSearchList.appendChild(
      documentElement(
        "p",
        "knowledge-search-empty",
        "Thử dùng từ khóa ngắn hơn hoặc từ xuất hiện trong tài liệu."));
    return;
  }

  results.forEach(result => {
    const item = documentElement("article", "knowledge-search-item");
    const heading = documentElement("div", "knowledge-search-item-heading");
    heading.append(
      documentElement("strong", "", result.fileName),
      documentElement("span", "", `Đoạn ${result.chunkIndex}`));
    const content = documentElement("p", "", result.content);
    item.append(heading, content);
    elements.knowledgeSearchList.appendChild(item);
  });
}

function clearKnowledgeSearch() {
  elements.knowledgeSearchResults.hidden = true;
  elements.knowledgeSearchSummary.textContent = "";
  elements.knowledgeSearchList.replaceChildren();
}

function setKnowledgeSearchBusy(value) {
  state.knowledgeSearchBusy = value;
  elements.knowledgeSearchResults.setAttribute("aria-busy", String(value));
  updateKnowledgeSearchControls();
}

function updateKnowledgeSearchControls() {
  const disabled = state.knowledgeBusy
    || state.knowledgeSearchBusy
    || state.knowledgeDocuments.length === 0;
  elements.knowledgeSearchInput.disabled = disabled;
  elements.knowledgeSearchButton.disabled = disabled;
}

async function refreshStatus() {
  try {
    const response = await fetch("/api/status");
    if (!response.ok) throw new Error("Không đọc được trạng thái");
    const status = await response.json();
    state.configured = status.configured;
    state.provider = status.provider;
    state.model = status.model;
    elements.statusDot.className = `status-dot ${status.configured ? "online" : "offline"}`;
    elements.statusText.textContent = status.configured
      ? `Sẵn sàng · Nhà cung cấp: ${status.provider} · Mô hình: ${status.model}`
      : "Chưa cấu hình khóa truy cập";
  } catch {
    elements.statusDot.className = "status-dot offline";
    elements.statusText.textContent = "Không kết nối được máy chủ";
  }
}

async function sendCurrentMessage(event) {
  event.preventDefault();
  const content = elements.input.value.trim();
  if (!content || state.busy) return;

  const conversation = getActiveConversation();
  conversation.messages.push({ role: "user", content });
  if (conversation.messages.filter(message => message.role === "user").length === 1) {
    conversation.title = createConversationTitle(content);
  }
  touchConversation(conversation);
  trimMessages(conversation);
  persistWorkspace();
  elements.input.value = "";
  resizeInput();
  renderConversationList();
  renderConversation();
  setBusy(true);

  const typingNode = appendTyping();

  try {
    const response = await fetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        messages: conversation.messages.map(message => ({
          role: message.role,
          content: message.content
        })),
        useTools: elements.useTools?.checked === true,
        useTaskContext: true
      })
    });
    const payload = await response.json().catch(() => ({}));

    if (!response.ok) {
      throw new Error(payload.error || "Không thể nhận câu trả lời từ AI.");
    }

    conversation.messages.push({
      role: "assistant",
      content: payload.message,
      sources: normalizeSources(payload.sources),
      toolProposal: normalizeToolProposal(payload.toolProposal),
      context: normalizeContextReport(payload.context)
    });
    touchConversation(conversation);
    trimMessages(conversation);
    persistWorkspace();
    renderConversationList();
    renderConversation();
  } catch (error) {
    typingNode.remove();
    appendSystemError(error.message);
  } finally {
    setBusy(false);
    elements.input.focus();
  }
}

function renderConversation() {
  const conversation = getActiveConversation();
  elements.messages.querySelectorAll(".message-row").forEach(node => node.remove());
  elements.welcome.hidden = conversation.messages.length > 0;

  conversation.messages.forEach(message => {
    elements.messages.appendChild(
      createMessageNode(
        message.role,
        message.content,
        message.sources,
        message.toolProposal,
        message.toolExecution,
        message.context));
  });

  scrollToBottom();
}

function createMessageNode(role, content, sources = [], toolProposal = null, toolExecution = null, contextReport = null) {
  const row = document.createElement("article");
  row.className = `message-row ${role}`;

  const avatar = document.createElement("div");
  avatar.className = "avatar";
  avatar.textContent = role === "user" ? "B" : "AI";

  const bubble = document.createElement("div");
  bubble.className = "bubble";
  bubble.innerHTML = role === "assistant" ? renderMarkdown(content) : escapeHtml(content).replaceAll("\n", "<br>");

  const normalizedContext = normalizeContextReport(contextReport);
  if (role === "assistant" && normalizedContext) {
    bubble.appendChild(createContextReportNode(normalizedContext));
  }

  const normalizedSources = normalizeSources(sources);
  if (role === "assistant" && normalizedSources.length > 0) {
    bubble.appendChild(createMessageSourcesNode(normalizedSources));
  }

  if (role === "assistant" && toolProposal) {
    bubble.appendChild(createToolProposalNode(toolProposal));
  }

  if (role === "assistant" && toolExecution) {
    bubble.appendChild(createToolExecutionNode(toolExecution));
  }

  row.append(avatar, bubble);
  return row;
}

function createContextReportNode(report) {
  const section = documentElement("details", "context-report");
  const summary = documentElement("summary", "context-report-summary");

  const counts = [];
  if (report.selectedMemories > 0) counts.push(`${report.selectedMemories} trí nhớ`);
  if (report.selectedDocuments > 0) counts.push(`${report.selectedDocuments} tài liệu`);
  if (report.selectedTasks > 0) counts.push(`${report.selectedTasks} tác vụ`);
  const budget = report.budget || {};
  const used = Number(budget.usedCharacters) || 0;
  const maximum = Number(budget.maximumCharacters) || 0;

  summary.textContent = counts.length > 0
    ? `Ngữ cảnh đã chọn · ${counts.join(" · ")} · ${formatContextCharacters(used)}/${formatContextCharacters(maximum)}`
    : "Ngữ cảnh đã chọn · không cần thêm dữ liệu";

  const body = documentElement("div", "context-report-body");
  const meta = documentElement(
    "p",
    "context-report-meta",
    `Workspace: ${report.workspaceId || "personal"} · chiến lược: ${report.strategy || "context-manager"}`);
  body.appendChild(meta);

  if (Array.isArray(report.items) && report.items.length > 0) {
    const list = documentElement("div", "context-report-items");
    report.items.forEach(item => {
      const row = documentElement("div", "context-report-item");
      const label = documentElement(
        "strong",
        "context-report-item-label",
        `${contextKindLabel(item.kind)} · ${item.label || "Mục ngữ cảnh"}`);
      const reason = documentElement(
        "span",
        "context-report-item-reason",
        item.reason || "Được chọn vì có liên quan đến yêu cầu hiện tại.");
      row.append(label, reason);
      list.appendChild(row);
    });
    body.appendChild(list);
  }

  section.append(summary, body);
  return section;
}

function contextKindLabel(kind) {
  if (kind === "memory") return "Trí nhớ";
  if (kind === "document") return "Tài liệu";
  if (kind === "task") return "Tác vụ";
  return "Ngữ cảnh";
}

function formatContextCharacters(value) {
  const number = Math.max(0, Number(value) || 0);
  return number >= 1000
    ? `${(number / 1000).toFixed(number >= 10_000 ? 0 : 1)}k`
    : String(number);
}


function createMessageSourcesNode(sources) {
  const section = documentElement("section", "message-sources");
  const heading = documentElement(
    "strong",
    "message-sources-title",
    `Nguồn dữ liệu đã dùng · ${sources.length}`);
  const list = documentElement("div", "message-source-list");

  sources.forEach(source => {
    const item = documentElement(
      "span",
      "message-source",
      `${source.fileName} · Đoạn ${source.chunkIndex}`);
    item.title = `${source.fileName} — đoạn ${source.chunkIndex}`;
    list.appendChild(item);
  });

  section.append(heading, list);
  return section;
}


function createToolProposalNode(proposal) {
  const section = documentElement("section", "tool-proposal");
  section.dataset.proposalId = proposal.proposalId;

  const heading = documentElement("strong", "tool-proposal-title", toolDisplayName(proposal.toolName));
  const reason = documentElement("p", "tool-proposal-reason", proposal.reason || "Đề xuất công cụ");
  const planner = proposal.planningMode === "provider-native"
    ? documentElement(
        "div",
        "tool-proposal-planner",
        "Nguồn đề xuất: " + (proposal.planningProvider || "AI")
          + " · gọi hàm gốc của nhà cung cấp"
          + (proposal.planningModel ? " · " + proposal.planningModel : ""))
    : documentElement("div", "tool-proposal-planner", "Nguồn đề xuất: máy chủ / thủ công");
  const permissions = documentElement(
    "div",
    "tool-proposal-permissions",
    "Quyền: " + formatPermissionList(proposal.requiredPermissions));

  const argumentsTitle = documentElement("span", "tool-proposal-arguments-title", "Tham số");
  const argumentsPre = documentElement("pre", "tool-proposal-arguments");
  argumentsPre.textContent = formatToolArgumentsForDisplay(proposal.toolName, proposal.arguments);

  const actions = documentElement("div", "tool-proposal-actions");
  const button = documentElement(
    "button",
    proposal.requiresConfirmation ? "primary-button" : "secondary-button",
    proposal.requiresConfirmation ? "Xác nhận & chạy" : "Chạy công cụ");
  button.type = "button";

  const expiresAt = new Date(proposal.expiresAt);
  const expired = Number.isNaN(expiresAt.getTime()) || expiresAt.getTime() <= Date.now();
  if (proposal.executed) {
    button.disabled = true;
    button.textContent = "Đã chạy";
  } else if (expired) {
    button.disabled = true;
    button.textContent = "Đề xuất đã hết hạn";
  } else {
    button.addEventListener("click", () => executeToolProposal(proposal, button));
  }

  if (proposal.requiresConfirmation) {
    const warning = documentElement(
      "span",
      "tool-proposal-warning",
      "Thao tác này chưa chạy. Cần xác nhận rõ ràng trước khi thực thi.");
    actions.append(warning, button);
  } else {
    actions.append(button);
  }

  section.append(heading, reason, planner, permissions, argumentsTitle, argumentsPre, actions);
  return section;
}

async function executeToolProposal(proposal, button) {
  if (proposal.executed || state.busy) return;

  let confirmed = false;
  if (proposal.requiresConfirmation) {
    confirmed = await showVietnameseConfirm(
      "Chạy " + toolDisplayName(proposal.toolName) + " với quyền "
      + formatPermissionList(proposal.requiredPermissions)
      + "?\n\nChỉ xác nhận khi các tham số hiển thị phía trên đúng với thao tác bạn muốn.",
      { title: "Xác nhận chạy công cụ", confirmText: "Xác nhận và chạy" });
    if (!confirmed) return;
  }

  button.disabled = true;
  const originalText = button.textContent;
  button.textContent = "Đang chạy…";

  try {
    const response = await fetch("/api/tools/orchestrate/execute", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        proposalId: proposal.proposalId,
        confirmed
      })
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(
        payload.error
        || payload.execution?.error
        || "Không thể thực thi đề xuất công cụ.");
    }

    proposal.executed = true;
    proposal.executionStatus = payload.execution?.status || "succeeded";
    button.textContent = "Đã chạy";

    const conversation = getActiveConversation();
    conversation.messages.push({
      role: "assistant",
      content: typeof payload.localSummary === "string" && payload.localSummary.trim()
        ? payload.localSummary.trim()
        : "Công cụ đã chạy thành công.",
      toolExecution: normalizeToolExecution({
        invocationId: payload.execution?.invocationId,
        toolName: payload.execution?.toolName || payload.proposal?.toolName,
        status: payload.execution?.status,
        outputPreview: createToolOutputPreview(payload.execution?.toolName || payload.proposal?.toolName, payload.execution?.output),
        canAiSynthesize: payload.canAiSynthesize === true,
        synthesisExpiresAt: payload.synthesisExpiresAt,
        aiSynthesized: false,
        canNativeContinue: payload.canNativeContinue === true,
        nativeContinueExpiresAt: payload.nativeContinueExpiresAt,
        nativeContinued: false
      })
    });
    touchConversation(conversation);
    trimMessages(conversation);
    persistWorkspace();
    renderConversationList();
    renderConversation();
  } catch (error) {
    button.disabled = false;
    button.textContent = originalText;
    appendSystemError(error.message);
  }
}

function createToolExecutionNode(execution) {
  const section = documentElement("section", "tool-execution");
  section.dataset.invocationId = execution.invocationId;

  const header = documentElement("div", "tool-execution-header");
  const title = documentElement("strong", "tool-execution-title", toolDisplayName(execution.toolName));
  const status = documentElement(
    "span",
    "tool-execution-status",
    localizeExecutionStatus(execution.status));
  header.append(title, status);

  const privacy = documentElement(
    "p",
    "tool-execution-privacy",
    execution.nativeContinued
      ? "Tóm tắt ban đầu được tạo trên máy. Kết quả đã được gửi về đúng nhà cung cấp AI theo xác nhận của bạn để hoàn tất câu trả lời."
      : execution.aiSynthesized
        ? "Tóm tắt ban đầu được tạo trên máy. Kết quả đã được gửi sang nhà cung cấp AI theo xác nhận của bạn để diễn giải."
        : "Tóm tắt phía trên được tạo trên máy. Kết quả chưa được gửi sang nhà cung cấp AI.");

  const details = document.createElement("details");
  details.className = "tool-execution-details";
  const summary = documentElement("summary", "", "Xem kết quả đã rút gọn");
  const preview = documentElement("pre", "tool-execution-output");
  preview.textContent = execution.outputPreview || "{}";
  details.append(summary, preview);

  const actions = documentElement("div", "tool-execution-actions");

  if (execution.canNativeContinue) {
    const expiresAt = new Date(execution.nativeContinueExpiresAt);
    const nativeExpired = !execution.nativeContinueExpiresAt
      || Number.isNaN(expiresAt.getTime())
      || expiresAt.getTime() <= Date.now();
    const nativeButton = documentElement(
      "button",
      "primary-button",
      execution.nativeContinued ? "Đã hoàn tất bằng AI" : "Hoàn tất câu trả lời bằng AI");
    nativeButton.type = "button";

    if (execution.nativeContinued) {
      nativeButton.disabled = true;
    } else if (nativeExpired) {
      nativeButton.disabled = true;
      nativeButton.textContent = "Lượt hoàn tất bằng AI đã hết hạn";
    } else {
      nativeButton.addEventListener(
        "click",
        () => continueNativeToolExecution(execution, nativeButton));
    }
    actions.appendChild(nativeButton);
  } else if (execution.canAiSynthesize) {
    const expiresAt = new Date(execution.synthesisExpiresAt);
    const synthesisExpired = !execution.synthesisExpiresAt
      || Number.isNaN(expiresAt.getTime())
      || expiresAt.getTime() <= Date.now();
    const synthesizeButton = documentElement(
      "button",
      "secondary-button",
      execution.aiSynthesized ? "Đã diễn giải bằng AI" : "Diễn giải bằng AI");
    synthesizeButton.type = "button";

    if (execution.aiSynthesized) {
      synthesizeButton.disabled = true;
    } else if (synthesisExpired) {
      synthesizeButton.disabled = true;
      synthesizeButton.textContent = "Đã hết thời gian diễn giải";
    } else {
      synthesizeButton.addEventListener(
        "click",
        () => synthesizeToolExecution(execution, synthesizeButton));
    }
    actions.appendChild(synthesizeButton);
  }

  section.append(header, privacy, details);
  if (actions.childElementCount > 0) {
    section.appendChild(actions);
  }
  return section;
}

async function continueNativeToolExecution(execution, button) {
  if (execution.nativeContinued || state.busy) return;

  const confirmed = await showVietnameseConfirm(
    "Để hoàn tất câu trả lời, kết quả công cụ sẽ được gửi về đúng nhà cung cấp và mô hình đã tạo đề xuất.\n\n"
    + "PersonalAI sẽ không cấp thêm công cụ trong lượt tiếp tục này. Chỉ tiếp tục nếu bạn đồng ý gửi phần kết quả ra nhà cung cấp AI.",
    { title: "Xác nhận gửi kết quả", confirmText: "Đồng ý và tiếp tục" });
  if (!confirmed) return;

  const originalText = button.textContent;
  button.disabled = true;
  button.textContent = "Đang hoàn tất…";

  try {
    const response = await fetch("/api/tools/orchestrate/continue-native", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        invocationId: execution.invocationId,
        confirmedExternal: true
      })
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.error || "Không thể hoàn tất lượt gọi hàm gốc của nhà cung cấp.");
    }

    execution.nativeContinued = true;

    const conversation = getActiveConversation();
    const storedMessage = conversation.messages.find(message =>
      message?.toolExecution?.invocationId === execution.invocationId);
    if (storedMessage?.toolExecution) {
      storedMessage.toolExecution.nativeContinued = true;
    }

    conversation.messages.push({
      role: "assistant",
      content: payload.message || "AI đã hoàn tất câu trả lời từ kết quả công cụ."
    });
    touchConversation(conversation);
    trimMessages(conversation);
    persistWorkspace();
    renderConversationList();
    renderConversation();
  } catch (error) {
    button.disabled = false;
    button.textContent = originalText;
    appendSystemError(error.message);
  }
}

async function synthesizeToolExecution(execution, button) {
  if (execution.aiSynthesized || state.busy) return;

  const confirmed = await showVietnameseConfirm(
    "Để AI diễn giải, kết quả của công cụ sẽ được gửi tới nhà cung cấp AI đang cấu hình.\n\n"
    + "Chỉ tiếp tục nếu bạn đồng ý gửi phần kết quả này ra ngoài ứng dụng chạy trên máy.",
    { title: "Xác nhận gửi kết quả", confirmText: "Đồng ý và diễn giải" });
  if (!confirmed) return;

  const originalText = button.textContent;
  button.disabled = true;
  button.textContent = "Đang diễn giải…";

  try {
    const response = await fetch("/api/tools/orchestrate/synthesize", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        invocationId: execution.invocationId,
        confirmedExternal: true
      })
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.error || "Không thể diễn giải kết quả công cụ bằng AI.");
    }

    execution.aiSynthesized = true;

    const conversation = getActiveConversation();
    const storedMessage = conversation.messages.find(message =>
      message?.toolExecution?.invocationId === execution.invocationId);
    if (storedMessage?.toolExecution) {
      storedMessage.toolExecution.aiSynthesized = true;
    }

    conversation.messages.push({
      role: "assistant",
      content: payload.message || "AI đã diễn giải kết quả công cụ."
    });
    touchConversation(conversation);
    trimMessages(conversation);
    persistWorkspace();
    renderConversationList();
    renderConversation();
  } catch (error) {
    button.disabled = false;
    button.textContent = originalText;
    appendSystemError(error.message);
  }
}

function createToolOutputPreview(toolName, output) {
  let serialized = formatToolOutputForDisplay(toolName, output);
  if (serialized.length > 6000) {
    serialized = serialized.slice(0, 6000) + "\n… (kết quả hiển thị đã được rút gọn)";
  }
  return serialized;
}


function toolDisplayName(toolName) {
  const names = {
    "app.summary": "Tổng quan ứng dụng",
    "documents.search": "Tìm trong tài liệu",
    "local.calculate": "Máy tính",
    "local.clock": "Đồng hồ hệ thống",
    "local.date_math": "Tính toán ngày giờ",
    "local.text_stats": "Thống kê văn bản",
    "memory.search": "Tìm trong trí nhớ",
    "workspace.create_directory": "Tạo thư mục",
    "workspace.delete": "Xóa tệp hoặc thư mục",
    "workspace.list": "Liệt kê thư mục làm việc",
    "workspace.move": "Di chuyển hoặc đổi tên",
    "workspace.read_text": "Đọc tệp văn bản",
    "workspace.write_text": "Ghi tệp văn bản"
  };
  return names[toolName] || "Công cụ";
}

function localizePermission(permission) {
  const labels = {
    READ: "Đọc",
    WRITE: "Ghi",
    DELETE: "Xóa",
    EXTERNAL: "Bên ngoài",
    SENSITIVE: "Nhạy cảm"
  };
  return labels[String(permission || "").toUpperCase()] || "Không xác định";
}

function formatPermissionList(permissions) {
  if (!Array.isArray(permissions) || permissions.length === 0) return "Không có";
  return permissions.map(localizePermission).join(", ");
}

function localizeExecutionStatus(status) {
  const labels = {
    succeeded: "Đã chạy",
    denied: "Bị từ chối",
    "invalid-input": "Dữ liệu không hợp lệ",
    "not-found": "Không tìm thấy",
    "timed-out": "Quá thời gian",
    failed: "Thất bại"
  };
  return labels[status] || "Không xác định";
}


function formatToolArgumentsForDisplay(toolName, args) {
  const value = args && typeof args === "object" ? args : {};
  const rows = [];
  const add = (label, item) => {
    if (item === undefined || item === null || item === "") return;
    rows.push(label + ": " + formatDisplayValue(item));
  };

  switch (toolName) {
    case "local.calculate":
      add("Biểu thức", value.expression);
      break;
    case "local.text_stats":
      add("Văn bản", value.text);
      break;
    case "local.date_math":
      add("Phép tính", localizeDateOperation(value.operation));
      add("Mốc bắt đầu", value.start);
      add("Mốc kết thúc", value.end);
      add("Số ngày", value.days);
      add("Số giờ", value.hours);
      add("Số phút", value.minutes);
      break;
    case "memory.search":
    case "documents.search":
      add("Nội dung cần tìm", value.query);
      add("Số kết quả tối đa", value.limit);
      break;
    case "workspace.list":
      add("Thư mục", value.path || ".");
      break;
    case "workspace.read_text":
      add("Đường dẫn tệp", value.path);
      break;
    case "workspace.write_text":
      add("Đường dẫn tệp", value.path);
      add("Nội dung", value.content);
      add("Cách ghi", localizeWriteMode(value.mode));
      add("Mã băm SHA-256 kỳ vọng", value.expectedSha256);
      break;
    case "workspace.create_directory":
      add("Đường dẫn thư mục", value.path);
      break;
    case "workspace.move":
      add("Đường dẫn nguồn", value.sourcePath);
      add("Đường dẫn đích", value.destinationPath);
      add("Mã băm SHA-256 kỳ vọng", value.expectedSha256);
      break;
    case "workspace.delete":
      add("Đường dẫn cần xóa", value.path);
      add("Mã băm SHA-256 kỳ vọng", value.expectedSha256);
      break;
    default:
      Object.entries(value).forEach(([key, item]) => add(localizeArgumentKey(key), item));
      break;
  }

  return rows.length ? rows.join("\n") : "Không có tham số.";
}

function formatToolOutputForDisplay(toolName, output) {
  if (!output || typeof output !== "object") return "Không có dữ liệu kết quả.";

  const rows = [];
  const add = (label, item) => {
    if (item === undefined || item === null || item === "") return;
    rows.push(label + ": " + formatDisplayValue(item));
  };

  switch (toolName) {
    case "local.calculate":
      add("Biểu thức", output.expression);
      add("Kết quả", output.resultText ?? output.result);
      break;
    case "local.clock":
      add("Giờ UTC", output.utcNow);
      add("Giờ trên máy", output.localNow);
      add("Múi giờ", output.timeZoneId);
      break;
    case "local.text_stats":
      add("Số ký tự", output.characterCount);
      add("Số từ", output.wordCount);
      add("Số dòng", output.lineCount);
      add("Số byte UTF-8", output.utf8Bytes);
      break;
    case "local.date_math":
      add("Phép tính", localizeDateOperation(output.operation));
      add("Kết quả", output.result ?? output.utcResult);
      add("Tổng số giờ chênh lệch", output.totalHours);
      break;
    case "memory.search":
    case "documents.search":
      add("Số kết quả", output.resultCount ?? output.results?.length);
      break;
    case "app.summary":
      add("Số trí nhớ", output.memory?.total);
      add("Số tài liệu", output.knowledge?.total);
      add("Số đoạn thiếu véc-tơ", output.embeddings?.missingChunks);
      break;
    case "workspace.list":
      add("Thư mục", output.path);
      add("Số mục trả về", output.returnedEntries ?? output.entryCount ?? output.entries?.length);
      break;
    case "workspace.read_text":
      add("Đường dẫn tệp", output.path);
      add("Số ký tự trả về", output.returnedCharacterCount);
      add("Tổng số ký tự", output.characterCount);
      add("Đã rút gọn", localizeBoolean(output.truncated));
      break;
    case "workspace.write_text":
      add("Đường dẫn tệp", output.path);
      add("Cách ghi", localizeWriteMode(output.mode));
      add("Đã tạo mới", localizeBoolean(output.created));
      add("Số byte đã ghi", output.bytesWritten);
      add("Kích thước hiện tại", output.sizeBytes);
      add("Mã băm SHA-256", output.sha256);
      break;
    case "workspace.create_directory":
      add("Đường dẫn thư mục", output.path);
      add("Đã tạo mới", localizeBoolean(output.created));
      break;
    case "workspace.move":
      add("Đường dẫn nguồn", output.sourcePath);
      add("Đường dẫn đích", output.destinationPath);
      add("Loại", localizeEntryType(output.type));
      add("Kích thước", output.sizeBytes);
      add("Mã băm SHA-256", output.sha256);
      break;
    case "workspace.delete":
      add("Đường dẫn", output.path);
      add("Loại", localizeEntryType(output.type));
      add("Đã xóa", localizeBoolean(output.deleted));
      add("Kích thước", output.sizeBytes);
      add("Mã băm SHA-256", output.sha256);
      break;
    default:
      Object.entries(output).forEach(([key, item]) => {
        if (typeof item !== "object") add(localizeArgumentKey(key), item);
      });
      break;
  }

  return rows.length ? rows.join("\n") : "Kết quả đã được xử lý thành công.";
}

function localizeArgumentKey(key) {
  const labels = {
    query: "Nội dung cần tìm",
    text: "Văn bản",
    expression: "Biểu thức",
    path: "Đường dẫn",
    content: "Nội dung",
    mode: "Cách ghi",
    sourcePath: "Đường dẫn nguồn",
    destinationPath: "Đường dẫn đích",
    expectedSha256: "Mã băm SHA-256 kỳ vọng",
    limit: "Số kết quả tối đa"
  };
  return labels[key] || "Thông tin";
}

function localizeWriteMode(mode) {
  return {
    create: "Tạo mới",
    overwrite: "Ghi đè",
    append: "Nối thêm"
  }[String(mode || "").toLowerCase()] || "Ghi";
}

function localizeDateOperation(operation) {
  return {
    add: "Cộng khoảng thời gian",
    difference: "Tính chênh lệch"
  }[String(operation || "").toLowerCase()] || formatDisplayValue(operation);
}

function localizeEntryType(type) {
  return {
    file: "Tệp",
    directory: "Thư mục"
  }[String(type || "").toLowerCase()] || "Mục";
}

function localizeBoolean(value) {
  if (value === true) return "Có";
  if (value === false) return "Không";
  return value;
}

function formatDisplayValue(value) {
  if (Array.isArray(value)) return value.map(formatDisplayValue).join(", ");
  if (value && typeof value === "object") return "Dữ liệu chi tiết";
  return String(value ?? "");
}

function safeJsonStringify(value) {
  try {
    return JSON.stringify(value ?? {}, null, 2);
  } catch {
    return "{}";
  }
}

function normalizeToolExecution(value) {
  if (!value || typeof value !== "object") return null;
  if (typeof value.invocationId !== "string" || !value.invocationId) return null;
  if (typeof value.toolName !== "string" || !value.toolName) return null;

  let outputPreview = typeof value.outputPreview === "string" ? value.outputPreview : "{}";
  if (outputPreview.length > 6200) {
    outputPreview = outputPreview.slice(0, 6200) + "\n…";
  }

  return {
    invocationId: value.invocationId,
    toolName: value.toolName,
    status: typeof value.status === "string" ? value.status : "succeeded",
    outputPreview,
    canAiSynthesize: value.canAiSynthesize === true,
    synthesisExpiresAt: typeof value.synthesisExpiresAt === "string"
      ? value.synthesisExpiresAt
      : "",
    aiSynthesized: value.aiSynthesized === true,
    canNativeContinue: value.canNativeContinue === true,
    nativeContinueExpiresAt: typeof value.nativeContinueExpiresAt === "string"
      ? value.nativeContinueExpiresAt
      : "",
    nativeContinued: value.nativeContinued === true
  };
}

function normalizeToolProposal(value) {
  if (!value || typeof value !== "object") return null;
  if (typeof value.proposalId !== "string" || !value.proposalId) return null;
  if (typeof value.toolName !== "string" || !value.toolName) return null;
  if (!value.arguments || typeof value.arguments !== "object" || Array.isArray(value.arguments)) return null;

  return {
    proposalId: value.proposalId,
    toolName: value.toolName,
    arguments: value.arguments,
    reason: typeof value.reason === "string" ? value.reason : "",
    assistantMessage: typeof value.assistantMessage === "string" ? value.assistantMessage : "",
    requiredPermissions: Array.isArray(value.requiredPermissions)
      ? value.requiredPermissions.filter(item => typeof item === "string").slice(0, 8)
      : [],
    requiresConfirmation: value.requiresConfirmation === true,
    expiresAt: typeof value.expiresAt === "string" ? value.expiresAt : "",
    planningMode: typeof value.planningMode === "string" ? value.planningMode : "manual",
    planningProvider: typeof value.planningProvider === "string" ? value.planningProvider : "",
    planningModel: typeof value.planningModel === "string" ? value.planningModel : "",
    executed: value.executed === true,
    executionStatus: typeof value.executionStatus === "string" ? value.executionStatus : ""
  };
}

function loadUseToolsPreference() {
  try {
    return localStorage.getItem(USE_TOOLS_STORAGE_KEY) === "1";
  } catch {
    return false;
  }
}

function appendTyping() {
  const row = createMessageNode("assistant", "");
  row.dataset.typing = "true";
  row.querySelector(".bubble").innerHTML = '<span class="typing"><i></i><i></i><i></i></span>';
  elements.messages.appendChild(row);
  scrollToBottom();
  return row;
}

function appendSystemError(message) {
  const row = createMessageNode("assistant", `**Chưa thể trả lời:** ${message}`);
  row.querySelector(".bubble").style.borderColor = "#e6b8b1";
  elements.messages.appendChild(row);
  scrollToBottom();
}

async function clearActiveConversation() {
  if (state.busy) return;
  const conversation = getActiveConversation();
  if (conversation.messages.length > 0
      && !(await showVietnameseConfirm("Xóa toàn bộ nội dung trong cuộc trò chuyện này?", { title: "Xác nhận xóa nội dung", confirmText: "Xóa", danger: true }))) {
    return;
  }

  conversation.messages = [];
  conversation.title = "Cuộc trò chuyện mới";
  touchConversation(conversation);
  persistWorkspace();
  elements.sidebar.classList.remove("open");
  renderConversationList();
  renderConversation();
  elements.input.focus();
}

function setBusy(value) {
  state.busy = value;
  elements.send.disabled = value;
  elements.input.disabled = value;
}

function resizeInput() {
  elements.input.style.height = "auto";
  elements.input.style.height = `${Math.min(elements.input.scrollHeight, 180)}px`;
}

function scrollToBottom() {
  requestAnimationFrame(() => {
    elements.messages.scrollTop = elements.messages.scrollHeight;
  });
}

function getActiveConversation() {
  let conversation = state.conversations.find(item => item.id === state.activeConversationId);
  if (!conversation) {
    conversation = createConversation();
    state.conversations.unshift(conversation);
    state.activeConversationId = conversation.id;
  }
  return conversation;
}

function createNewConversation() {
  if (state.busy) return;

  const activeConversation = getActiveConversation();
  if (activeConversation.messages.length === 0) {
    elements.sidebar.classList.remove("open");
    elements.input.focus();
    return;
  }

  const conversation = createConversation();
  state.conversations.unshift(conversation);
  state.activeConversationId = conversation.id;
  persistWorkspace();
  elements.sidebar.classList.remove("open");
  renderConversationList();
  renderConversation();
  elements.input.focus();
}

function selectConversation(conversationId) {
  if (state.busy || conversationId === state.activeConversationId) return;
  if (!state.conversations.some(item => item.id === conversationId)) return;

  state.activeConversationId = conversationId;
  persistWorkspace();
  elements.sidebar.classList.remove("open");
  renderConversationList();
  renderConversation();
  elements.input.focus();
}

async function renameConversation(conversationId) {
  if (state.busy) return;
  const conversation = state.conversations.find(item => item.id === conversationId);
  if (!conversation) return;

  const title = (await showVietnamesePrompt("Nhập tên mới cho cuộc trò chuyện:", conversation.title, { title: "Đổi tên cuộc trò chuyện", confirmText: "Lưu tên" }))?.trim();
  if (!title) return;

  conversation.title = createConversationTitle(title);
  touchConversation(conversation);
  persistWorkspace();
  renderConversationList();
}

async function deleteConversation(conversationId) {
  if (state.busy) return;
  const conversation = state.conversations.find(item => item.id === conversationId);
  if (!conversation) return;
  if (!(await showVietnameseConfirm(`Xóa cuộc trò chuyện “${conversation.title}”?`, { title: "Xác nhận xóa cuộc trò chuyện", confirmText: "Xóa", danger: true }))) return;

  state.conversations = state.conversations.filter(item => item.id !== conversationId);
  if (state.conversations.length === 0) {
    const replacement = createConversation();
    state.conversations.push(replacement);
    state.activeConversationId = replacement.id;
  } else if (state.activeConversationId === conversationId) {
    state.activeConversationId = [...state.conversations]
      .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))[0].id;
  }

  persistWorkspace();
  renderConversationList();
  renderConversation();
  elements.input.focus();
}

function renderConversationList() {
  const sortedConversations = [...state.conversations]
    .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt));

  elements.conversationList.replaceChildren();
  elements.conversationCount.textContent = String(sortedConversations.length);
  sortedConversations.forEach(conversation => {
    elements.conversationList.appendChild(createConversationListItem(conversation));
  });
}

function createConversationListItem(conversation) {
  const item = document.createElement("article");
  item.className = `conversation-item${conversation.id === state.activeConversationId ? " active" : ""}`;
  item.setAttribute("role", "listitem");

  const openButton = document.createElement("button");
  openButton.className = "conversation-open";
  openButton.type = "button";
  openButton.title = conversation.title;
  openButton.addEventListener("click", () => selectConversation(conversation.id));

  const title = document.createElement("span");
  title.className = "conversation-title";
  title.textContent = conversation.title;

  const time = document.createElement("span");
  time.className = "conversation-time";
  time.textContent = formatConversationTime(conversation.updatedAt);
  openButton.append(title, time);

  const renameButton = document.createElement("button");
  renameButton.className = "conversation-action";
  renameButton.type = "button";
  renameButton.textContent = "✎";
  renameButton.title = "Đổi tên";
  renameButton.setAttribute("aria-label", `Đổi tên ${conversation.title}`);
  renameButton.addEventListener("click", () => renameConversation(conversation.id));

  const deleteButton = document.createElement("button");
  deleteButton.className = "conversation-action delete";
  deleteButton.type = "button";
  deleteButton.textContent = "×";
  deleteButton.title = "Xóa";
  deleteButton.setAttribute("aria-label", `Xóa ${conversation.title}`);
  deleteButton.addEventListener("click", () => deleteConversation(conversation.id));

  item.append(openButton, renameButton, deleteButton);
  return item;
}

function formatConversationTime(timestamp) {
  const date = new Date(timestamp);
  const now = new Date();
  if (date.toDateString() === now.toDateString()) {
    return date.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" });
  }

  const ageInDays = Math.floor((now - date) / 86_400_000);
  if (ageInDays < 7) {
    return date.toLocaleDateString("vi-VN", { weekday: "long" });
  }

  return date.toLocaleDateString("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" });
}

function createConversationTitle(content) {
  const normalized = content.replace(/\s+/g, " ").trim();
  return normalized.length > MAX_TITLE_LENGTH
    ? `${normalized.slice(0, MAX_TITLE_LENGTH).trim()}…`
    : normalized || "Cuộc trò chuyện mới";
}

function touchConversation(conversation) {
  conversation.updatedAt = new Date().toISOString();
}

function trimMessages(conversation) {
  if (conversation.messages.length > MAX_STORED_MESSAGES) {
    conversation.messages = conversation.messages.slice(-MAX_STORED_MESSAGES);
  }
}

function createConversation(messages = []) {
  const now = new Date().toISOString();
  const firstQuestion = messages.find(message => message.role === "user")?.content;
  return {
    id: createConversationId(),
    title: firstQuestion ? createConversationTitle(firstQuestion) : "Cuộc trò chuyện mới",
    messages,
    createdAt: now,
    updatedAt: now
  };
}

function createConversationId() {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
  return `chat-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

function persistWorkspace() {
  const workspace = {
    version: 1,
    activeConversationId: state.activeConversationId,
    conversations: state.conversations
  };

  try {
    localStorage.setItem(WORKSPACE_STORAGE_KEY, JSON.stringify(workspace));
  } catch (error) {
    console.error("Không thể lưu lịch sử hội thoại.", error);
  }
}

function loadWorkspace() {
  const storageKeys = [WORKSPACE_STORAGE_KEY];
  if (ACTIVE_WORKSPACE_ID === "personal") {
    storageKeys.push(LEGACY_WORKSPACE_STORAGE_KEY);
  }

  for (const storageKey of storageKeys) {
    try {
      const savedWorkspace = JSON.parse(localStorage.getItem(storageKey) || "null");
      const conversations = Array.isArray(savedWorkspace?.conversations)
        ? savedWorkspace.conversations.map(normalizeConversation).filter(Boolean)
        : [];

      if (conversations.length > 0) {
        const activeConversationId = conversations.some(
          conversation => conversation.id === savedWorkspace.activeConversationId)
          ? savedWorkspace.activeConversationId
          : conversations[0].id;
        return { conversations, activeConversationId };
      }
    } catch {
      // Ignore damaged browser storage and continue with the next migration source.
    }
  }

  const migratedConversation = createConversation(
    ACTIVE_WORKSPACE_ID === "personal" ? loadLegacyMessages() : []);
  return {
    conversations: [migratedConversation],
    activeConversationId: migratedConversation.id
  };
}

function normalizeConversation(value) {
  if (!value || typeof value !== "object") return null;
  const messages = normalizeMessages(value.messages);
  const firstQuestion = messages.find(message => message.role === "user")?.content;
  const createdAt = normalizeTimestamp(value.createdAt);
  const updatedAt = normalizeTimestamp(value.updatedAt, createdAt);

  return {
    id: typeof value.id === "string" && value.id ? value.id : createConversationId(),
    title: typeof value.title === "string" && value.title.trim()
      ? createConversationTitle(value.title)
      : firstQuestion ? createConversationTitle(firstQuestion) : "Cuộc trò chuyện mới",
    messages,
    createdAt,
    updatedAt
  };
}

function normalizeMessages(value) {
  return Array.isArray(value)
    ? value.filter(item =>
        ["user", "assistant"].includes(item?.role)
        && typeof item?.content === "string")
      .slice(-MAX_STORED_MESSAGES)
      .map(item => ({
        role: item.role,
        content: item.content,
        sources: item.role === "assistant" ? normalizeSources(item.sources) : [],
        toolProposal: item.role === "assistant" ? normalizeToolProposal(item.toolProposal) : null,
        toolExecution: item.role === "assistant" ? normalizeToolExecution(item.toolExecution) : null,
        context: item.role === "assistant" ? normalizeContextReport(item.context) : null
      }))
    : [];
}

function normalizeContextReport(value) {
  if (!value || typeof value !== "object") return null;

  const budget = value.budget && typeof value.budget === "object"
    ? {
        maximumCharacters: Math.max(0, Number(value.budget.maximumCharacters) || 0),
        usedCharacters: Math.max(0, Number(value.budget.usedCharacters) || 0),
        memoryCharacters: Math.max(0, Number(value.budget.memoryCharacters) || 0),
        documentCharacters: Math.max(0, Number(value.budget.documentCharacters) || 0),
        taskCharacters: Math.max(0, Number(value.budget.taskCharacters) || 0)
      }
    : null;

  const items = Array.isArray(value.items)
    ? value.items
        .filter(item => item && typeof item === "object")
        .slice(0, 12)
        .map(item => ({
          kind: typeof item.kind === "string" ? item.kind : "",
          id: typeof item.id === "string" ? item.id : "",
          label: typeof item.label === "string" ? item.label : "",
          reason: typeof item.reason === "string" ? item.reason : "",
          score: Number.isFinite(Number(item.score)) ? Number(item.score) : null,
          characterCount: Math.max(0, Number(item.characterCount) || 0)
        }))
    : [];

  return {
    workspaceId: typeof value.workspaceId === "string" ? value.workspaceId : "personal",
    strategy: typeof value.strategy === "string" ? value.strategy : "",
    selectedMemories: Math.max(0, Number(value.selectedMemories) || 0),
    selectedDocuments: Math.max(0, Number(value.selectedDocuments) || 0),
    selectedTasks: Math.max(0, Number(value.selectedTasks) || 0),
    budget,
    items
  };
}


function normalizeSources(value) {
  return Array.isArray(value)
    ? value.filter(source =>
        source
        && typeof source.fileName === "string"
        && Number.isInteger(Number(source.chunkIndex))
        && Number(source.chunkIndex) > 0)
      .slice(0, 5)
      .map(source => ({
        documentId: typeof source.documentId === "string" ? source.documentId : "",
        fileName: source.fileName,
        chunkIndex: Number(source.chunkIndex)
      }))
    : [];
}

function normalizeTimestamp(value, fallback = new Date().toISOString()) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? fallback : date.toISOString();
}

function loadLegacyMessages() {
  try {
    const parsed = JSON.parse(localStorage.getItem(LEGACY_MESSAGES_STORAGE_KEY) || "[]");
    return normalizeMessages(parsed);
  } catch {
    return [];
  }
}

function escapeHtml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function renderMarkdown(markdown) {
  let safe = escapeHtml(markdown);
  const codeBlocks = [];

  safe = safe.replace(/```(?:\w+)?\n([\s\S]*?)```/g, (_, code) => {
    const index = codeBlocks.push(`<pre><code>${code.trim()}</code></pre>`) - 1;
    return `@@CODE_BLOCK_${index}@@`;
  });

  safe = safe
    .replace(/^### (.+)$/gm, "<h3>$1</h3>")
    .replace(/^## (.+)$/gm, "<h2>$1</h2>")
    .replace(/^# (.+)$/gm, "<h1>$1</h1>")
    .replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>")
    .replace(/`([^`]+)`/g, "<code>$1</code>")
    .replace(/^[-*] (.+)$/gm, "<li>$1</li>")
    .replace(/(?:<li>.*<\/li>\n?)+/g, list => `<ul>${list}</ul>`)
    .split(/\n{2,}/)
    .map(block => block.startsWith("<") || block.startsWith("@@CODE_BLOCK_")
      ? block
      : `<p>${block.replaceAll("\n", "<br>")}</p>`)
    .join("");

  codeBlocks.forEach((block, index) => {
    safe = safe.replace(`@@CODE_BLOCK_${index}@@`, block);
  });

  return safe;
}
