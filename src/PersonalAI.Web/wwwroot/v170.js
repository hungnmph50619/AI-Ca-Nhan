(() => {
  const VERSION = "1.7.0";
  let busy = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureButton();
    ensureDialog();
    refreshBadge().catch(() => {});
  }

  function updateVersion() {
    const node = document.querySelector(".brand > div:last-child > span");
    if (node) node.textContent = "Phiên bản " + VERSION;
  }

  function ensureButton() {
    if (document.querySelector("#decisionEngineButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "decisionEngineButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "◇";

    const label = document.createElement("span");
    label.textContent = "Quyết định";

    const badge = document.createElement("span");
    badge.id = "decisionEngineBadge";
    badge.className = "tool-count v170-decision-badge";
    badge.textContent = "…";

    button.append(icon, label, badge);

    const life = document.querySelector("#lifeContextButton");
    const android = document.querySelector("#androidCompanionButton");
    const reference = life || android;
    container.insertBefore(button, reference || null);

    button.addEventListener("click", openDialog);
  }

  function ensureDialog() {
    let dialog = document.querySelector("#decisionEngineDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "decisionEngineDialog";
    dialog.className = "settings-dialog v170-decision-dialog";
    dialog.setAttribute("aria-labelledby", "decisionEngineTitle");

    const card = document.createElement("div");
    card.className = "settings-card v170-decision-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "EVIDENCE · TRADE-OFF · USER DECIDES";

    const title = document.createElement("h2");
    title.id = "decisionEngineTitle";
    title.textContent = "Decision Engine v1.7";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Decision Engine");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v170-decision-intro";
    intro.innerHTML =
      "<strong>Phân tích để hỗ trợ quyết định, không hành động thay bạn</strong>" +
      "<p>Decision Engine dùng context đã được phép trong workspace để so sánh lựa chọn, nêu trade-off và uncertainty. Recommendation không tự chạy tool, task hay action.</p>";

    const status = document.createElement("div");
    status.id = "decisionEngineStatus";
    status.className = "v170-decision-status";
    status.textContent = "Đang kiểm tra…";

    const form = document.createElement("form");
    form.id = "decisionEngineForm";
    form.className = "v170-decision-form";

    const question = createTextarea(
      "Câu hỏi quyết định",
      "decisionQuestion",
      "Ví dụ: Tôi nên chọn phương án nào cho kế hoạch này?",
      4);

    const options = createTextarea(
      "Các lựa chọn — mỗi dòng một phương án",
      "decisionOptions",
      "Phương án A\nPhương án B",
      5);

    const criteria = createTextarea(
      "Tiêu chí — mỗi dòng một tiêu chí",
      "decisionCriteria",
      "Chi phí\nThời gian\nRủi ro",
      4);
    criteria.textarea.required = false;

    const constraints = createTextarea(
      "Ràng buộc / lưu ý thêm",
      "decisionConstraints",
      "Không bắt buộc.",
      3);
    constraints.textarea.required = false;

    const toggles = document.createElement("div");
    toggles.className = "v170-context-toggles";
    const knowledge = createToggle("Tài liệu", true);
    const memory = createToggle("Memory", true);
    const tasks = createToggle("Tasks", true);
    const life = createToggle("Life Context", true);
    toggles.append(
      knowledge.label,
      memory.label,
      tasks.label,
      life.label);

    const note = document.createElement("p");
    note.className = "v170-decision-note";
    note.textContent =
      "Preview chỉ chọn context cục bộ, không gọi AI. Phân tích mới gửi phần context đã chọn tới provider AI đang cấu hình.";

    const actions = document.createElement("div");
    actions.className = "v170-decision-actions";

    const preview = document.createElement("button");
    preview.id = "decisionPreviewButton";
    preview.type = "button";
    preview.className = "secondary-button";
    preview.textContent = "Preview context";

    const analyze = document.createElement("button");
    analyze.id = "decisionAnalyzeButton";
    analyze.type = "submit";
    analyze.className = "primary-button";
    analyze.textContent = "Phân tích";

    actions.append(preview, analyze);

    const feedback = document.createElement("div");
    feedback.id = "decisionEngineFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    form.append(
      question.wrapper,
      options.wrapper,
      criteria.wrapper,
      constraints.wrapper,
      toggles,
      note,
      actions,
      feedback);

    const result = document.createElement("section");
    result.id = "decisionEngineResult";
    result.className = "v170-decision-result";
    result.hidden = true;

    const resultMeta = document.createElement("div");
    resultMeta.id = "decisionResultMeta";
    resultMeta.className = "v170-decision-result-meta";

    const contextSummary = document.createElement("div");
    contextSummary.id = "decisionContextSummary";
    contextSummary.className = "v170-decision-context";

    const analysis = document.createElement("div");
    analysis.id = "decisionAnalysis";
    analysis.className = "v170-decision-analysis";

    const sources = document.createElement("div");
    sources.id = "decisionSources";
    sources.className = "v170-decision-sources";

    const boundary = document.createElement("div");
    boundary.className = "v170-decision-boundary";
    boundary.textContent =
      "Decision Engine không có execute endpoint. Quyết định cuối cùng và mọi hành động tiếp theo thuộc về bạn.";

    result.append(
      resultMeta,
      contextSummary,
      analysis,
      sources,
      boundary);

    form.addEventListener("submit", async event => {
      event.preventDefault();
      await runDecision(
        "/api/decisions/analyze",
        true);
    });

    preview.addEventListener("click", async () => {
      await runDecision(
        "/api/decisions/preview",
        false);
    });

    card.append(
      header,
      intro,
      status,
      form,
      result);
    dialog.appendChild(card);

    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });

    document.body.appendChild(dialog);

    function payload() {
      return {
        question: question.textarea.value.trim(),
        options: splitLines(options.textarea.value),
        criteria: splitLines(criteria.textarea.value),
        constraints: constraints.textarea.value.trim() || null,
        useKnowledge: knowledge.input.checked,
        useMemory: memory.input.checked,
        useTaskContext: tasks.input.checked,
        useLifeContext: life.input.checked
      };
    }

    async function runDecision(path, withAi) {
      if (busy) return;
      setFeedback("");

      const body = payload();
      if (!body.question || body.options.length < 2) {
        setFeedback(
          "Hãy nhập câu hỏi và ít nhất 2 lựa chọn khác nhau.",
          true);
        return;
      }

      busy = true;
      preview.disabled = true;
      analyze.disabled = true;
      setFeedback(
        withAi
          ? "Đang phân tích bằng AI…"
          : "Đang chọn context cục bộ…");

      try {
        const response = await fetch(path, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(body)
        });
        const payload = await response.json().catch(() => ({}));
        if (!response.ok) {
          throw new Error(
            payload.error
            || "Decision Engine không xử lý được yêu cầu.");
        }

        renderResult(
          payload,
          withAi);
        setFeedback(
          withAi
            ? "Đã hoàn tất phân tích. Recommendation không được tự thực thi."
            : "Preview hoàn tất. Chưa gọi nhà cung cấp AI.");
      } catch (error) {
        setFeedback(
          error.message
          || "Decision Engine không xử lý được yêu cầu.",
          true);
      } finally {
        busy = false;
        preview.disabled = false;
        analyze.disabled = false;
      }
    }
  }

  async function openDialog() {
    const dialog = ensureDialog();
    dialog.showModal();
    await loadStatus();
  }

  async function refreshBadge() {
    const response = await fetch(
      "/api/decisions/status",
      { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    updateBadge(
      response.ok
      && payload.supported === true
      && payload.autoActionEnabled === false
      && payload.toolExecutionEnabled === false);
  }

  async function loadStatus() {
    try {
      const response = await fetch(
        "/api/decisions/status",
        { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(
          payload.error
          || "Không đọc được Decision Engine status.");
      }

      const status = document.querySelector("#decisionEngineStatus");
      if (status) {
        status.textContent =
          `Sẵn sàng · tối đa ${payload.maximumOptions || 0} lựa chọn · ${payload.maximumCriteria || 0} tiêu chí · auto-action: ${payload.autoActionEnabled ? "bật" : "tắt"} · tool execution: ${payload.toolExecutionEnabled ? "bật" : "tắt"}`;
      }

      updateBadge(
        payload.supported === true
        && payload.autoActionEnabled === false
        && payload.toolExecutionEnabled === false);
    } catch (error) {
      setFeedback(
        error.message
        || "Không đọc được Decision Engine status.",
        true);
      updateBadge(false);
    }
  }

  function renderResult(payload, withAi) {
    const section = document.querySelector("#decisionEngineResult");
    const meta = document.querySelector("#decisionResultMeta");
    const context = document.querySelector("#decisionContextSummary");
    const analysis = document.querySelector("#decisionAnalysis");
    const sources = document.querySelector("#decisionSources");
    if (!section || !meta || !context || !analysis || !sources) return;

    section.hidden = false;
    meta.textContent =
      `Decision ${payload.decisionId || "—"} · ${withAi ? (payload.provider || "AI") + " / " + (payload.model || "model") : "local context preview"}`;

    const report = payload.context || {};
    const budget = report.budget || {};
    context.textContent =
      `Context: ${Number(report.selectedMemories) || 0} memory · ${Number(report.selectedDocuments) || 0} document · ${Number(report.selectedTasks) || 0} task · ${Number(report.selectedLifeContext) || 0} life context · ${Number(budget.usedCharacters) || 0}/${Number(budget.maximumCharacters) || 0} ký tự`;

    if (withAi) {
      analysis.textContent = payload.analysis || "Không có nội dung phân tích.";
      analysis.hidden = false;
    } else {
      analysis.textContent =
        "Preview không gọi AI. Hãy kiểm tra context rồi bấm Phân tích nếu muốn tiếp tục.";
      analysis.hidden = false;
    }

    sources.replaceChildren();
    const sourceItems = Array.isArray(payload.sources)
      ? payload.sources
      : [];
    if (sourceItems.length > 0) {
      const heading = document.createElement("strong");
      heading.textContent = "Tài liệu được chọn";
      sources.appendChild(heading);

      const list = document.createElement("ul");
      sourceItems.forEach(source => {
        const item = document.createElement("li");
        item.textContent =
          `${source.fileName || "Tài liệu"} · đoạn ${Number(source.chunkIndex) || 0}`;
        list.appendChild(item);
      });
      sources.appendChild(list);
    }
  }

  function createTextarea(labelText, id, placeholder, rows) {
    const wrapper = document.createElement("label");
    wrapper.className = "v170-field";

    const label = document.createElement("span");
    label.textContent = labelText;

    const textarea = document.createElement("textarea");
    textarea.id = id;
    textarea.rows = rows;
    textarea.placeholder = placeholder;
    textarea.required = true;

    wrapper.append(label, textarea);
    return { wrapper, textarea };
  }

  function createToggle(text, checked) {
    const label = document.createElement("label");
    label.className = "v170-toggle";

    const input = document.createElement("input");
    input.type = "checkbox";
    input.checked = checked;

    const copy = document.createElement("span");
    copy.textContent = text;

    label.append(input, copy);
    return { label, input };
  }

  function splitLines(value) {
    return String(value || "")
      .split(/\r?\n/)
      .map(item => item.trim())
      .filter(Boolean);
  }

  function updateBadge(ready) {
    const badge = document.querySelector("#decisionEngineBadge");
    if (!badge) return;
    badge.textContent = ready ? "ON" : "OFF";
    badge.className =
      `tool-count v170-decision-badge ${ready ? "is-ready" : "is-off"}`;
  }

  function setFeedback(message, isError = false) {
    const node = document.querySelector("#decisionEngineFeedback");
    if (!node) return;
    node.textContent = message;
    node.className = isError
      ? "settings-feedback error"
      : "settings-feedback";
  }
})();
