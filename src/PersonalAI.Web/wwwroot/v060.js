(() => {
  const PREFS_KEY = "personal-ai-v0.6-memory-prefs";
  const nativeFetch = window.fetch.bind(window);
  let prefs = loadPrefs();
  let memories = [];

  window.fetch = async (input, init = {}) => {
    const url = typeof input === "string" ? input : input?.url || "";
    const method = String(init.method || input?.method || "GET").toUpperCase();

    if (method === "POST" && isChatUrl(url) && typeof init.body === "string") {
      try {
        const payload = JSON.parse(init.body);
        payload.useMemory = prefs.useMemory;
        init = { ...init, body: JSON.stringify(payload) };
      } catch {
        // Keep the existing request untouched if another layer supplied malformed JSON.
      }
    }

    return nativeFetch(input, init);
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeV060, { once: true });
  } else {
    initializeV060();
  }

  function initializeV060() {
    injectStyles();
    updateVersionLabels();
    insertMemoryToggle();
    insertMemoryButton();
    insertMemoryDialog();
    bindMemoryEvents();
    syncMemoryToggle();
    loadMemories();
  }

  function isChatUrl(url) {
    try {
      return new URL(url, window.location.origin).pathname === "/api/chat";
    } catch {
      return url === "/api/chat";
    }
  }

  function loadPrefs() {
    try {
      const stored = JSON.parse(localStorage.getItem(PREFS_KEY) || "null");
      return { useMemory: stored?.useMemory !== false };
    } catch {
      return { useMemory: true };
    }
  }

  function persistPrefs() {
    localStorage.setItem(PREFS_KEY, JSON.stringify(prefs));
  }

  function updateVersionLabels() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = "Phiên bản 0.6.0";

    const introTitle = document.querySelector(".knowledge-intro strong");
    if (introTitle) introTitle.textContent = "Hỏi đáp có nguồn v0.6.0";

    document.querySelectorAll(".security-copy").forEach(node => {
      node.innerHTML = node.innerHTML.replace(/v0\.5\.[0-9.]+/g, "v0.6.0");
    });
  }

  function insertMemoryToggle() {
    const controls = document.querySelector("#knowledgeChatControls");
    const modeGroup = controls?.querySelector(".knowledge-mode-group");
    if (!controls || !modeGroup || document.querySelector("#useMemoryToggle")) return;

    const label = document.createElement("label");
    label.className = "memory-use-toggle";
    label.title = "Cho phép AI sử dụng các trí nhớ cá nhân phù hợp với câu hỏi";
    label.innerHTML = `
      <input id="useMemoryToggle" type="checkbox">
      <span class="memory-switch" aria-hidden="true"></span>
      <span class="memory-toggle-copy">
        <strong>Trí nhớ</strong>
        <small id="memoryToggleStatus"></small>
      </span>`;
    controls.insertBefore(label, modeGroup);
  }

  function insertMemoryButton() {
    const tools = document.querySelector(".sidebar-tools");
    const settingsButton = document.querySelector("#settingsButton");
    if (!tools || document.querySelector("#memoryButton")) return;

    const button = document.createElement("button");
    button.className = "settings-button";
    button.id = "memoryButton";
    button.type = "button";
    button.innerHTML = `
      <span aria-hidden="true">◇</span>
      <span>Trí nhớ</span>
      <span class="tool-count" id="memorySidebarCount" aria-label="Số trí nhớ">0</span>`;
    tools.insertBefore(button, settingsButton || null);
  }

  function insertMemoryDialog() {
    if (document.querySelector("#memoryDialog")) return;

    const dialog = document.createElement("dialog");
    dialog.className = "settings-dialog memory-dialog";
    dialog.id = "memoryDialog";
    dialog.setAttribute("aria-labelledby", "memoryTitle");
    dialog.innerHTML = `
      <div class="settings-card memory-card">
        <header class="settings-header">
          <div>
            <p class="eyebrow">DỮ LIỆU CÁ NHÂN TRÊN MÁY NÀY</p>
            <h2 id="memoryTitle">Trí nhớ cá nhân</h2>
          </div>
          <button class="dialog-close" id="memoryCloseButton" type="button" aria-label="Đóng">×</button>
        </header>

        <div class="memory-intro">
          <div class="memory-intro-icon" aria-hidden="true">◇</div>
          <div>
            <strong>Trí nhớ v0.6.0</strong>
            <p>Bạn chủ động lưu điều muốn AI nhớ. AI chỉ dùng các trí nhớ phù hợp và bỏ qua hoàn toàn khi ở chế độ “Chỉ theo tài liệu”.</p>
          </div>
        </div>

        <label class="memory-dialog-toggle">
          <input id="memoryDialogToggle" type="checkbox">
          <span>
            <strong>Dùng trí nhớ khi trò chuyện</strong>
            <small>Có thể tắt bất kỳ lúc nào mà không xóa dữ liệu đã lưu.</small>
          </span>
        </label>

        <form id="memoryForm" class="memory-form">
          <label class="field-label" for="memoryKind">Loại trí nhớ</label>
          <select id="memoryKind">
            <option value="fact">Thông tin</option>
            <option value="preference">Sở thích</option>
            <option value="rule">Quy tắc</option>
          </select>

          <label class="field-label" for="memoryContent">Nội dung muốn AI nhớ</label>
          <textarea id="memoryContent" rows="3" maxlength="1000" required placeholder="Ví dụ: Tên tôi là Nguyễn Mạnh Hùng."></textarea>
          <div class="memory-form-actions">
            <span class="field-hint">v0.6.0 chưa tự lưu từ hội thoại; chỉ lưu khi bạn bấm nút.</span>
            <button class="primary-button" id="memorySaveButton" type="submit">Lưu trí nhớ</button>
          </div>
        </form>

        <div class="settings-feedback" id="memoryFeedback" role="status" aria-live="polite"></div>

        <div class="memory-list-header">
          <div>
            <strong>Đã lưu</strong>
            <span id="memorySummary">0 trí nhớ</span>
          </div>
          <button class="icon-refresh" id="memoryRefreshButton" type="button" aria-label="Tải lại" title="Tải lại">↻</button>
        </div>

        <div class="memory-list" id="memoryList" role="list"></div>
        <div class="memory-empty" id="memoryEmpty">
          <span aria-hidden="true">◇</span>
          <strong>Chưa có trí nhớ nào</strong>
          <p>Hãy lưu một thông tin, sở thích hoặc quy tắc để kiểm thử trí nhớ cá nhân.</p>
        </div>

        <p class="security-copy">
          Trí nhớ được mã hóa và lưu ngoài thư mục Git tại <code>%LOCALAPPDATA%\\PersonalAI\\Memory</code>.
          Nội dung chỉ được giải mã trong ứng dụng trên máy này khi cần sử dụng.
        </p>
      </div>`;
    document.body.appendChild(dialog);
  }

  function bindMemoryEvents() {
    document.querySelector("#memoryButton")?.addEventListener("click", async () => {
      await loadMemories();
      document.querySelector("#memoryDialog")?.showModal();
    });

    document.querySelector("#memoryCloseButton")?.addEventListener("click", () => {
      document.querySelector("#memoryDialog")?.close();
    });

    document.querySelector("#memoryDialog")?.addEventListener("click", event => {
      if (event.target === event.currentTarget) event.currentTarget.close();
    });

    document.querySelector("#useMemoryToggle")?.addEventListener("change", event => {
      prefs.useMemory = event.target.checked;
      persistPrefs();
      syncMemoryToggle();
    });

    document.querySelector("#memoryDialogToggle")?.addEventListener("change", event => {
      prefs.useMemory = event.target.checked;
      persistPrefs();
      syncMemoryToggle();
    });

    document.querySelector("#memoryRefreshButton")?.addEventListener("click", loadMemories);
    document.querySelector("#memoryForm")?.addEventListener("submit", saveMemory);

    document.querySelector("#memoryList")?.addEventListener("click", async event => {
      const button = event.target.closest?.("[data-memory-delete]");
      if (!button) return;
      const memoryId = button.dataset.memoryDelete;
      if (!memoryId || !(await window.PersonalAiUi.confirm("Xóa trí nhớ này?", { title: "Xác nhận xóa trí nhớ", confirmText: "Xóa", danger: true }))) return;
      await deleteMemory(memoryId);
    });
  }

  function syncMemoryToggle() {
    const composerToggle = document.querySelector("#useMemoryToggle");
    const dialogToggle = document.querySelector("#memoryDialogToggle");
    const status = document.querySelector("#memoryToggleStatus");
    if (composerToggle) composerToggle.checked = prefs.useMemory;
    if (dialogToggle) dialogToggle.checked = prefs.useMemory;
    if (status) status.textContent = prefs.useMemory ? "Bật" : "Tắt";
  }

  async function loadMemories() {
    try {
      const response = await nativeFetch("/api/memory", { cache: "no-store" });
      const payload = await response.json().catch(() => []);
      if (!response.ok) throw new Error(payload.error || "Không tải được trí nhớ.");
      memories = Array.isArray(payload) ? payload : [];
      renderMemories();
      setFeedback("");
    } catch (error) {
      memories = [];
      renderMemories();
      setFeedback(error.message || "Không tải được trí nhớ.", true);
    }
  }

  async function saveMemory(event) {
    event.preventDefault();
    const contentInput = document.querySelector("#memoryContent");
    const kindInput = document.querySelector("#memoryKind");
    const content = contentInput?.value.trim() || "";
    if (content.length < 3) {
      setFeedback("Hãy nhập nội dung trí nhớ.", true);
      return;
    }

    setFeedback("Đang lưu…");
    try {
      const response = await nativeFetch("/api/memory", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ kind: kindInput?.value || "fact", content })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không lưu được trí nhớ.");
      if (contentInput) contentInput.value = "";
      setFeedback("Đã lưu trí nhớ.");
      await loadMemories();
    } catch (error) {
      setFeedback(error.message || "Không lưu được trí nhớ.", true);
    }
  }

  async function deleteMemory(memoryId) {
    setFeedback("Đang xóa…");
    try {
      const response = await nativeFetch(`/api/memory/${encodeURIComponent(memoryId)}`, {
        method: "DELETE"
      });
      if (!response.ok && response.status !== 404) {
        const payload = await response.json().catch(() => ({}));
        throw new Error(payload.error || "Không xóa được trí nhớ.");
      }
      setFeedback("Đã xóa trí nhớ.");
      await loadMemories();
    } catch (error) {
      setFeedback(error.message || "Không xóa được trí nhớ.", true);
    }
  }

  function renderMemories() {
    const list = document.querySelector("#memoryList");
    const empty = document.querySelector("#memoryEmpty");
    const summary = document.querySelector("#memorySummary");
    const count = document.querySelector("#memorySidebarCount");
    if (summary) summary.textContent = `${memories.length} trí nhớ`;
    if (count) count.textContent = String(memories.length);
    if (!list || !empty) return;

    list.replaceChildren();
    empty.hidden = memories.length > 0;

    memories.forEach(memory => {
      const row = document.createElement("article");
      row.className = "memory-row";
      row.setAttribute("role", "listitem");

      const body = document.createElement("div");
      body.className = "memory-row-body";
      const badge = document.createElement("span");
      badge.className = `memory-kind memory-kind-${memory.kind || "fact"}`;
      badge.textContent = kindLabel(memory.kind);
      const content = document.createElement("p");
      content.textContent = memory.content || "";
      body.append(badge, content);

      const remove = document.createElement("button");
      remove.type = "button";
      remove.className = "memory-delete";
      remove.dataset.memoryDelete = memory.id;
      remove.textContent = "Xóa";
      remove.setAttribute("aria-label", `Xóa trí nhớ: ${memory.content || ""}`);

      row.append(body, remove);
      list.appendChild(row);
    });
  }

  function kindLabel(kind) {
    if (kind === "rule") return "Quy tắc";
    if (kind === "preference") return "Sở thích";
    return "Thông tin";
  }

  function setFeedback(message, isError = false) {
    const feedback = document.querySelector("#memoryFeedback");
    if (!feedback) return;
    feedback.textContent = message;
    feedback.classList.toggle("error", isError);
  }

  function injectStyles() {
    if (document.querySelector("#v060Styles")) return;
    const style = document.createElement("style");
    style.id = "v060Styles";
    style.textContent = `
      .memory-use-toggle{display:flex;align-items:center;gap:7px;cursor:pointer;user-select:none;white-space:nowrap}
      .memory-use-toggle input{position:absolute;opacity:0;pointer-events:none}
      .memory-switch{width:34px;height:20px;border-radius:999px;background:#d0d5dd;position:relative;transition:.2s ease;flex:none}
      .memory-switch::after{content:"";position:absolute;width:14px;height:14px;border-radius:50%;background:#fff;top:3px;left:3px;box-shadow:0 1px 3px rgba(0,0,0,.25);transition:.2s ease}
      .memory-use-toggle input:checked + .memory-switch{background:#3157d5}
      .memory-use-toggle input:checked + .memory-switch::after{transform:translateX(14px)}
      .memory-toggle-copy{display:flex;align-items:baseline;gap:5px}
      .memory-toggle-copy strong{font-size:12px;color:var(--text,#202124)}
      .memory-toggle-copy small{font-size:11px;color:var(--muted,#667085)}
      .memory-card{width:min(620px,calc(100vw - 32px));max-height:min(780px,calc(100vh - 40px));overflow:auto}
      .memory-intro{display:flex;gap:12px;padding:14px;border:1px solid #dde8ce;border-radius:12px;background:#f7faef;margin-bottom:14px}
      .memory-intro-icon{width:34px;height:34px;border-radius:10px;display:grid;place-items:center;background:#c8ff2e;color:#15251d;font-weight:800;flex:none}
      .memory-intro p{margin:4px 0 0;color:#667085;font-size:12px;line-height:1.45}
      .memory-dialog-toggle{display:flex;gap:10px;align-items:flex-start;padding:12px 0 15px;border-bottom:1px solid #edf0f2;margin-bottom:14px}
      .memory-dialog-toggle input{margin-top:3px}
      .memory-dialog-toggle span{display:flex;flex-direction:column;gap:3px}
      .memory-dialog-toggle small{color:#667085}
      .memory-form textarea{width:100%;resize:vertical;min-height:82px;box-sizing:border-box}
      .memory-form-actions{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-top:10px}
      .memory-form-actions .field-hint{margin:0;max-width:340px}
      .memory-list-header{display:flex;justify-content:space-between;align-items:center;margin-top:18px;padding-top:14px;border-top:1px solid #edf0f2}
      .memory-list-header>div{display:flex;flex-direction:column;gap:2px}
      .memory-list-header span{font-size:11px;color:#667085}
      .memory-list{display:flex;flex-direction:column;gap:8px;margin-top:10px}
      .memory-row{display:flex;align-items:flex-start;justify-content:space-between;gap:12px;border:1px solid #e3e8e5;border-radius:10px;padding:11px 12px;background:#fff}
      .memory-row-body{min-width:0}
      .memory-row-body p{margin:6px 0 0;white-space:pre-wrap;word-break:break-word;font-size:13px;line-height:1.45;color:#26332d}
      .memory-kind{display:inline-flex;padding:3px 7px;border-radius:999px;background:#eef2f0;color:#4a5b52;font-size:10px;font-weight:700}
      .memory-kind-preference{background:#eef2ff;color:#334eaa}
      .memory-kind-rule{background:#fff5df;color:#885d08}
      .memory-delete{border:0;background:transparent;color:#b42318;cursor:pointer;padding:4px;font-size:11px;flex:none}
      .memory-empty{text-align:center;padding:24px 12px;color:#667085}
      .memory-empty strong{display:block;color:#344054;margin:6px 0}
      .memory-empty p{margin:0;font-size:12px}
      .settings-feedback.error{color:#b42318}
      @media(max-width:900px){.knowledge-chat-controls{flex-wrap:wrap}.memory-toggle-copy small{display:none}}
      @media(max-width:700px){.memory-form-actions{align-items:stretch;flex-direction:column}.memory-form-actions .primary-button{width:100%}}
    `;
    document.head.appendChild(style);
  }
})();
