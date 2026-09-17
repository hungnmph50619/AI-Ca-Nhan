(() => {
  const VERSION = "0.6.2";
  let allMemories = [];
  let editingId = null;
  let query = "";
  let kindFilter = "all";
  const nativeFetch = window.fetch.bind(window);

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize, { once: true });
  else initialize();

  function initialize() {
    updateVersionLabels();
    injectStyles();
    upgradeMemoryDialog();
    document.querySelector("#memoryButton")?.addEventListener("click", () => setTimeout(loadMemories, 0));
  }

  function updateVersionLabels() {
    const brand = document.querySelector(".brand > div:last-child > span");
    if (brand) brand.textContent = `Phiên bản ${VERSION}`;
    const memoryIntro = document.querySelector(".memory-intro strong");
    if (memoryIntro) memoryIntro.textContent = `Trí nhớ v${VERSION}`;
    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Hỏi đáp có nguồn v${VERSION}`;
  }

  function upgradeMemoryDialog() {
    const oldForm = document.querySelector("#memoryForm");
    const oldList = document.querySelector("#memoryList");
    if (!oldForm || !oldList || document.querySelector("#memorySearchInput")) return;

    const form = oldForm.cloneNode(true);
    oldForm.replaceWith(form);
    const list = oldList.cloneNode(false);
    oldList.replaceWith(list);

    const actions = form.querySelector(".memory-form-actions");
    if (actions) {
      actions.querySelector(".field-hint").textContent = "Thêm mới hoặc chọn Sửa ở một trí nhớ đã lưu.";
      const cancel = document.createElement("button");
      cancel.type = "button";
      cancel.id = "memoryCancelEditButton";
      cancel.className = "secondary-button";
      cancel.textContent = "Hủy sửa";
      cancel.hidden = true;
      actions.insertBefore(cancel, actions.querySelector("#memorySaveButton"));
    }

    const header = document.querySelector(".memory-list-header");
    const controls = document.createElement("div");
    controls.className = "memory-v062-controls";
    controls.innerHTML = `
      <input id="memorySearchInput" type="search" maxlength="200" autocomplete="off" placeholder="Tìm trong trí nhớ…" aria-label="Tìm trong trí nhớ">
      <select id="memoryKindFilter" aria-label="Lọc loại trí nhớ">
        <option value="all">Tất cả loại</option>
        <option value="fact">Thông tin</option>
        <option value="preference">Sở thích</option>
        <option value="rule">Quy tắc</option>
      </select>`;
    header?.insertAdjacentElement("afterend", controls);

    form.addEventListener("submit", saveOrUpdate);
    document.querySelector("#memoryCancelEditButton")?.addEventListener("click", resetEditor);
    document.querySelector("#memorySearchInput")?.addEventListener("input", event => { query = normalize(event.target.value); render(); });
    document.querySelector("#memoryKindFilter")?.addEventListener("change", event => { kindFilter = event.target.value; render(); });
    list.addEventListener("click", handleListAction);
    document.querySelector("#memoryRefreshButton")?.addEventListener("click", loadMemories);
    loadMemories();
  }

  async function loadMemories() {
    try {
      const response = await nativeFetch("/api/memory", { cache: "no-store" });
      const payload = await response.json().catch(() => []);
      if (!response.ok) throw new Error(payload.error || "Không tải được trí nhớ.");
      allMemories = Array.isArray(payload) ? payload : [];
      render();
      feedback("");
    } catch (error) { feedback(error.message || "Không tải được trí nhớ.", true); }
  }

  async function saveOrUpdate(event) {
    event.preventDefault();
    const content = document.querySelector("#memoryContent")?.value.trim() || "";
    const kind = document.querySelector("#memoryKind")?.value || "fact";
    if (content.length < 3) return feedback("Hãy nhập nội dung trí nhớ.", true);

    const duplicate = allMemories.find(m => m.id !== editingId && normalize(m.content) === normalize(content));
    if (duplicate) {
      const ok = window.confirm(`Đã có một trí nhớ cùng nội dung (${kindLabel(duplicate.kind)}). Bạn có muốn ghi đè trí nhớ đó?`);
      if (!ok) return feedback("Đã hủy để tránh ghi đè trí nhớ hiện có.");
      if (!editingId) {
        await deleteById(duplicate.id, false);
        return createMemory(kind, content, "Đã ghi đè trí nhớ trùng.");
      }
    }

    if (!editingId) return createMemory(kind, content, "Đã lưu trí nhớ.");

    const original = allMemories.find(m => m.id === editingId);
    if (!original) { resetEditor(); return feedback("Trí nhớ cần sửa không còn tồn tại.", true); }
    if (!window.confirm("Lưu thay đổi cho trí nhớ này?")) return;

    // v0.6.2 keeps compatibility with the existing API: create replacement first,
    // then remove the old record so a failed create never destroys the original.
    if (normalize(original.content) !== normalize(content)) {
      if (duplicate) await deleteById(duplicate.id, false);
      const created = await createMemory(kind, content, "", false);
      if (!created) return;
      await deleteById(original.id, false);
    } else {
      // Kind-only edits require replacing the same-content record.
      await deleteById(original.id, false);
      const created = await createMemory(kind, content, "", false);
      if (!created) return feedback("Không tạo lại được trí nhớ sau khi đổi loại.", true);
    }
    resetEditor();
    feedback("Đã cập nhật trí nhớ.");
    await loadMemories();
  }

  async function createMemory(kind, content, successMessage, reload = true) {
    try {
      const response = await nativeFetch("/api/memory", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ kind, content }) });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không lưu được trí nhớ.");
      if (successMessage) feedback(successMessage);
      if (reload) { resetEditor(); await loadMemories(); }
      return true;
    } catch (error) { feedback(error.message || "Không lưu được trí nhớ.", true); return false; }
  }

  async function handleListAction(event) {
    const edit = event.target.closest?.("[data-memory-edit]");
    if (edit) return beginEdit(edit.dataset.memoryEdit);
    const remove = event.target.closest?.("[data-memory-delete-v062]");
    if (!remove || !window.confirm("Xóa trí nhớ này?")) return;
    await deleteById(remove.dataset.memoryDeleteV062, true);
  }

  function beginEdit(id) {
    const memory = allMemories.find(m => m.id === id);
    if (!memory) return;
    editingId = id;
    document.querySelector("#memoryKind").value = memory.kind || "fact";
    document.querySelector("#memoryContent").value = memory.content || "";
    document.querySelector("#memorySaveButton").textContent = "Lưu thay đổi";
    document.querySelector("#memoryCancelEditButton").hidden = false;
    document.querySelector("#memoryContent")?.focus();
    feedback("Đang sửa trí nhớ. Thay đổi chỉ được lưu sau khi bạn xác nhận.");
  }

  function resetEditor() {
    editingId = null;
    const content = document.querySelector("#memoryContent");
    if (content) content.value = "";
    const save = document.querySelector("#memorySaveButton");
    if (save) save.textContent = "Lưu trí nhớ";
    const cancel = document.querySelector("#memoryCancelEditButton");
    if (cancel) cancel.hidden = true;
  }

  async function deleteById(id, reload) {
    try {
      const response = await nativeFetch(`/api/memory/${encodeURIComponent(id)}`, { method: "DELETE" });
      if (!response.ok && response.status !== 404) throw new Error("Không xóa được trí nhớ.");
      allMemories = allMemories.filter(m => m.id !== id);
      if (reload) { feedback("Đã xóa trí nhớ."); await loadMemories(); }
      return true;
    } catch (error) { feedback(error.message || "Không xóa được trí nhớ.", true); return false; }
  }

  function render() {
    const list = document.querySelector("#memoryList");
    const empty = document.querySelector("#memoryEmpty");
    const summary = document.querySelector("#memorySummary");
    const count = document.querySelector("#memorySidebarCount");
    if (!list || !empty) return;
    const filtered = allMemories.filter(m => (kindFilter === "all" || m.kind === kindFilter) && (!query || normalize(m.content).includes(query)));
    if (summary) summary.textContent = filtered.length === allMemories.length ? `${allMemories.length} trí nhớ` : `${filtered.length}/${allMemories.length} trí nhớ`;
    if (count) count.textContent = String(allMemories.length);
    list.replaceChildren();
    empty.hidden = filtered.length > 0;
    const emptyTitle = empty.querySelector("strong");
    const emptyText = empty.querySelector("p");
    if (filtered.length === 0 && allMemories.length > 0) {
      if (emptyTitle) emptyTitle.textContent = "Không có kết quả phù hợp";
      if (emptyText) emptyText.textContent = "Thử từ khóa khác hoặc chọn Tất cả loại.";
    } else {
      if (emptyTitle) emptyTitle.textContent = "Chưa có trí nhớ nào";
      if (emptyText) emptyText.textContent = "Hãy lưu một thông tin, sở thích hoặc quy tắc.";
    }
    filtered.forEach(memory => {
      const row = document.createElement("article");
      row.className = "memory-row";
      row.setAttribute("role", "listitem");
      const body = document.createElement("div"); body.className = "memory-row-body";
      const badge = document.createElement("span"); badge.className = `memory-kind memory-kind-${memory.kind || "fact"}`; badge.textContent = kindLabel(memory.kind);
      const content = document.createElement("p"); content.textContent = memory.content || "";
      body.append(badge, content);
      const actions = document.createElement("div"); actions.className = "memory-v062-row-actions";
      const edit = document.createElement("button"); edit.type = "button"; edit.className = "secondary-button"; edit.dataset.memoryEdit = memory.id; edit.textContent = "Sửa";
      const remove = document.createElement("button"); remove.type = "button"; remove.className = "memory-delete"; remove.dataset.memoryDeleteV062 = memory.id; remove.textContent = "Xóa";
      actions.append(edit, remove); row.append(body, actions); list.appendChild(row);
    });
  }

  function feedback(message, error = false) { const node = document.querySelector("#memoryFeedback"); if (!node) return; node.textContent = message; node.classList.toggle("error", error); }
  function normalize(value) { return String(value || "").trim().replace(/\s+/g, " ").toLocaleLowerCase("vi-VN"); }
  function kindLabel(kind) { return kind === "rule" ? "Quy tắc" : kind === "preference" ? "Sở thích" : "Thông tin"; }

  function injectStyles() {
    if (document.querySelector("#v062Styles")) return;
    const style = document.createElement("style"); style.id = "v062Styles";
    style.textContent = `.memory-v062-controls{display:grid;grid-template-columns:minmax(0,1fr) 170px;gap:8px;margin:10px 0 12px}.memory-v062-controls input,.memory-v062-controls select{width:100%;box-sizing:border-box}.memory-v062-row-actions{display:flex;gap:7px;align-items:center;flex:none}.memory-v062-row-actions .secondary-button{padding:7px 10px}@media(max-width:560px){.memory-v062-controls{grid-template-columns:1fr}.memory-row{align-items:flex-start}.memory-v062-row-actions{flex-direction:column}}`;
    document.head.appendChild(style);
  }
})();
