(() => {
  const PREFS_KEY = "personal-ai-v0.5.4-knowledge-prefs";
  const WORKSPACE_KEY = "personal-ai-v0.4-workspace";
  const MODES = new Set(["normal", "documents-only"]);
  const nativeFetch = window.fetch.bind(window);

  let prefs = loadPrefs();

  window.fetch = async (input, init = {}) => {
    const url = typeof input === "string" ? input : input?.url || "";
    const method = String(init.method || input?.method || "GET").toUpperCase();

    if (method === "POST" && isChatUrl(url) && typeof init.body === "string") {
      try {
        const payload = JSON.parse(init.body);
        payload.useKnowledge = prefs.useKnowledge;
        payload.knowledgeMode = prefs.useKnowledge ? prefs.knowledgeMode : "normal";
        init = { ...init, body: JSON.stringify(payload) };
      } catch {
        // Leave malformed bodies untouched so the existing API validation can handle them.
      }
    }

    return nativeFetch(input, init);
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeV054, { once: true });
  } else {
    initializeV054();
  }

  function initializeV054() {
    injectStyles();
    updateVersionLabels();
    insertKnowledgeControls();
    bindSourceInteractions();
    hydrateSourceMetadata();

    const messages = document.querySelector("#messages");
    if (messages) {
      const observer = new MutationObserver(() => hydrateSourceMetadata());
      observer.observe(messages, { childList: true, subtree: true });
    }
  }

  function isChatUrl(url) {
    try {
      const parsed = new URL(url, window.location.origin);
      return parsed.pathname === "/api/chat";
    } catch {
      return url === "/api/chat";
    }
  }

  function loadPrefs() {
    try {
      const stored = JSON.parse(localStorage.getItem(PREFS_KEY) || "null");
      return {
        useKnowledge: stored?.useKnowledge !== false,
        knowledgeMode: MODES.has(stored?.knowledgeMode) ? stored.knowledgeMode : "normal"
      };
    } catch {
      return { useKnowledge: true, knowledgeMode: "normal" };
    }
  }

  function persistPrefs() {
    localStorage.setItem(PREFS_KEY, JSON.stringify(prefs));
  }

  function insertKnowledgeControls() {
    const composerWrap = document.querySelector(".composer-wrap");
    const composer = document.querySelector("#chatForm");
    if (!composerWrap || !composer || document.querySelector("#knowledgeChatControls")) return;

    const controls = document.createElement("div");
    controls.className = "knowledge-chat-controls";
    controls.id = "knowledgeChatControls";
    controls.innerHTML = `
      <label class="knowledge-use-toggle" title="Cho phép AI tìm các đoạn liên quan trong kho dữ liệu riêng">
        <input id="useKnowledgeToggle" type="checkbox">
        <span class="knowledge-switch" aria-hidden="true"></span>
        <span class="knowledge-toggle-copy">
          <strong>Dữ liệu riêng</strong>
          <small id="knowledgeToggleStatus"></small>
        </span>
      </label>
      <div class="knowledge-mode-group" aria-label="Chế độ trả lời">
        <button type="button" data-knowledge-mode="normal">Chat thông thường</button>
        <button type="button" data-knowledge-mode="documents-only">Chỉ theo tài liệu</button>
      </div>`;

    composerWrap.insertBefore(controls, composer);

    const toggle = controls.querySelector("#useKnowledgeToggle");
    toggle.checked = prefs.useKnowledge;
    toggle.addEventListener("change", () => {
      prefs.useKnowledge = toggle.checked;
      persistPrefs();
      syncControls();
    });

    controls.querySelectorAll("[data-knowledge-mode]").forEach(button => {
      button.addEventListener("click", () => {
        if (!prefs.useKnowledge) return;
        prefs.knowledgeMode = button.dataset.knowledgeMode;
        persistPrefs();
        syncControls();
      });
    });

    syncControls();
  }

  function syncControls() {
    const toggle = document.querySelector("#useKnowledgeToggle");
    const status = document.querySelector("#knowledgeToggleStatus");
    if (toggle) toggle.checked = prefs.useKnowledge;

    if (status) {
      status.textContent = prefs.useKnowledge
        ? (prefs.knowledgeMode === "documents-only" ? "Chỉ dùng nguồn đã tải" : "Tự tìm khi có liên quan")
        : "Đang tắt";
    }

    document.querySelectorAll("[data-knowledge-mode]").forEach(button => {
      const active = prefs.useKnowledge && button.dataset.knowledgeMode === prefs.knowledgeMode;
      button.classList.toggle("active", active);
      button.setAttribute("aria-pressed", String(active));
      button.disabled = !prefs.useKnowledge;
    });
  }

  function updateVersionLabels() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = "Phiên bản 0.5.4.3";

    const introTitle = document.querySelector(".knowledge-intro strong");
    if (introTitle) introTitle.textContent = "Hỏi đáp có nguồn v0.5.4.3";

    document.querySelectorAll(".security-copy").forEach(node => {
      node.innerHTML = node.innerHTML.replace(/v0\.5\.(?:3|4(?:\.\d+)?)/g, "v0.5.4.3");
    });
  }

  function bindSourceInteractions() {
    document.addEventListener("click", event => {
      const source = event.target.closest?.(".message-source");
      if (source) openSource(source);
    });

    document.addEventListener("keydown", event => {
      const source = event.target.closest?.(".message-source");
      if (!source || !["Enter", " "].includes(event.key)) return;
      event.preventDefault();
      openSource(source);
    });
  }

  function hydrateSourceMetadata() {
    const workspace = readWorkspace();
    const conversation = workspace?.conversations?.find(
      item => item.id === workspace.activeConversationId);
    if (!conversation || !Array.isArray(conversation.messages)) return;

    const rows = [...document.querySelectorAll("#messages .message-row")];
    rows.forEach((row, messageIndex) => {
      const message = conversation.messages[messageIndex];
      if (!message || message.role !== "assistant" || !Array.isArray(message.sources)) return;

      row.querySelectorAll(".message-source").forEach((chip, sourceIndex) => {
        const source = message.sources[sourceIndex];
        if (!source) return;
        chip.dataset.documentId = source.documentId || "";
        chip.dataset.chunkIndex = String(source.chunkIndex || "");
        chip.setAttribute("role", "button");
        chip.setAttribute("tabindex", "0");
        chip.setAttribute("aria-label", `Xem nguyên văn ${source.fileName}, đoạn ${source.chunkIndex}`);
        chip.title = "Bấm để xem nguyên văn đoạn đã dùng";
      });
    });
  }

  function readWorkspace() {
    try {
      return JSON.parse(localStorage.getItem(WORKSPACE_KEY) || "null");
    } catch {
      return null;
    }
  }

  async function openSource(chip) {
    const documentId = chip.dataset.documentId;
    const chunkIndex = Number(chip.dataset.chunkIndex);
    if (!documentId || !Number.isInteger(chunkIndex) || chunkIndex <= 0) {
      hydrateSourceMetadata();
    }

    const resolvedDocumentId = chip.dataset.documentId;
    const resolvedChunkIndex = Number(chip.dataset.chunkIndex);
    if (!resolvedDocumentId || !Number.isInteger(resolvedChunkIndex) || resolvedChunkIndex <= 0) {
      return;
    }

    const section = chip.closest(".message-sources");
    if (!section) return;
    const sourceKey = `${resolvedDocumentId}:${resolvedChunkIndex}`;
    const existing = section.querySelector(".source-original-preview");
    if (existing?.dataset.sourceKey === sourceKey) {
      existing.hidden = !existing.hidden;
      return;
    }

    existing?.remove();
    const preview = document.createElement("div");
    preview.className = "source-original-preview";
    preview.dataset.sourceKey = sourceKey;
    preview.textContent = "Đang tải nguyên văn nguồn…";
    section.appendChild(preview);

    try {
      const response = await nativeFetch(
        `/api/knowledge/documents/${encodeURIComponent(resolvedDocumentId)}/chunks/${resolvedChunkIndex}`,
        { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không đọc được đoạn nguồn.");

      preview.replaceChildren();
      const header = document.createElement("div");
      header.className = "source-original-heading";
      header.textContent = `${payload.fileName} · Đoạn ${payload.chunkIndex} · nguyên văn`;
      const content = document.createElement("pre");
      content.textContent = payload.content || "";
      preview.append(header, content);
    } catch (error) {
      preview.textContent = error.message || "Không đọc được đoạn nguồn.";
      preview.classList.add("error");
    }
  }

  function injectStyles() {
    if (document.querySelector("#v054Styles")) return;
    const style = document.createElement("style");
    style.id = "v054Styles";
    style.textContent = `
      .knowledge-chat-controls{display:flex;align-items:center;justify-content:space-between;gap:12px;max-width:860px;margin:0 auto 8px;padding:0 4px;font-size:12px;color:var(--muted,#667085)}
      .knowledge-use-toggle{display:flex;align-items:center;gap:8px;cursor:pointer;user-select:none}
      .knowledge-use-toggle input{position:absolute;opacity:0;pointer-events:none}
      .knowledge-switch{width:34px;height:20px;border-radius:999px;background:#d0d5dd;position:relative;transition:.2s ease;flex:none}
      .knowledge-switch::after{content:"";position:absolute;width:14px;height:14px;border-radius:50%;background:white;top:3px;left:3px;box-shadow:0 1px 3px rgba(0,0,0,.25);transition:.2s ease}
      .knowledge-use-toggle input:checked + .knowledge-switch{background:#3157d5}
      .knowledge-use-toggle input:checked + .knowledge-switch::after{transform:translateX(14px)}
      .knowledge-toggle-copy{display:flex;align-items:baseline;gap:6px;white-space:nowrap}
      .knowledge-toggle-copy strong{color:var(--text,#202124);font-size:12px}
      .knowledge-toggle-copy small{font-size:11px;color:inherit}
      .knowledge-mode-group{display:flex;gap:3px;padding:3px;border:1px solid #e1e5eb;border-radius:10px;background:rgba(255,255,255,.72)}
      .knowledge-mode-group button{border:0;background:transparent;border-radius:7px;padding:6px 9px;font:inherit;color:inherit;cursor:pointer;transition:.15s ease}
      .knowledge-mode-group button.active{background:#eef2ff;color:#2742a8;font-weight:700;box-shadow:0 1px 2px rgba(16,24,40,.08)}
      .knowledge-mode-group button:disabled{cursor:not-allowed;opacity:.45}
      .message-source{cursor:pointer;transition:.15s ease;outline:none}
      .message-source:hover,.message-source:focus-visible{transform:translateY(-1px);box-shadow:0 0 0 2px rgba(49,87,213,.16)}
      .source-original-preview{margin-top:10px;border:1px solid #dbe2f1;border-radius:10px;background:#f8faff;overflow:hidden;color:#1f2937!important;opacity:1!important}
      .source-original-preview.error{padding:10px 12px;color:#b42318!important;background:#fff6f5;border-color:#f4c7c3}
      .source-original-heading{padding:8px 11px;border-bottom:1px solid #e5eaf4;font-size:11px;font-weight:700;color:#1f2937!important;background:#f1f4fb;opacity:1!important}
      .source-original-preview pre{margin:0;padding:11px 12px;white-space:pre-wrap;word-break:break-word;font:12px/1.55 ui-monospace,SFMono-Regular,Consolas,monospace;max-height:300px;overflow:auto;background:#fff;color:#1f2937!important;-webkit-text-fill-color:#1f2937!important;opacity:1!important}
      @media (max-width:700px){.knowledge-chat-controls{align-items:flex-start;flex-direction:column;gap:7px}.knowledge-mode-group{width:100%}.knowledge-mode-group button{flex:1}.knowledge-toggle-copy small{display:none}}
    `;
    document.head.appendChild(style);
  }
})();
