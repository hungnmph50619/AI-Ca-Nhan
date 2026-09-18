(() => {
  const VERSION = "0.7.2";

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;

    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Kho dữ liệu véc-tơ trên máy v${VERSION}`;

    const row = document.querySelector(".knowledge-search-row");
    const keywordButton = document.querySelector("#knowledgeSearchButton");
    if (row && keywordButton && !document.querySelector("#knowledgeSemanticSearchButton")) {
      const semanticButton = document.createElement("button");
      semanticButton.id = "knowledgeSemanticSearchButton";
      semanticButton.className = "secondary-button knowledge-semantic-button";
      semanticButton.type = "button";
      semanticButton.textContent = "Tìm ngữ nghĩa";
      semanticButton.addEventListener("click", semanticSearch);
      keywordButton.insertAdjacentElement("afterend", semanticButton);
    }

    const searchForm = document.querySelector("#knowledgeSearchForm");
    const hint = searchForm?.querySelector(".field-hint");
    if (hint && !document.querySelector("#knowledgeEmbeddingStatus")) {
      const status = document.createElement("p");
      status.id = "knowledgeEmbeddingStatus";
      status.className = "field-hint knowledge-vector-status";
      status.textContent = "Véc-tơ trên máy · chỉ mục được tạo khi cần.";
      hint.insertAdjacentElement("afterend", status);
    }

    const knowledgeButton = document.querySelector("#knowledgeButton");
    knowledgeButton?.addEventListener("click", refreshEmbeddingStatus);
  }

  async function refreshEmbeddingStatus() {
    const node = document.querySelector("#knowledgeEmbeddingStatus");
    if (!node) return;

    node.textContent = "Đang kiểm tra chỉ mục véc-tơ trên máy…";
    try {
      const response = await fetch("/api/knowledge/embeddings/status", { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không đọc được trạng thái véc-tơ.");

      const indexed = Number(payload.indexedChunks || 0).toLocaleString("vi-VN");
      const total = Number(payload.totalChunks || 0).toLocaleString("vi-VN");
      const dimensions = Number(payload.dimensions || 0).toLocaleString("vi-VN");
      node.textContent = `Véc-tơ trên máy · ${indexed}/${total} đoạn · ${dimensions} chiều · ${payload.embeddingModel || "mô hình trên máy"}`;
    } catch {
      node.textContent = "Véc-tơ trên máy · sẽ tự lập chỉ mục khi tìm kiếm ngữ nghĩa.";
    }
  }

  async function semanticSearch() {
    const input = document.querySelector("#knowledgeSearchInput");
    const semanticButton = document.querySelector("#knowledgeSemanticSearchButton");
    const keywordButton = document.querySelector("#knowledgeSearchButton");
    const resultsNode = document.querySelector("#knowledgeSearchResults");
    const summaryNode = document.querySelector("#knowledgeSearchSummary");
    const listNode = document.querySelector("#knowledgeSearchList");
    if (!input || !semanticButton || !resultsNode || !summaryNode || !listNode) return;

    const query = input.value.trim();
    if (query.length < 2) {
      input.focus();
      input.reportValidity();
      return;
    }

    input.disabled = true;
    semanticButton.disabled = true;
    if (keywordButton) keywordButton.disabled = true;
    resultsNode.hidden = false;
    resultsNode.setAttribute("aria-busy", "true");
    summaryNode.textContent = "Đang tìm bằng véc-tơ trên máy…";
    listNode.replaceChildren();

    try {
      const response = await fetch(
        `/api/knowledge/semantic-search?query=${encodeURIComponent(query)}&limit=5`,
        { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không tìm kiếm ngữ nghĩa được.");
      renderSemanticResults(payload, summaryNode, listNode);
      await refreshEmbeddingStatus();
    } catch (error) {
      summaryNode.textContent = "Không thể tìm kiếm ngữ nghĩa";
      const message = document.createElement("p");
      message.className = "knowledge-search-empty error";
      message.textContent = error.message;
      listNode.appendChild(message);
    } finally {
      input.disabled = false;
      semanticButton.disabled = false;
      if (keywordButton) keywordButton.disabled = false;
      resultsNode.setAttribute("aria-busy", "false");
      input.focus();
    }
  }

  function renderSemanticResults(payload, summaryNode, listNode) {
    const results = Array.isArray(payload.results) ? payload.results : [];
    const model = payload.embeddingModel || "mô hình véc-tơ trên máy";
    summaryNode.textContent = results.length > 0
      ? `${results.length} đoạn gần nhất · Mô hình véc-tơ: ${model}`
      : "Không có đoạn đủ tương đồng";
    listNode.replaceChildren();

    if (results.length === 0) {
      const empty = document.createElement("p");
      empty.className = "knowledge-search-empty";
      empty.textContent = "Thử dùng cách diễn đạt gần với nội dung tài liệu hơn.";
      listNode.appendChild(empty);
      return;
    }

    results.forEach(result => {
      const item = document.createElement("article");
      item.className = "knowledge-search-item";

      const heading = document.createElement("div");
      heading.className = "knowledge-search-item-heading";
      const name = document.createElement("strong");
      name.textContent = result.fileName || "Tài liệu";
      const meta = document.createElement("span");
      const location = result.pageNumber
        ? `Trang ${result.pageNumber}`
        : result.heading
          ? result.heading
          : `Đoạn ${result.chunkIndex}`;
      const similarity = Number(result.similarityScore);
      const scoreText = Number.isFinite(similarity)
        ? ` · ${(Math.max(0, similarity) * 100).toFixed(0)}%`
        : "";
      meta.textContent = `${location}${scoreText}`;
      heading.append(name, meta);

      const content = document.createElement("p");
      content.textContent = result.content || "";
      item.append(heading, content);
      listNode.appendChild(item);
    });
  }
})();
