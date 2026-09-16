const STORAGE_KEY = "personal-ai-v0.1-messages";
const MAX_STORED_MESSAGES = 40;

const state = {
  messages: loadMessages(),
  busy: false,
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
  sidebar: document.querySelector("#sidebar")
};

initialize();

async function initialize() {
  bindEvents();
  renderConversation();
  await refreshStatus();
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

  elements.clear.addEventListener("click", clearConversation);
  elements.newChat.addEventListener("click", clearConversation);
  elements.menu.addEventListener("click", () => elements.sidebar.classList.toggle("open"));

  document.querySelectorAll("[data-prompt]").forEach(button => {
    button.addEventListener("click", () => {
      elements.input.value = button.dataset.prompt;
      resizeInput();
      elements.form.requestSubmit();
    });
  });
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

  state.messages.push({ role: "user", content });
  trimMessages();
  persistMessages();
  elements.input.value = "";
  resizeInput();
  renderConversation();
  setBusy(true);

  const typingNode = appendTyping();

  try {
    const response = await fetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ messages: state.messages })
    });
    const payload = await response.json().catch(() => ({}));

    if (!response.ok) {
      throw new Error(payload.error || "Không thể nhận câu trả lời từ AI.");
    }

    state.messages.push({ role: "assistant", content: payload.message });
    trimMessages();
    persistMessages();
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
  elements.messages.querySelectorAll(".message-row").forEach(node => node.remove());
  elements.welcome.hidden = state.messages.length > 0;

  state.messages.forEach(message => {
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

function clearConversation() {
  if (state.busy) return;
  state.messages = [];
  localStorage.removeItem(STORAGE_KEY);
  elements.sidebar.classList.remove("open");
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

function trimMessages() {
  if (state.messages.length > MAX_STORED_MESSAGES) {
    state.messages = state.messages.slice(-MAX_STORED_MESSAGES);
  }
}

function persistMessages() {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(state.messages));
}

function loadMessages() {
  try {
    const parsed = JSON.parse(localStorage.getItem(STORAGE_KEY) || "[]");
    return Array.isArray(parsed)
      ? parsed.filter(item =>
          ["user", "assistant"].includes(item?.role)
          && typeof item?.content === "string")
      : [];
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
