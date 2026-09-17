(() => {
  const VERSION = "0.7.4";
  const WORKSPACE_STORAGE_KEY = "personal-ai-v0.4-workspace";
  const sourceCache = new Map();
  let enhanceScheduled = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersionLabels();
    ensureCitationDialog();
    enhanceVisibleCitations();

    const messages = document.querySelector("#messages");
    if (messages) {
      const observer = new MutationObserver(scheduleEnhance);
      observer.observe(messages, { childList: true, subtree: true });
    }

    window.addEventListener("storage", event => {
      if (event.key === WORKSPACE_STORAGE_KEY) scheduleEnhance();
    });
  }

  function updateVersionLabels() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;

    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Kho dữ liệu có trích nguồn v${VERSION}`;
  }

  function scheduleEnhance() {
    if (enhanceScheduled) return;
    enhanceScheduled = true;
    queueMicrotask(() => {
      enhanceScheduled = false;
      enhanceVisibleCitations();
    });
  }

  function enhanceVisibleCitations() {
    const sourceGroups = Array.from(document.querySelectorAll("#messages .message-row.assistant .message-sources"));
    if (sourceGroups.length === 0) return;

    const assistantMessages = getActiveAssistantMessages();
    if (assistantMessages.length === 0) return;

    sourceGroups.forEach((group, groupIndex) => {
      const message = assistantMessages[groupIndex];
      const sources = Array.isArray(message?.sources) ? message.sources : [];
      const chips = Array.from(group.querySelectorAll(".message-source"));

      chips.forEach((chip, sourceIndex) => {
        const source = sources[sourceIndex];
        if (!source || chip.dataset.citationBound === "true") return;

        const documentId = typeof source.documentId === "string" ? source.documentId : "";
        const chunkIndex = Number(source.chunkIndex);
        if (!documentId || !Number.isInteger(chunkIndex) || chunkIndex <= 0) {
          chip.dataset.citationBound = "true";
          return;
        }

        const button = document.createElement("button");
        button.type = "button";
        button.className = `${chip.className} citation-source-button`;
        button.dataset.citationBound = "true";
        button.dataset.documentId = documentId;
        button.dataset.chunkIndex = String(chunkIndex);
        button.dataset.citationIndex = String(sourceIndex + 1);
        button.textContent = `[${sourceIndex + 1}] ${source.fileName || "Tài liệu"} · Đoạn ${chunkIndex}`;
        button.title = "Mở đúng đoạn tài liệu đã được dùng làm nguồn";
        button.setAttribute("aria-label", `Mở nguồn ${sourceIndex + 1}: ${source.fileName || "Tài liệu"}, đoạn ${chunkIndex}`);
        button.addEventListener("click", () => openCitation(button));
        chip.replaceWith(button);

        enrichCitationButton(button).catch(() => {
          // Giữ nhãn cơ bản nếu nguồn đã bị xóa hoặc tạm thời không đọc được.
        });
      });
    });
  }

  function getActiveAssistantMessages() {
    try {
      const raw = localStorage.getItem(WORKSPACE_STORAGE_KEY);
      if (!raw) return [];
      const workspace = JSON.parse(raw);
      const conversations = Array.isArray(workspace?.conversations) ? workspace.conversations : [];
      const active = conversations.find(item => item?.id === workspace?.activeConversationId)
        || conversations[0];
      const messages = Array.isArray(active?.messages) ? active.messages : [];
      return messages.filter(message => message?.role === "assistant");
    } catch {
      return [];
    }
  }

  async function enrichCitationButton(button) {
    const detail = await loadCitation(button.dataset.documentId, Number(button.dataset.chunkIndex));
    const citationIndex = button.dataset.citationIndex || "?";
    button.textContent = `[${citationIndex}] ${buildCitationLabel(detail)}`;
    button.title = buildCitationTitle(detail);
  }

  async function loadCitation(documentId, chunkIndex) {
    const key = `${documentId}:${chunkIndex}`;
    if (sourceCache.has(key)) return sourceCache.get(key);

    const request = fetch(
      `/api/knowledge/documents/${encodeURIComponent(documentId)}/chunks/${encodeURIComponent(chunkIndex)}`,
      { cache: "no-store" })
      .then(async response => {
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
          throw new Error(response.status === 404
            ? "Đoạn nguồn này không còn trong Kho dữ liệu."
            : payload.error || "Không đọc được đoạn nguồn.");
        }
        return payload;
      })
      .catch(error => {
        sourceCache.delete(key);
        throw error;
      });

    sourceCache.set(key, request);
    return request;
  }

  function buildCitationLabel(detail) {
    const parts = [detail.fileName || "Tài liệu"];
    if (Number.isInteger(Number(detail.pageNumber)) && Number(detail.pageNumber) > 0) {
      parts.push(`Trang ${Number(detail.pageNumber)}`);
    }
    if (typeof detail.heading === "string" && detail.heading.trim()) {
      parts.push(`Mục ${detail.heading.trim()}`);
    } else if (typeof detail.section === "string" && detail.section.trim()
      && !/^trang\s+\d+$/i.test(detail.section.trim())) {
      parts.push(`Phần ${detail.section.trim()}`);
    }
    parts.push(`Đoạn ${Number(detail.chunkIndex)}`);
    return parts.join(" · ");
  }

  function buildCitationTitle(detail) {
    const location = buildCitationLabel(detail);
    return `${location}. Bấm để xem nguyên văn đoạn nguồn.`;
  }

  async function openCitation(button) {
    const dialog = ensureCitationDialog();
    const title = dialog.querySelector("#citationTitle");
    const meta = dialog.querySelector("#citationMeta");
    const content = dialog.querySelector("#citationContent");
    const copy = dialog.querySelector("#citationCopyButton");

    title.textContent = `Nguồn ${button.dataset.citationIndex || ""}`.trim();
    meta.textContent = "Đang tải đoạn nguồn…";
    content.textContent = "";
    copy.disabled = true;
    dialog.showModal();

    try {
      const detail = await loadCitation(button.dataset.documentId, Number(button.dataset.chunkIndex));
      title.textContent = detail.fileName || "Nguồn tài liệu";
      meta.textContent = buildCitationLabel(detail);
      content.textContent = detail.content || "";
      copy.disabled = !content.textContent;
      copy.onclick = async () => {
        try {
          await navigator.clipboard.writeText(content.textContent);
          const original = copy.textContent;
          copy.textContent = "Đã sao chép";
          setTimeout(() => { copy.textContent = original; }, 1200);
        } catch {
          copy.textContent = "Không sao chép được";
        }
      };
    } catch (error) {
      title.textContent = "Nguồn không khả dụng";
      meta.textContent = error.message;
      content.textContent = "Đoạn nguồn có thể đã bị xóa hoặc tài liệu đã được thay đổi.";
    }
  }

  function ensureCitationDialog() {
    let dialog = document.querySelector("#citationDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "citationDialog";
    dialog.className = "citation-dialog";

    const card = document.createElement("div");
    card.className = "citation-card";

    const header = document.createElement("header");
    header.className = "citation-header";
    const headingWrap = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "TRÍCH NGUỒN TỪ KHO DỮ LIỆU";
    const title = document.createElement("h2");
    title.id = "citationTitle";
    title.textContent = "Nguồn tài liệu";
    headingWrap.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng nguồn");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(headingWrap, close);

    const meta = document.createElement("p");
    meta.id = "citationMeta";
    meta.className = "citation-meta";

    const content = document.createElement("pre");
    content.id = "citationContent";
    content.className = "citation-content";

    const footer = document.createElement("footer");
    footer.className = "citation-actions";
    const copy = document.createElement("button");
    copy.id = "citationCopyButton";
    copy.type = "button";
    copy.className = "secondary-button";
    copy.textContent = "Sao chép đoạn nguồn";
    copy.disabled = true;
    const closeFooter = document.createElement("button");
    closeFooter.type = "button";
    closeFooter.className = "primary-button";
    closeFooter.textContent = "Đóng";
    closeFooter.addEventListener("click", () => dialog.close());
    footer.append(copy, closeFooter);

    card.append(header, meta, content, footer);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
    document.body.appendChild(dialog);
    return dialog;
  }
})();
