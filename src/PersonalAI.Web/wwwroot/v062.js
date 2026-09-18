(() => {
  const VERSION = "0.6.5";
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
      const hint = actions.querySelector(".field-hint");
      if (hint) hint.textContent = "Thêm mới hoặc chọn Sửa ở một trí nhớ đã lưu.";
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
    } catch (error) {
      feedback(error.message || "Không tải được trí nhớ.", true);
    }
  }

  async function saveOrUpdate(event) {
    event.preventDefault();
    const content = document.querySelector("#memoryContent")?.value.trim() || "";
    const kind = document.querySelector("#memoryKind")?.value || "fact";
    const lifecycle = readLifecycle();
    if (content.length < 3) return feedback("Hãy nhập nội dung trí nhớ.", true);
    if (lifecycle.retention === "temporary" && !lifecycle.expiresAt) return feedback("Hãy chọn ngày hết hạn cho trí nhớ tạm thời.", true);

    const duplicate = allMemories.find(memory =>
      memory.id !== editingId && normalize(memory.content) === normalize(content));

    if (!editingId) {
      if (duplicate) {
        const overwrite = await window.PersonalAiUi.confirm(`Đã có một trí nhớ cùng nội dung (${kindLabel(duplicate.kind)}). Bạn có muốn cập nhật trí nhớ đó?`, { title: "Phát hiện trí nhớ trùng", confirmText: "Cập nhật trí nhớ" });
        if (!overwrite) return feedback("Đã hủy để tránh tạo trí nhớ trùng.");
        return updateMemory(duplicate.id, kind, content, true, lifecycle, "Đã cập nhật trí nhớ trùng.");
      }

      return createMemory(kind, content, lifecycle, "Đã lưu trí nhớ.");
    }

    const original = allMemories.find(memory => memory.id === editingId);
    if (!original) {
      resetEditor();
      return feedback("Trí nhớ cần sửa không còn tồn tại.", true);
    }

    let confirmOverwrite = false;
    if (duplicate) {
      confirmOverwrite = await window.PersonalAiUi.confirm(`Đã có một trí nhớ cùng nội dung (${kindLabel(duplicate.kind)}). Ghi đè trí nhớ đó bằng thay đổi này?`, { title: "Xác nhận ghi đè", confirmText: "Ghi đè" });
      if (!confirmOverwrite) return feedback("Đã hủy để tránh ghi đè trí nhớ hiện có.");
    } else if (!(await window.PersonalAiUi.confirm("Lưu thay đổi cho trí nhớ này?", { title: "Xác nhận lưu thay đổi", confirmText: "Lưu thay đổi" }))) {
      return;
    }

    return updateMemory(editingId, kind, content, confirmOverwrite, lifecycle, "Đã cập nhật trí nhớ.");
  }

  async function createMemory(kind, content, lifecycle, successMessage, reload = true, confirmCreateSimilar = false) {
    try {
      const response = await nativeFetch("/api/memory", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          kind,
          content,
          retention: lifecycle.retention,
          expiresAt: lifecycle.expiresAt,
          confirmCreateSimilar
        })
      });
      const payload = await response.json().catch(() => ({}));

      if (response.status === 409 && payload.suggestion && payload.candidateMemoryId && !confirmCreateSimilar) {
        const updateExisting = await window.PersonalAiUi.confirm(`${payload.error || "Nội dung có vẻ là bản cập nhật."}\n\nTrí nhớ cũ: ${payload.candidateContent || ""}\n\nBạn có muốn cập nhật trí nhớ cũ không?`, { title: "Phát hiện nội dung tương tự", confirmText: "Cập nhật trí nhớ cũ", cancelText: "Lưu thành trí nhớ mới" });
        if (updateExisting) {
          return updateMemory(payload.candidateMemoryId, kind, content, false, lifecycle, "Đã cập nhật trí nhớ cũ thay vì tạo bản trùng.");
        }

        if (!(await window.PersonalAiUi.confirm("Bạn vẫn muốn lưu nội dung này thành một trí nhớ mới?", { title: "Xác nhận tạo trí nhớ mới", confirmText: "Tạo trí nhớ mới" }))) {
          feedback("Đã hủy lưu trí nhớ mới.");
          return false;
        }

        return createMemory(kind, content, lifecycle, successMessage, reload, true);
      }

      if (!response.ok) throw new Error(payload.error || "Không lưu được trí nhớ.");
      if (successMessage) feedback(successMessage);
      if (reload) {
        resetEditor();
        await loadMemories();
      }
      return true;
    } catch (error) {
      feedback(error.message || "Không lưu được trí nhớ.", true);
      return false;
    }
  }

  async function updateMemory(id, kind, content, confirmOverwrite, lifecycle, successMessage) {
    try {
      const response = await nativeFetch(`/api/memory/${encodeURIComponent(id)}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          kind,
          content,
          confirmOverwrite,
          retention: lifecycle.retention,
          expiresAt: lifecycle.expiresAt
        })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không cập nhật được trí nhớ.");
      resetEditor();
      feedback(successMessage || "Đã cập nhật trí nhớ.");
      await loadMemories();
      return true;
    } catch (error) {
      feedback(error.message || "Không cập nhật được trí nhớ.", true);
      return false;
    }
  }

  async function handleListAction(event) {
    const edit = event.target.closest?.("[data-memory-edit]");
    if (edit) return beginEdit(edit.dataset.memoryEdit);
    const remove = event.target.closest?.("[data-memory-delete-v062]");
    if (!remove || !(await window.PersonalAiUi.confirm("Xóa trí nhớ này?", { title: "Xác nhận xóa trí nhớ", confirmText: "Xóa", danger: true }))) return;
    await deleteById(remove.dataset.memoryDeleteV062, true);
  }

  function beginEdit(id) {
    const memory = allMemories.find(item => item.id === id);
    if (!memory) return;
    editingId = id;
    document.querySelector("#memoryKind").value = memory.kind || "fact";
    document.querySelector("#memoryContent").value = memory.content || "";
    applyLifecycleToEditor(memory);
    document.querySelector("#memorySaveButton").textContent = "Lưu thay đổi";
    document.querySelector("#memoryCancelEditButton").hidden = false;
    document.querySelector("#memoryContent")?.focus();
    feedback("Đang sửa trí nhớ. Thay đổi chỉ được lưu sau khi bạn xác nhận.");
    window.dispatchEvent(new CustomEvent("personalai:memory-edit", { detail: { memory } }));
  }

  function resetEditor() {
    editingId = null;
    const content = document.querySelector("#memoryContent");
    if (content) content.value = "";
    const save = document.querySelector("#memorySaveButton");
    if (save) save.textContent = "Lưu trí nhớ";
    const cancel = document.querySelector("#memoryCancelEditButton");
    if (cancel) cancel.hidden = true;
    applyLifecycleToEditor({ retention: "long-term", expiresAt: null });
    window.dispatchEvent(new CustomEvent("personalai:memory-edit-reset"));
  }

  async function deleteById(id, reload) {
    try {
      const response = await nativeFetch(`/api/memory/${encodeURIComponent(id)}`, { method: "DELETE" });
      if (!response.ok && response.status !== 404) throw new Error("Không xóa được trí nhớ.");
      allMemories = allMemories.filter(memory => memory.id !== id);
      if (reload) {
        feedback("Đã xóa trí nhớ.");
        await loadMemories();
      }
      return true;
    } catch (error) {
      feedback(error.message || "Không xóa được trí nhớ.", true);
      return false;
    }
  }

  function render() {
    const list = document.querySelector("#memoryList");
    const empty = document.querySelector("#memoryEmpty");
    const summary = document.querySelector("#memorySummary");
    const count = document.querySelector("#memorySidebarCount");
    if (!list || !empty) return;

    const filtered = allMemories.filter(memory =>
      (kindFilter === "all" || memory.kind === kindFilter) &&
      (!query || normalize(memory.content).includes(query)));

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
      const body = document.createElement("div");
      body.className = "memory-row-body";
      const badge = document.createElement("span");
      badge.className = `memory-kind memory-kind-${memory.kind || "fact"}`;
      badge.textContent = kindLabel(memory.kind);
      const content = document.createElement("p");
      content.textContent = memory.content || "";
      body.append(badge, content);
      const actions = document.createElement("div");
      actions.className = "memory-v062-row-actions";
      const edit = document.createElement("button");
      edit.type = "button";
      edit.className = "secondary-button";
      edit.dataset.memoryEdit = memory.id;
      edit.textContent = "Sửa";
      const remove = document.createElement("button");
      remove.type = "button";
      remove.className = "memory-delete";
      remove.dataset.memoryDeleteV062 = memory.id;
      remove.textContent = "Xóa";
      actions.append(edit, remove);
      row.append(body, actions);
      list.appendChild(row);
    });

    window.dispatchEvent(new CustomEvent("personalai:memory-rendered", {
      detail: { memories: allMemories }
    }));
  }

  function readLifecycle() {
    const retention = document.querySelector("#memoryRetention")?.value || "long-term";
    const rawExpiry = document.querySelector("#memoryExpiresAt")?.value || "";
    let expiresAt = null;
    if (retention === "temporary" && rawExpiry) {
      const parsed = new Date(rawExpiry);
      if (!Number.isNaN(parsed.getTime())) expiresAt = parsed.toISOString();
    }
    return { retention, expiresAt };
  }

  function applyLifecycleToEditor(memory) {
    const retention = document.querySelector("#memoryRetention");
    const expiresAt = document.querySelector("#memoryExpiresAt");
    if (retention) retention.value = memory?.retention || "long-term";
    if (expiresAt) expiresAt.value = toLocalDateTime(memory?.expiresAt);
    window.dispatchEvent(new CustomEvent("personalai:memory-lifecycle-changed"));
  }

  function toLocalDateTime(value) {
    if (!value) return "";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";
    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
    return local.toISOString().slice(0, 16);
  }

  function feedback(message, error = false) {
    const node = document.querySelector("#memoryFeedback");
    if (!node) return;
    node.textContent = message;
    node.classList.toggle("error", error);
  }

  function normalize(value) {
    return String(value || "").trim().replace(/\s+/g, " ").toLocaleLowerCase("vi-VN");
  }

  function kindLabel(kind) {
    return kind === "rule" ? "Quy tắc" : kind === "preference" ? "Sở thích" : "Thông tin";
  }

  function injectStyles() {
    if (document.querySelector("#v062Styles")) return;
    const style = document.createElement("style");
    style.id = "v062Styles";
    style.textContent = `.memory-v062-controls{display:grid;grid-template-columns:minmax(0,1fr) 170px;gap:8px;margin:10px 0 12px}.memory-v062-controls input,.memory-v062-controls select{width:100%;box-sizing:border-box}.memory-v062-row-actions{display:flex;gap:7px;align-items:center;flex:none}.memory-v062-row-actions .secondary-button{padding:7px 10px}@media(max-width:560px){.memory-v062-controls{grid-template-columns:1fr}.memory-row{align-items:flex-start}.memory-v062-row-actions{flex-direction:column}}`;
    document.head.appendChild(style);
  }
})();
