(() => {
  const VERSION = "0.6.1";
  const WORKSPACE_KEY = "personal-ai-v0.4-workspace";
  const MEMORY_PREFS_KEY = "personal-ai-v0.6-memory-prefs";
  const KNOWLEDGE_PREFS_KEY = "personal-ai-v0.5.4-knowledge-prefs";
  const SUGGESTIONS_KEY = "personal-ai-v0.6.1-memory-suggestions";
  const upstreamFetch = window.fetch.bind(window);

  window.fetch = async (input, init = {}) => {
    const url = typeof input === "string" ? input : input?.url || "";
    const method = String(init.method || input?.method || "GET").toUpperCase();
    let suggestionContext = null;

    if (method === "POST" && isChatUrl(url) && typeof init.body === "string") {
      try {
        const payload = JSON.parse(init.body);
        const messages = Array.isArray(payload.messages) ? payload.messages : [];
        const lastUserIndex = findLastUserIndex(messages);
        const workspace = readWorkspace();
        const candidate = lastUserIndex >= 0
          ? buildMemoryCandidate(messages[lastUserIndex]?.content || "")
          : null;

        if (candidate
            && workspace?.activeConversationId
            && memorySuggestionsEnabled()
            && !documentsOnlyEnabled()) {
          suggestionContext = {
            conversationId: workspace.activeConversationId,
            messageIndex: messages.length,
            candidate
          };
        }
      } catch {
        // Leave malformed chat payloads to the existing validation layer.
      }
    }

    const response = await upstreamFetch(input, init);
    if (suggestionContext && response.ok) {
      void persistSuggestionAfterSuccess(suggestionContext);
    }
    return response;
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeV061, { once: true });
  } else {
    initializeV061();
  }

  function initializeV061() {
    updateVersionLabels();
    injectStyles();
    bindSuggestionActions();
    pruneSuggestionStore();
    hydrateSuggestions();

    const messages = document.querySelector("#messages");
    if (messages) {
      const observer = new MutationObserver(() => hydrateSuggestions());
      observer.observe(messages, { childList: true, subtree: true });
    }
  }

  function isChatUrl(url) {
    try {
      return new URL(url, window.location.origin).pathname === "/api/chat";
    } catch {
      return url === "/api/chat";
    }
  }

  function findLastUserIndex(messages) {
    for (let index = messages.length - 1; index >= 0; index -= 1) {
      if (String(messages[index]?.role || "").toLowerCase() === "user") return index;
    }
    return -1;
  }

  function memorySuggestionsEnabled() {
    try {
      const prefs = JSON.parse(localStorage.getItem(MEMORY_PREFS_KEY) || "null");
      return prefs?.useMemory !== false;
    } catch {
      return true;
    }
  }

  function documentsOnlyEnabled() {
    try {
      const prefs = JSON.parse(localStorage.getItem(KNOWLEDGE_PREFS_KEY) || "null");
      return prefs?.useKnowledge !== false && prefs?.knowledgeMode === "documents-only";
    } catch {
      return false;
    }
  }

  function buildMemoryCandidate(rawContent) {
    const text = String(rawContent || "")
      .replace(/\s+/g, " ")
      .trim();

    if (text.length < 6 || text.length > 300 || /[?？]\s*$/.test(text)) return null;
    if (containsSecretLikeData(text) || looksTemporary(text)) return null;

    const explicitRemember = text.match(/^(?:hãy\s+)?nhớ(?:\s+rằng)?\s*[:,]?\s*(.+)$/iu);
    if (explicitRemember?.[1]) {
      const content = explicitRemember[1].trim();
      if (content.length < 3) return null;
      return { kind: classifyKind(content), content };
    }

    const lower = text.toLocaleLowerCase("vi-VN");
    if (/(?:^|\s)(?:từ giờ|từ nay|hãy luôn|đừng bao giờ|luôn trả lời|khi trả lời[^.]{0,80}\bhãy\b)/iu.test(lower)) {
      return { kind: "rule", content: text };
    }

    if (/(?:^|\s)(?:tôi thích|tôi không thích|tôi ghét|tôi ưu tiên|phong cách tôi thích|cách trả lời tôi thích)/iu.test(lower)) {
      return { kind: "preference", content: text };
    }

    if (/(?:^|\s)(?:tên tôi là|tôi tên|tôi sinh(?: năm| ngày)?|tôi sống(?: ở)?|tôi ở|tôi làm việc(?: tại| ở)?|tôi làm nghề|nghề của tôi|công việc của tôi|mục tiêu dài hạn của tôi|của tôi là)/iu.test(lower)) {
      return { kind: "fact", content: text };
    }

    return null;
  }

  function classifyKind(content) {
    const lower = content.toLocaleLowerCase("vi-VN");
    if (/(?:từ giờ|từ nay|hãy luôn|đừng bao giờ|luôn trả lời|khi trả lời)/iu.test(lower)) return "rule";
    if (/(?:tôi thích|tôi không thích|tôi ghét|tôi ưu tiên|phong cách)/iu.test(lower)) return "preference";
    return "fact";
  }

  function containsSecretLikeData(text) {
    return /(?:api\s*key|mật khẩu|password|passcode|otp|access\s*token|refresh\s*token|private\s*key|secret\s*key|cvv|cvc|số thẻ|thẻ tín dụng)/iu.test(text);
  }

  function looksTemporary(text) {
    return /(?:\bhôm nay\b|\bngày mai\b|\btối nay\b|\blát nữa\b|\bbây giờ\b|\btuần này\b)/iu.test(text);
  }

  async function persistSuggestionAfterSuccess(context) {
    if (await alreadyRemembered(context.candidate.content)) return;

    const suggestions = readSuggestions();
    const duplicate = suggestions.some(item =>
      item.conversationId === context.conversationId
      && normalize(item.content) === normalize(context.candidate.content));
    if (duplicate) return;

    suggestions.push({
      id: crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`,
      conversationId: context.conversationId,
      messageIndex: context.messageIndex,
      kind: context.candidate.kind,
      content: context.candidate.content,
      createdAt: new Date().toISOString()
    });
    writeSuggestions(suggestions);
    setTimeout(hydrateSuggestions, 0);
  }

  async function alreadyRemembered(content) {
    try {
      const response = await upstreamFetch("/api/memory", { cache: "no-store" });
      if (!response.ok) return false;
      const memories = await response.json().catch(() => []);
      return Array.isArray(memories)
        && memories.some(memory => normalize(memory.content) === normalize(content));
    } catch {
      return false;
    }
  }

  function readSuggestions() {
    try {
      const parsed = JSON.parse(localStorage.getItem(SUGGESTIONS_KEY) || "[]");
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  }

  function writeSuggestions(suggestions) {
    localStorage.setItem(SUGGESTIONS_KEY, JSON.stringify(suggestions));
  }

  function removeSuggestion(id) {
    writeSuggestions(readSuggestions().filter(item => item.id !== id));
  }

  function pruneSuggestionStore() {
    const workspace = readWorkspace();
    const validConversationIds = new Set(
      Array.isArray(workspace?.conversations)
        ? workspace.conversations.map(item => item.id)
        : []);
    const cutoff = Date.now() - (30 * 24 * 60 * 60 * 1000);
    const pruned = readSuggestions().filter(item =>
      validConversationIds.has(item.conversationId)
      && (!item.createdAt || Date.parse(item.createdAt) >= cutoff));
    writeSuggestions(pruned);
  }

  function readWorkspace() {
    try {
      return JSON.parse(localStorage.getItem(WORKSPACE_KEY) || "null");
    } catch {
      return null;
    }
  }

  function hydrateSuggestions() {
    const workspace = readWorkspace();
    if (!workspace?.activeConversationId) return;

    document.querySelectorAll(".memory-suggestion").forEach(node => node.remove());
    const rows = [...document.querySelectorAll("#messages .message-row")];
    const activeSuggestions = readSuggestions().filter(
      item => item.conversationId === workspace.activeConversationId);

    activeSuggestions.forEach(suggestion => {
      const row = rows[suggestion.messageIndex];
      if (!row?.classList.contains("assistant")) return;
      const bubble = row.querySelector(".bubble");
      if (!bubble) return;
      bubble.appendChild(createSuggestionNode(suggestion));
    });
  }

  function createSuggestionNode(suggestion) {
    const card = document.createElement("section");
    card.className = "memory-suggestion";
    card.dataset.memorySuggestionId = suggestion.id;

    const header = document.createElement("div");
    header.className = "memory-suggestion-heading";
    const title = document.createElement("strong");
    title.textContent = "Có thể lưu vào trí nhớ";
    const badge = document.createElement("span");
    badge.textContent = kindLabel(suggestion.kind);
    header.append(title, badge);

    const content = document.createElement("p");
    content.textContent = suggestion.content;

    const actions = document.createElement("div");
    actions.className = "memory-suggestion-actions";
    const dismiss = document.createElement("button");
    dismiss.type = "button";
    dismiss.className = "memory-suggestion-dismiss";
    dismiss.dataset.memorySuggestionDismiss = suggestion.id;
    dismiss.textContent = "Bỏ qua";
    const save = document.createElement("button");
    save.type = "button";
    save.className = "memory-suggestion-save";
    save.dataset.memorySuggestionSave = suggestion.id;
    save.textContent = "Lưu vào trí nhớ";
    actions.append(dismiss, save);

    card.append(header, content, actions);
    return card;
  }

  function bindSuggestionActions() {
    document.addEventListener("click", async event => {
      const saveButton = event.target.closest?.("[data-memory-suggestion-save]");
      if (saveButton) {
        await saveSuggestedMemory(saveButton.dataset.memorySuggestionSave, saveButton);
        return;
      }

      const dismissButton = event.target.closest?.("[data-memory-suggestion-dismiss]");
      if (dismissButton) {
        removeSuggestion(dismissButton.dataset.memorySuggestionDismiss);
        dismissButton.closest(".memory-suggestion")?.remove();
      }
    });
  }

  async function saveSuggestedMemory(id, button) {
    const suggestion = readSuggestions().find(item => item.id === id);
    if (!suggestion || !button) return;

    button.disabled = true;
    button.textContent = "Đang lưu…";
    try {
      const response = await upstreamFetch("/api/memory", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ kind: suggestion.kind, content: suggestion.content })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok && response.status !== 400) {
        throw new Error(payload.error || "Không lưu được trí nhớ.");
      }

      removeSuggestion(id);
      const card = button.closest(".memory-suggestion");
      if (card) {
        card.classList.add("saved");
        card.replaceChildren();
        const saved = document.createElement("strong");
        saved.textContent = response.ok ? "✓ Đã lưu vào trí nhớ" : "✓ Nội dung này đã có trong trí nhớ";
        card.appendChild(saved);
        setTimeout(() => card.remove(), 1400);
      }
      document.querySelector("#memoryRefreshButton")?.click();
    } catch (error) {
      button.disabled = false;
      button.textContent = "Lưu vào trí nhớ";
      button.title = error.message || "Không lưu được trí nhớ.";
    }
  }

  function kindLabel(kind) {
    if (kind === "rule") return "Quy tắc";
    if (kind === "preference") return "Sở thích";
    return "Thông tin";
  }

  function normalize(value) {
    return String(value || "").trim().replace(/\s+/g, " ").toLocaleLowerCase("vi-VN");
  }

  function updateVersionLabels() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;

    const memoryIntro = document.querySelector(".memory-intro strong");
    if (memoryIntro) memoryIntro.textContent = `Trí nhớ v${VERSION}`;

    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Hỏi đáp có nguồn v${VERSION}`;

    const memoryHint = document.querySelector(".memory-form-actions .field-hint");
    if (memoryHint) {
      memoryHint.textContent = "v0.6.1 có thể gợi ý trí nhớ từ câu nói rõ ràng; chỉ lưu khi bạn xác nhận.";
    }

    document.querySelectorAll(".security-copy").forEach(node => {
      node.innerHTML = node.innerHTML
        .replace(/v0\.5\.[0-9.]+/g, `v${VERSION}`)
        .replace(/v0\.6\.0/g, `v${VERSION}`);
    });
  }

  function injectStyles() {
    if (document.querySelector("#v061Styles")) return;
    const style = document.createElement("style");
    style.id = "v061Styles";
    style.textContent = `
      .memory-dialog{overflow-x:hidden!important;max-width:100vw!important}
      .memory-card{box-sizing:border-box!important;width:min(620px,calc(100vw - 48px))!important;max-width:calc(100vw - 48px)!important;overflow-y:auto!important;overflow-x:hidden!important}
      .memory-card *,.memory-card *::before,.memory-card *::after{box-sizing:border-box}
      .memory-card select,.memory-card textarea,.memory-card input{max-width:100%}
      .memory-form-actions,.memory-dialog-toggle,.memory-intro,.memory-list-header,.memory-row{min-width:0;max-width:100%}
      .memory-suggestion{margin-top:12px;padding:11px 12px;border:1px solid #dce6d4;border-radius:10px;background:#f8fbf1;color:#26332d}
      .memory-suggestion-heading{display:flex;align-items:center;justify-content:space-between;gap:10px}
      .memory-suggestion-heading strong{font-size:12px}
      .memory-suggestion-heading span{padding:3px 7px;border-radius:999px;background:#eaf3df;color:#4b633d;font-size:10px;font-weight:700;white-space:nowrap}
      .memory-suggestion p{margin:7px 0 9px;font-size:12px;line-height:1.45;white-space:pre-wrap;word-break:break-word}
      .memory-suggestion-actions{display:flex;justify-content:flex-end;gap:7px}
      .memory-suggestion-actions button{border-radius:8px;padding:6px 9px;font:inherit;font-size:11px;cursor:pointer}
      .memory-suggestion-dismiss{border:1px solid #d7ddd3;background:#fff;color:#667085}
      .memory-suggestion-save{border:0;background:#c8ff2e;color:#17251c;font-weight:700}
      .memory-suggestion-save:disabled{opacity:.65;cursor:wait}
      .memory-suggestion.saved{padding:9px 11px;background:#f3fae8;color:#365126}
      @media(max-width:520px){.memory-card{width:calc(100vw - 24px)!important;max-width:calc(100vw - 24px)!important}.memory-suggestion-heading{align-items:flex-start;flex-direction:column}.memory-suggestion-actions{justify-content:stretch}.memory-suggestion-actions button{flex:1}}
    `;
    document.head.appendChild(style);
  }
})();
