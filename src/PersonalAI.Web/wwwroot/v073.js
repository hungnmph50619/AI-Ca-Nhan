(() => {
  const VERSION = "0.7.3";

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;

    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Kho dữ liệu tìm kiếm kết hợp v${VERSION}`;

    const row = document.querySelector(".knowledge-search-row");
    const keywordButton = document.querySelector("#knowledgeSearchButton");
    const semanticButton = document.querySelector("#knowledgeSemanticSearchButton");
    if (row && keywordButton && !document.querySelector("#knowledgeHybridSearchButton")) {
      const hybridButton = document.createElement("button");
      hybridButton.id = "knowledgeHybridSearchButton";
      hybridButton.className = "secondary-button knowledge-hybrid-button";
      hybridButton.type = "button";
      hybridButton.textContent = "Tìm kết hợp";
      hybridButton.addEventListener("click", hybridSearch);
      if (semanticButton) {
        semanticButton.insertAdjacentElement("beforebegin", hybridButton);
      } else {
        keywordButton.insertAdjacentElement("afterend", hybridButton);
      }
    }

    const searchForm = document.querySelector("#knowledgeSearchForm");
    const hint = searchForm?.querySelector(".field-hint");
    if (hint) {
      hint.textContent = "Từ khóa = chỉ mục toàn văn; ngữ nghĩa = véc-tơ trên máy; tìm kết hợp = hợp nhất hai nguồn rồi xếp hạng lại. Khi trò chuyện, hệ thống tự dùng tìm kiếm kết hợp.";
    }

    const vectorStatus = document.querySelector("#knowledgeEmbeddingStatus");
    if (vectorStatus && !vectorStatus.dataset.v073) {
      vectorStatus.dataset.v073 = "1";
      vectorStatus.insertAdjacentText("beforeend", " · tìm kiếm kết hợp và xếp hạng lại đã sẵn sàng");
    }
  }

  async function hybridSearch() {
    const input = document.querySelector("#knowledgeSearchInput");
    const hybridButton = document.querySelector("#knowledgeHybridSearchButton");
    const keywordButton = document.querySelector("#knowledgeSearchButton");
    const semanticButton = document.querySelector("#knowledgeSemanticSearchButton");
    const resultsNode = document.querySelector("#knowledgeSearchResults");
    const summaryNode = document.querySelector("#knowledgeSearchSummary");
    const listNode = document.querySelector("#knowledgeSearchList");
    if (!input || !hybridButton || !resultsNode || !summaryNode || !listNode) return;

    const query = input.value.trim();
    if (query.length < 2) {
      input.focus();
      input.reportValidity();
      return;
    }

    setSearchBusy(true, input, keywordButton, semanticButton, hybridButton);
    resultsNode.hidden = false;
    resultsNode.setAttribute("aria-busy", "true");
    summaryNode.textContent = "Đang hợp nhất từ khóa và véc-tơ rồi xếp hạng lại…";
    listNode.replaceChildren();

    try {
      const response = await fetch(
        `/api/knowledge/hybrid-search?query=${encodeURIComponent(query)}&limit=5`,
        { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không tìm kiếm kết hợp được.");
      renderHybridResults(payload, summaryNode, listNode);
    } catch (error) {
      summaryNode.textContent = "Không thể tìm kiếm kết hợp";
      const message = document.createElement("p");
      message.className = "knowledge-search-empty error";
      message.textContent = error.message;
      listNode.appendChild(message);
    } finally {
      setSearchBusy(false, input, keywordButton, semanticButton, hybridButton);
      resultsNode.setAttribute("aria-busy", "false");
      input.focus();
    }
  }

  function setSearchBusy(busy, input, keywordButton, semanticButton, hybridButton) {
    input.disabled = busy;
    if (keywordButton) keywordButton.disabled = busy;
    if (semanticButton) semanticButton.disabled = busy;
    hybridButton.disabled = busy;
  }

  function renderHybridResults(payload, summaryNode, listNode) {
    const results = Array.isArray(payload.results) ? payload.results : [];
    summaryNode.textContent = results.length > 0
      ? `${results.length} đoạn đã được xếp hạng lại · tìm kiếm kết hợp`
      : "Không có đoạn đủ liên quan";
    listNode.replaceChildren();

    if (results.length === 0) {
      const empty = document.createElement("p");
      empty.className = "knowledge-search-empty";
      empty.textContent = "Không tìm thấy kết quả đủ mạnh sau khi kết hợp từ khóa và véc-tơ trên máy.";
      listNode.appendChild(empty);
      return;
    }

    results.forEach(result => {
      const item = document.createElement("article");
      item.className = "knowledge-search-item knowledge-hybrid-result";

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
      const hybridScore = Number(result.hybridScore);
      const scoreText = Number.isFinite(hybridScore)
        ? ` · điểm kết hợp ${hybridScore.toFixed(0)}`
        : "";
      const sourceText = result.matchSource ? ` · ${formatMatchSource(result.matchSource)}` : "";
      meta.textContent = `${location}${scoreText}${sourceText}`;
      heading.append(name, meta);

      const content = document.createElement("p");
      content.textContent = result.content || "";
      item.append(heading, content);
      listNode.appendChild(item);
    });
  }

  function formatMatchSource(value) {
    if (value === "keyword+semantic") return "từ khóa + véc-tơ";
    if (value === "semantic") return "véc-tơ";
    if (value === "keyword") return "từ khóa";
    return value;
  }
})();
