const LEGACY_MESSAGES_STORAGE_KEY = "personal-ai-v0.1-messages";
const WORKSPACE_STORAGE_KEY = "personal-ai-v0.4-workspace";
const MAX_STORED_MESSAGES = 40;
const MAX_TITLE_LENGTH = 42;
const CUSTOM_MODEL_VALUE = "__custom__";
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
  testConnection: document.querySelector("#testConnectionButton")
};

initialize();

async function initialize() {
  bindEvents();
  persistWorkspace();
  localStorage.removeItem(LEGACY_MESSAGES_STORAGE_KEY);
  renderConversationList();
  renderConversation();
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
  elements.provider.addEventListener("change", () => {
    const selectedProvider = elements.provider.value;
    populateModelOptions(selectedProvider, DEFAULT_MODELS[selectedProvider]);
    elements.apiKey.value = "";
    elements.apiKeyHint.textContent = "Để trống để giữ API key đã lưu cho nhà cung cấp này.";
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
      ? `Đã có API key: ${settings.maskedApiKey}. Để trống để giữ nguyên.`
      : "Chưa có API key. Hãy nhập key để sử dụng nhà cung cấp này.";
    setSettingsFeedback("");
    elements.provider.focus();
  } catch (error) {
    setSettingsFeedback(error.message, "error");
  }
}

async function saveAiSettings(testAfterSave) {
  if (state.settingsBusy) return;
  if (!elements.settingsForm.reportValidity()) return;
  setSettingsBusy(true);
  setSettingsFeedback(testAfterSave ? "Đang lưu và kiểm tra kết nối…" : "Đang lưu cài đặt…");

  try {
    const response = await fetch("/api/settings/ai", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        provider: elements.provider.value,
        model: getSelectedModel(),
        apiKey: elements.apiKey.value.trim()
      })
    });
    const settings = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(settings.error || "Không lưu được cài đặt AI.");

    elements.apiKey.value = "";
    elements.apiKeyHint.textContent = settings.hasApiKey
      ? `Đã có API key: ${settings.maskedApiKey}. Để trống để giữ nguyên.`
      : "Chưa có API key. Hãy nhập key để sử dụng nhà cung cấp này.";

    if (testAfterSave) {
      const testResponse = await fetch("/api/settings/ai/test", { method: "POST" });
      const testResult = await testResponse.json().catch(() => ({}));
      if (!testResponse.ok || !testResult.success) {
        throw new Error(testResult.message || "Không kết nối được với AI.");
      }
      setSettingsFeedback(
        `Kết nối thành công với ${settings.provider} · ${settings.model}.`,
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
  customOption.textContent = "Model tùy chỉnh…";
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
  elements.toggleApiKey.setAttribute("aria-label", shouldShow ? "Ẩn API key" : "Hiện API key");
  elements.toggleApiKey.setAttribute("aria-pressed", String(shouldShow));
}

function resetApiKeyVisibility() {
  elements.apiKey.type = "password";
  elements.toggleApiKey.textContent = "Hiện";
  elements.toggleApiKey.setAttribute("aria-label", "Hiện API key");
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
  if (!window.confirm(`Xóa tài liệu “${document.fileName}” khỏi kho dữ liệu?`)) return;

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
    `${formatFileSize(document.fileSize)} · ${Number(document.characterCount || 0).toLocaleString("vi-VN")} ký tự · ${formatDocumentDate(document.createdAt)}`);
  details.append(name, metadata);

  const status = documentElement("span", "knowledge-status", document.status === "ready" ? "Sẵn sàng" : "Đang xử lý");
  const deleteButton = documentElement("button", "knowledge-delete", "Xóa");
  deleteButton.type = "button";
  deleteButton.setAttribute("aria-label", `Xóa ${document.fileName}`);
  deleteButton.addEventListener("click", () => deleteKnowledgeDocument(document));

  item.append(type, details, status, deleteButton);
  return item;
}

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
}

function setKnowledgeFeedback(message, type = "") {
  elements.knowledgeFeedback.textContent = message;
  elements.knowledgeFeedback.className = `settings-feedback knowledge-feedback ${type}`.trim();
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
      ? `Sẵn sàng · ${status.provider} · ${status.model}`
      : "Chưa cấu hình API key";
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
      body: JSON.stringify({ messages: conversation.messages })
    });
    const payload = await response.json().catch(() => ({}));

    if (!response.ok) {
      throw new Error(payload.error || "Không thể nhận câu trả lời từ AI.");
    }

    conversation.messages.push({ role: "assistant", content: payload.message });
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
    elements.messages.appendChild(createMessageNode(message.role, message.content));
  });

  scrollToBottom();
}

function createMessageNode(role, content) {
  const row = document.createElement("article");
  row.className = `message-row ${role}`;

  const avatar = document.createElement("div");
  avatar.className = "avatar";
  avatar.textContent = role === "user" ? "B" : "AI";

  const bubble = document.createElement("div");
  bubble.className = "bubble";
  bubble.innerHTML = role === "assistant" ? renderMarkdown(content) : escapeHtml(content).replaceAll("\n", "<br>");

  row.append(avatar, bubble);
  return row;
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

function clearActiveConversation() {
  if (state.busy) return;
  const conversation = getActiveConversation();
  if (conversation.messages.length > 0
      && !window.confirm("Xóa toàn bộ nội dung trong cuộc trò chuyện này?")) {
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

function renameConversation(conversationId) {
  if (state.busy) return;
  const conversation = state.conversations.find(item => item.id === conversationId);
  if (!conversation) return;

  const title = window.prompt("Đổi tên cuộc trò chuyện:", conversation.title)?.trim();
  if (!title) return;

  conversation.title = createConversationTitle(title);
  touchConversation(conversation);
  persistWorkspace();
  renderConversationList();
}

function deleteConversation(conversationId) {
  if (state.busy) return;
  const conversation = state.conversations.find(item => item.id === conversationId);
  if (!conversation) return;
  if (!window.confirm(`Xóa cuộc trò chuyện “${conversation.title}”?`)) return;

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
  try {
    const savedWorkspace = JSON.parse(localStorage.getItem(WORKSPACE_STORAGE_KEY) || "null");
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
    // Ignore damaged browser storage and import the previous format below.
  }

  const migratedConversation = createConversation(loadLegacyMessages());
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
      .map(item => ({ role: item.role, content: item.content }))
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
