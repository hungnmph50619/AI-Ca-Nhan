(() => {
  const VERSION = "0.7.5";
  const managementState = {
    documents: [],
    selected: new Set(),
    busy: false,
    refreshTimer: 0
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersionLabels();
    const card = document.querySelector("#knowledgeDialog .knowledge-card");
    const legacyHeader = card?.querySelector(".knowledge-list-header");
    if (!card || !legacyHeader || document.querySelector("#v075Management")) return;

    card.classList.add("v075-management-enabled");
    const panel = buildManagementPanel();
    legacyHeader.before(panel);
    bindManagementEvents(panel);
    observeLegacyKnowledgeList();

    const knowledgeButton = document.querySelector("#knowledgeButton");
    knowledgeButton?.addEventListener("click", () => scheduleRefresh(0));
    document.querySelector("#knowledgeRefreshButton")?.addEventListener("click", () => scheduleRefresh(80));
    scheduleRefresh(0);
  }

  function updateVersionLabels() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;

    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Kho dữ liệu quản lý v${VERSION}`;
  }

  function buildManagementPanel() {
    const panel = document.createElement("section");
    panel.id = "v075Management";
    panel.className = "v075-management";
    panel.innerHTML = `
      <div class="v075-management-heading">
        <div>
          <strong>Quản lý tài liệu</strong>
          <span id="v075ManagementSummary">Đang tải trạng thái chỉ mục…</span>
        </div>
        <div class="v075-management-actions">
          <button class="secondary-button" id="v075ExportButton" type="button">Xuất thông tin mô tả</button>
          <button class="danger-button" id="v075BulkDeleteButton" type="button" disabled>Xóa đã chọn</button>
        </div>
      </div>
      <div class="v075-filters">
        <input id="v075FilterInput" type="search" maxlength="180" autocomplete="off" placeholder="Lọc theo tên tài liệu…" aria-label="Lọc tài liệu theo tên">
        <select id="v075TypeFilter" aria-label="Lọc theo loại tệp">
          <option value="">Mọi định dạng</option>
          <option value="PDF">PDF</option>
          <option value="DOCX">DOCX</option>
          <option value="TXT">TXT</option>
          <option value="MD">Markdown</option>
        </select>
        <select id="v075StatusFilter" aria-label="Lọc theo trạng thái chỉ mục">
          <option value="">Mọi trạng thái</option>
          <option value="ready">Sẵn sàng</option>
          <option value="needs-indexing">Thiếu véc-tơ</option>
          <option value="indexing">Đang lập chỉ mục</option>
          <option value="error">Lỗi đọc tài liệu</option>
          <option value="index-error">Lỗi véc-tơ</option>
        </select>
        <select id="v075Sort" aria-label="Sắp xếp tài liệu">
          <option value="newest">Mới nhất</option>
          <option value="name">Tên A–Z</option>
          <option value="size">Kích thước lớn nhất</option>
          <option value="chunks">Nhiều đoạn nhất</option>
        </select>
      </div>
      <div class="v075-selection-bar">
        <label><input id="v075SelectVisible" type="checkbox"> Chọn tất cả kết quả đang hiển thị</label>
        <span id="v075SelectedCount">0 đã chọn</span>
      </div>
      <div class="v075-management-feedback" id="v075ManagementFeedback" role="status" aria-live="polite"></div>
      <div class="v075-document-list" id="v075DocumentList"></div>
      <div class="v075-empty" id="v075Empty" hidden>Không có tài liệu phù hợp với bộ lọc.</div>
    `;
    return panel;
  }

  function bindManagementEvents(panel) {
    panel.querySelector("#v075FilterInput")?.addEventListener("input", renderDocuments);
    panel.querySelector("#v075TypeFilter")?.addEventListener("change", renderDocuments);
    panel.querySelector("#v075StatusFilter")?.addEventListener("change", renderDocuments);
    panel.querySelector("#v075Sort")?.addEventListener("change", renderDocuments);
    panel.querySelector("#v075ExportButton")?.addEventListener("click", exportMetadata);
    panel.querySelector("#v075BulkDeleteButton")?.addEventListener("click", deleteSelectedDocuments);
    panel.querySelector("#v075SelectVisible")?.addEventListener("change", event => {
      const visible = getVisibleDocuments();
      visible.forEach(item => {
        if (event.target.checked) managementState.selected.add(item.id);
        else managementState.selected.delete(item.id);
      });
      renderDocuments();
    });
  }

  function observeLegacyKnowledgeList() {
    const legacyList = document.querySelector("#knowledgeList");
    if (!legacyList) return;
    const observer = new MutationObserver(() => scheduleRefresh(120));
    observer.observe(legacyList, { childList: true });
  }

  function scheduleRefresh(delay = 80) {
    window.clearTimeout(managementState.refreshTimer);
    managementState.refreshTimer = window.setTimeout(() => {
      loadManagement().catch(error => showFeedback(error.message, "error"));
    }, delay);
  }

  async function loadManagement() {
    const response = await fetch("/api/knowledge/documents/management", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.error || "Không đọc được trạng thái quản lý tài liệu.");

    managementState.documents = Array.isArray(payload.documents) ? payload.documents : [];
    const validIds = new Set(managementState.documents.map(item => item.id));
    managementState.selected = new Set(
      [...managementState.selected].filter(id => validIds.has(id)));

    const summary = document.querySelector("#v075ManagementSummary");
    if (summary) {
      const chunkCount = managementState.documents.reduce((sum, item) => sum + Number(item.chunkCount || 0), 0);
      const indexedCount = managementState.documents.reduce((sum, item) => sum + Number(item.indexedChunkCount || 0), 0);
      summary.textContent = `${payload.total ?? managementState.documents.length} tài liệu · ${chunkCount} đoạn · ${indexedCount} véc-tơ · ${payload.embeddingModel || "mô hình trên máy"}`;
    }

    showFeedback("");
    renderDocuments();
  }

  function getVisibleDocuments() {
    const filter = (document.querySelector("#v075FilterInput")?.value || "").trim().toLocaleLowerCase("vi");
    const type = document.querySelector("#v075TypeFilter")?.value || "";
    const status = document.querySelector("#v075StatusFilter")?.value || "";
    const sort = document.querySelector("#v075Sort")?.value || "newest";

    const items = managementState.documents.filter(item => {
      if (filter && !String(item.fileName || "").toLocaleLowerCase("vi").includes(filter)) return false;
      if (type && item.fileType !== type) return false;
      if (status && item.indexStatus !== status) return false;
      return true;
    });

    items.sort((a, b) => {
      if (sort === "name") return String(a.fileName).localeCompare(String(b.fileName), "vi");
      if (sort === "size") return Number(b.fileSize || 0) - Number(a.fileSize || 0);
      if (sort === "chunks") return Number(b.chunkCount || 0) - Number(a.chunkCount || 0);
      return new Date(b.createdAt || 0) - new Date(a.createdAt || 0);
    });
    return items;
  }

  function renderDocuments() {
    const list = document.querySelector("#v075DocumentList");
    const empty = document.querySelector("#v075Empty");
    if (!list || !empty) return;

    const visible = getVisibleDocuments();
    list.replaceChildren();
    empty.hidden = visible.length > 0;

    visible.forEach(item => list.appendChild(createDocumentCard(item)));
    updateSelectionUi(visible);
  }

  function createDocumentCard(item) {
    const card = document.createElement("article");
    card.className = "v075-document-card";
    card.dataset.documentId = item.id;

    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.className = "v075-document-check";
    checkbox.checked = managementState.selected.has(item.id);
    checkbox.setAttribute("aria-label", `Chọn ${item.fileName}`);
    checkbox.addEventListener("change", () => {
      if (checkbox.checked) managementState.selected.add(item.id);
      else managementState.selected.delete(item.id);
      updateSelectionUi(getVisibleDocuments());
    });

    const content = document.createElement("div");
    content.className = "v075-document-content";
    const title = document.createElement("strong");
    title.className = "v075-document-title";
    title.textContent = item.fileName;

    const meta = document.createElement("div");
    meta.className = "v075-document-meta";
    meta.textContent = [
      item.fileType,
      formatBytes(item.fileSize),
      `${item.chunkCount} đoạn`,
      `${item.indexedChunkCount}/${item.chunkCount} vector`,
      item.pageCount ? `${item.pageCount} trang` : null,
      formatDate(item.createdAt)
    ].filter(Boolean).join(" · ");

    const status = document.createElement("span");
    status.className = `v075-index-status status-${item.indexStatus || "unknown"}`;
    status.textContent = statusLabel(item.indexStatus);
    status.title = item.missingEmbeddingCount > 0
      ? `Còn ${item.missingEmbeddingCount} đoạn chưa có vector hiện hành.`
      : "Chỉ mục hiện hành đã đầy đủ.";

    content.append(title, meta, status);

    const actions = document.createElement("div");
    actions.className = "v075-document-actions";
    actions.append(
      actionButton("Đổi tên", () => renameDocument(item)),
      actionButton("Lập lại chỉ mục", () => reindexDocument(item)),
      actionButton("Xóa", () => deleteOneDocument(item), true));

    card.append(checkbox, content, actions);
    return card;
  }

  function actionButton(label, handler, danger = false) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = danger ? "v075-action danger" : "v075-action";
    button.textContent = label;
    button.disabled = managementState.busy;
    button.addEventListener("click", handler);
    return button;
  }

  function updateSelectionUi(visible) {
    const count = document.querySelector("#v075SelectedCount");
    const bulk = document.querySelector("#v075BulkDeleteButton");
    const selectVisible = document.querySelector("#v075SelectVisible");
    if (count) count.textContent = `${managementState.selected.size} đã chọn`;
    if (bulk) bulk.disabled = managementState.busy || managementState.selected.size === 0;
    if (selectVisible) {
      const selectedVisible = visible.filter(item => managementState.selected.has(item.id)).length;
      selectVisible.checked = visible.length > 0 && selectedVisible === visible.length;
      selectVisible.indeterminate = selectedVisible > 0 && selectedVisible < visible.length;
      selectVisible.disabled = managementState.busy || visible.length === 0;
    }
  }

  async function renameDocument(item) {
    if (managementState.busy) return;
    const value = window.prompt(
      "Tên mới của tài liệu. Không thể đổi định dạng tệp:",
      item.fileName);
    if (value === null) return;

    await runMutation(`Đang đổi tên “${item.fileName}”…`, async () => {
      const response = await fetch(`/api/knowledge/documents/${encodeURIComponent(item.id)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fileName: value.trim() })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không đổi tên được tài liệu.");
      showFeedback(`Đã đổi tên thành “${payload.fileName}”.`, "success");
    });
  }

  async function reindexDocument(item) {
    if (managementState.busy) return;
    await runMutation(`Đang lập chỉ mục lại “${item.fileName}”…`, async () => {
      const response = await fetch(`/api/knowledge/documents/${encodeURIComponent(item.id)}/reindex`, {
        method: "POST"
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không lập lại chỉ mục cho tài liệu được.");
      showFeedback(`Đã lập lại chỉ mục “${payload.fileName}”: ${payload.chunkCount} đoạn, ${payload.indexedChunkCount} véc-tơ.`, "success");
    });
  }

  async function deleteOneDocument(item) {
    if (managementState.busy) return;
    if (!window.confirm(`Xóa vĩnh viễn “${item.fileName}” khỏi Kho dữ liệu?`)) return;
    managementState.selected = new Set([item.id]);
    await deleteSelectedDocuments(true);
  }

  async function deleteSelectedDocuments(skipConfirm = false) {
    if (managementState.busy || managementState.selected.size === 0) return;
    const ids = [...managementState.selected];
    if (!skipConfirm && !window.confirm(`Xóa vĩnh viễn ${ids.length} tài liệu đã chọn?`)) return;

    await runMutation(`Đang xóa ${ids.length} tài liệu…`, async () => {
      const response = await fetch("/api/knowledge/documents/bulk-delete", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ documentIds: ids })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không xóa được các tài liệu đã chọn.");
      managementState.selected.clear();
      showFeedback(`Đã xóa ${payload.deleted ?? 0} tài liệu.`, "success");
    });
  }

  async function exportMetadata() {
    if (managementState.busy) return;
    try {
      const response = await fetch("/api/knowledge/documents/export", { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không xuất được thông tin mô tả của tài liệu.");

      const blob = new Blob([JSON.stringify(payload, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      const date = new Date().toISOString().slice(0, 10);
      anchor.href = url;
      anchor.download = `personal-ai-knowledge-metadata-${date}.json`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      URL.revokeObjectURL(url);
      showFeedback(`Đã xuất thông tin mô tả của ${(payload.documents || []).length} tài liệu.`, "success");
    } catch (error) {
      showFeedback(error.message, "error");
    }
  }

  async function runMutation(progressMessage, action) {
    setBusy(true);
    showFeedback(progressMessage);
    try {
      await action();
      await refreshLegacyDocuments();
      await loadManagement();
    } catch (error) {
      showFeedback(error.message, "error");
    } finally {
      setBusy(false);
      renderDocuments();
    }
  }

  async function refreshLegacyDocuments() {
    if (typeof window.refreshKnowledgeDocuments === "function") {
      try {
        await window.refreshKnowledgeDocuments(false);
        return;
      } catch {
        // Management state is authoritative for this panel; continue with local refresh.
      }
    }
  }

  function setBusy(value) {
    managementState.busy = value;
    document.querySelectorAll("#v075Management button, #v075Management select, #v075Management input")
      .forEach(element => {
        if (element.id !== "v075FilterInput") element.disabled = value;
      });
    updateSelectionUi(getVisibleDocuments());
  }

  function showFeedback(message, type = "") {
    const feedback = document.querySelector("#v075ManagementFeedback");
    if (!feedback) return;
    feedback.textContent = message || "";
    feedback.className = `v075-management-feedback ${type}`.trim();
  }

  function statusLabel(status) {
    return {
      ready: "Sẵn sàng",
      "needs-indexing": "Thiếu véc-tơ",
      indexing: "Đang lập chỉ mục",
      error: "Lỗi đọc tài liệu",
      "index-error": "Lỗi véc-tơ"
    }[status] || "Chưa xác định";
  }

  function formatBytes(value) {
    const bytes = Number(value || 0);
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  function formatDate(value) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "";
    return new Intl.DateTimeFormat("vi-VN", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric"
    }).format(date);
  }
})();
