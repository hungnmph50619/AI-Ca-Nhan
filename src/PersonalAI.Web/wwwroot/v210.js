(() => {
  const VERSION = "2.2.4";
  let dialog;
  let select;
  let goal;
  let feedback;
  let result;
  let meta;
  let runButton;
  let agents = [];
  const translatedNames = {
    "core.personal-assistant":"Trợ lý cá nhân",
    "planning.planner":"Tác nhân lập kế hoạch",
    "research.researcher":"Tác nhân nghiên cứu",
    "development.developer":"Tác nhân phân tích mã",
    "office.office-assistant":"Tác nhân văn phòng",
    "operations.operator":"Tác nhân lập kế hoạch thao tác",
    "quality.reviewer":"Tác nhân rà soát",
    "security.security-reviewer":"Tác nhân kiểm tra bảo mật"
  };
  const translatedRoles = {
    "general-assistant":"Trợ lý tổng hợp",planner:"Lập kế hoạch",
    research:"Nghiên cứu",developer:"Phân tích mã",office:"Văn phòng",
    operator:"Lập kế hoạch thao tác",reviewer:"Rà soát",security:"Bảo mật"
  };
  const translatedName = agent => translatedNames[agent.id] || "Tác nhân AI";
  const translatedRole = agent => translatedRoles[agent.role] || "Chức năng chuyên biệt";


  document.addEventListener("DOMContentLoaded", () => {
    updateVersion();
    ensureUi();
  });

  function updateVersion() {
    document.querySelectorAll(".brand span").forEach(node => {
      node.textContent = `Phiên bản ${VERSION}`;
    });
  }

  function ensureUi() {
    const tools = document.querySelector(".sidebar-tools");
    if (!tools || document.getElementById("agentButton")) return;

    const button = document.createElement("button");
    button.className = "settings-button";
    button.id = "agentButton";
    button.type = "button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "◉";

    const label = document.createElement("span");
    label.textContent = "Các tác nhân AI";

    const badge = document.createElement("span");
    badge.className = "tool-count";
    badge.id = "agentSidebarCount";
    badge.textContent = "0";

    button.append(icon, label, badge);

    const osButton = document.getElementById("osButton");
    if (osButton?.nextSibling) {
      tools.insertBefore(button, osButton.nextSibling);
    } else {
      tools.prepend(button);
    }

    dialog = document.createElement("dialog");
    dialog.className = "settings-dialog";
    dialog.id = "agentDialog";
    dialog.setAttribute("aria-labelledby", "agentTitle");

    const card = document.createElement("div");
    card.className = "settings-card agent-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "HỆ THỐNG TÁC NHÂN AI · v2.2.4";
    const title = document.createElement("h2");
    title.id = "agentTitle";
    title.textContent = "Sử dụng tác nhân AI";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.className = "dialog-close";
    close.type = "button";
    close.setAttribute("aria-label", "Đóng");
    close.textContent = "×";

    header.append(heading, close);

    const status = document.createElement("p");
    status.className = "agent-status";
    status.id = "agentStatus";
    status.textContent = "Đang tải danh sách tác nhân AI…";

    const grid = document.createElement("div");
    grid.className = "agent-grid";

    const labelSelect = document.createElement("label");
    labelSelect.className = "field-label";
    labelSelect.setAttribute("for", "agentSelect");
    labelSelect.textContent = "Chọn tác nhân AI";

    select = document.createElement("select");
    select.id = "agentSelect";
    select.disabled = true;

    meta = document.createElement("div");
    meta.className = "agent-meta";

    const form = document.createElement("form");
    form.className = "agent-form";
    form.id = "agentExecuteForm";

    const goalLabel = document.createElement("label");
    goalLabel.className = "field-label";
    goalLabel.setAttribute("for", "agentGoal");
    goalLabel.textContent = "Mục tiêu";

    goal = document.createElement("textarea");
    goal.id = "agentGoal";
    goal.maxLength = 4000;
    goal.placeholder = "Ví dụ: Phân tích những việc tôi đang làm và đề xuất bước tiếp theo.";
    goal.required = true;

    const options = document.createElement("div");
    options.className = "agent-context-options";
    [
      ["agentUseKnowledge", "Tài liệu của bạn", true],
      ["agentUseMemory", "Thông tin đã ghi nhớ", true],
      ["agentUseTasks", "Công việc", true],
      ["agentUseLife", "Thông tin cuộc sống", true]
    ].forEach(([id, text, checked]) => {
      const option = document.createElement("label");
      const input = document.createElement("input");
      input.type = "checkbox";
      input.id = id;
      input.checked = checked;
      const span = document.createElement("span");
      span.textContent = text;
      option.append(input, span);
      options.append(option);
    });

    const boundary = document.createElement("div");
    boundary.className = "agent-boundary";
    boundary.textContent =
      "v2.2.4: Bạn chọn tác nhân và dữ liệu muốn dùng. Quy trình nhiều bước cần bạn duyệt nội dung trước khi chuyển. Bộ lọc dữ liệu nhạy cảm không bảo đảm phát hiện mọi bí mật. Các tác nhân không tự chạy công cụ.";

    feedback = document.createElement("div");
    feedback.className = "agent-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    runButton = document.createElement("button");
    runButton.className = "primary-button";
    runButton.type = "submit";
    runButton.textContent = "Chạy tác nhân";
    runButton.disabled = true;

    form.append(
      goalLabel,
      goal,
      options,
      boundary,
      feedback,
      runButton
    );

    result = document.createElement("div");
    result.className = "agent-result";
    result.hidden = true;

    grid.append(labelSelect, select, meta, form, result);
    card.append(header, status, grid);
    dialog.append(card);
    document.body.append(dialog);

    button.addEventListener("click", async () => {
      dialog.showModal();
      await refresh();
    });
    close.addEventListener("click", () => dialog.close());
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
    select.addEventListener("change", renderAgentMeta);
    form.addEventListener("submit", executeAgent);
  }

  async function refresh() {
    const statusNode = document.getElementById("agentStatus");
    feedback.textContent = "";
    result.hidden = true;

    try {
      const [statusResponse, catalogResponse] = await Promise.all([
        fetch("/api/agents/status", { headers: workspaceHeaders() }),
        fetch("/api/agents", { headers: workspaceHeaders() })
      ]);

      if (!statusResponse.ok || !catalogResponse.ok) {
        throw new Error("Không tải được danh sách tác nhân AI.");
      }

      const status = await statusResponse.json();
      const catalog = await catalogResponse.json();
      agents = Array.isArray(catalog.agents) ? catalog.agents : [];

      statusNode.textContent =
        `${agents.length} tác nhân AI · chỉ chạy khi bạn yêu cầu · quyền chạy công cụ: ${status.toolExecutionEnabled ? "đã bật" : "đang tắt"}`;

      select.replaceChildren();
      agents.forEach(agent => {
        const option = document.createElement("option");
        option.value = agent.id;
        option.textContent = `${translatedName(agent)} · ${translatedRole(agent)}`;
        select.append(option);
      });

      select.disabled = agents.length === 0;
      runButton.disabled = agents.length === 0;
      const badge = document.getElementById("agentSidebarCount");
      if (badge) badge.textContent = String(agents.length);
      renderAgentMeta();
    } catch (error) {
      statusNode.textContent = error instanceof Error
        ? error.message
        : "Không tải được danh sách tác nhân AI.";
      select.disabled = true;
      runButton.disabled = true;
    }
  }

  function renderAgentMeta() {
    const agent = agents.find(item => item.id === select.value);
    meta.replaceChildren();
    if (!agent) return;

    const isPlanner = agent.role === "planner";
    const isResearch = agent.role === "research";
    const isDeveloper = agent.role === "developer";
    const isOffice = agent.role === "office";
    const isOperator = agent.role === "operator";
    const isReviewer = agent.role === "reviewer";
    const isSecurity = agent.role === "security";
    goal.placeholder = isSecurity
      ? "Mô tả thao tác muốn đánh giá trước khi thực hiện; không nhập mật khẩu, token hoặc khóa thật."
      : isReviewer
      ? "Dán toàn bộ đoạn văn, bản nháp hoặc kế hoạch cần rà soát (tối thiểu 80 ký tự). Chỉ nội dung bạn dán được đánh giá; có thể gửi tới AI provider đã cấu hình."
      : isOperator
      ? "Ví dụ: Lập kế hoạch kiểm tra một website và đề xuất các bước cần người dùng xác nhận; không thực hiện thao tác."
      : isOffice
      ? "Ví dụ: Soạn email nháp cập nhật tiến độ dự án, chưa gửi; liệt kê thông tin còn thiếu."
      : isDeveloper
      ? "Ví dụ: search: ExecuteAsync — xem các đoạn mã liên quan và đề xuất cải tiến. Các snippet có thể được gửi đến AI provider đã cấu hình."
      : isResearch
      ? "Ví dụ: Từ các tài liệu trong workspace, tổng hợp ưu nhược điểm của phương án này và dẫn nguồn cho từng nhận định."
      : isPlanner
        ? "Ví dụ: Phát triển AI Cá Nhân từ v2.1.2 đến v2.2 theo từng bước an toàn."
        : "Ví dụ: Phân tích những việc tôi đang làm và đề xuất bước tiếp theo.";
    runButton.textContent = isSecurity ? "Kiểm tra mẫu rủi ro" : isReviewer ? "Rà soát nội dung" : isOperator ? "Chuẩn bị thao tác" : isOffice ? "Soạn bản nháp" : isDeveloper ? "Phân tích mã" : isResearch ? "Nghiên cứu tài liệu" : isPlanner ? "Lập kế hoạch" : "Chạy agent";

    [
      ["Vai trò", translatedRole(agent)],
      ["Khả năng", "Xem mô tả chức năng và giới hạn của tác nhân ở phần hướng dẫn"],
      ["Quyền chạy công cụ", agent.toolExecutionEnabled ? "Đã bật" : "Đang tắt"]
    ].forEach(([label, value]) => {
      const item = document.createElement("div");
      const small = document.createElement("span");
      small.textContent = label;
      const strong = document.createElement("strong");
      strong.textContent = value;
      item.append(small, strong);
      meta.append(item);
    });
  }

  async function executeAgent(event) {
    event.preventDefault();
    const agentId = select.value;
    const text = goal.value.trim();
    if (!agentId || !text) return;

    feedback.textContent = "Agent đang xử lý…";
    result.hidden = true;
    runButton.disabled = true;

    try {
      const response = await fetch(
        `/api/agents/${encodeURIComponent(agentId)}/execute`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            ...workspaceHeaders()
          },
          body: JSON.stringify({
            goal: text,
            useKnowledge: document.getElementById("agentUseKnowledge").checked,
            useMemory: document.getElementById("agentUseMemory").checked,
            useTaskContext: document.getElementById("agentUseTasks").checked,
            useLifeContext: document.getElementById("agentUseLife").checked
          })
        });

      const payload = await response.json();
      if (!response.ok) {
        throw new Error(payload.error || "Tác nhân không hoàn thành yêu cầu.");
      }

      feedback.textContent =
        `${translatedNames[payload.agentId] || "Tác nhân AI"} · ${payload.provider === "local" ? "Xử lý tại máy" : "Dịch vụ AI đã cấu hình"} · ${payload.status === "succeeded" ? "Đã hoàn thành" : "Chưa hoàn thành"}`;
      renderExecutionResult(payload);
    } catch (error) {
      feedback.textContent = error instanceof Error
        ? error.message
        : "Tác nhân không hoàn thành yêu cầu.";
    } finally {
      runButton.disabled = agents.length === 0;
    }
  }

  function renderExecutionResult(payload) {
    result.replaceChildren();

    if (payload.security) {
      renderSecurityReview(payload.security);
    } else if (payload.reviewer) {
      renderReviewerReport(payload.reviewer);
    } else if (payload.operator) {
      renderOperatorPlan(payload.operator);
    } else if (payload.office) {
      renderOfficeDraft(payload.office);
    } else if (payload.developer) {
      renderDeveloperReport(payload.developer);
    } else if (payload.research) {
      renderResearchReport(payload.research);
    } else if (payload.plan) {
      renderPlannerPlan(payload.plan);
    } else {
      const copy = document.createElement("div");
      copy.className = "agent-result-copy";
      copy.textContent = payload.message || "Agent không trả về nội dung.";
      result.append(copy);
    }

    result.hidden = false;
  }

  function renderPlannerPlan(plan) {
    const wrapper = document.createElement("section");
    wrapper.className = "planner-plan";

    const heading = document.createElement("div");
    heading.className = "planner-plan-heading";

    const title = document.createElement("h3");
    title.textContent = "Kế hoạch đã chuẩn bị";

    const status = document.createElement("span");
    status.className = "planner-plan-status";
    status.textContent = plan.status || "prepared";

    heading.append(title, status);

    const summary = document.createElement("p");
    summary.className = "planner-plan-summary";
    summary.textContent = plan.summary || "";

    const steps = document.createElement("div");
    steps.className = "planner-plan-steps";

    (plan.steps || []).forEach(step => {
      const card = document.createElement("article");
      card.className = "planner-step";

      const stepHeading = document.createElement("div");
      stepHeading.className = "planner-step-heading";

      const stepTitle = document.createElement("strong");
      stepTitle.textContent = `${step.step}. ${step.title}`;

      const role = document.createElement("span");
      role.className = "planner-step-role";
      role.textContent = step.suggestedRole || "general-assistant";

      stepHeading.append(stepTitle, role);

      const description = document.createElement("p");
      description.textContent = step.description || "";

      const outcome = document.createElement("p");
      outcome.className = "planner-step-outcome";
      outcome.textContent = `Kết quả: ${step.expectedOutcome || ""}`;

      card.append(stepHeading, description, outcome);

      if (Array.isArray(step.dependsOn) && step.dependsOn.length > 0) {
        const dependency = document.createElement("p");
        dependency.className = "planner-step-meta";
        dependency.textContent = `Phụ thuộc bước: ${step.dependsOn.join(", ")}`;
        card.append(dependency);
      }

      if (Array.isArray(step.requiredCapabilities)
          && step.requiredCapabilities.length > 0) {
        const capabilities = document.createElement("p");
        capabilities.className = "planner-step-meta";
        capabilities.textContent =
          `Capabilities: ${step.requiredCapabilities.join(", ")}`;
        card.append(capabilities);
      }

      if (step.requiresUserInput) {
        const userInput = document.createElement("p");
        userInput.className = "planner-step-user-input";
        userInput.textContent = "Cần người dùng cung cấp thêm thông tin.";
        card.append(userInput);
      }

      steps.append(card);
    });

    wrapper.append(heading, summary, steps);
    appendPlannerList(wrapper, "Cần làm rõ", plan.openQuestions);
    appendPlannerList(wrapper, "Giả định", plan.assumptions);
    appendPlannerList(wrapper, "Rủi ro", plan.risks);

    const boundary = document.createElement("p");
    boundary.className = "planner-plan-boundary";
    boundary.textContent =
      "Plan này chỉ được chuẩn bị để bạn xem. v2.2 không tự tạo Task Engine task, không dispatch agent ngoài workflow đã được người dùng chọn, và không persist plan tự động.";
    wrapper.append(boundary);

    result.append(wrapper);
  }

  function renderResearchReport(report) {
    const root = document.createElement("section");
    root.className = "research-report";
    const title = document.createElement("h3");
    title.textContent = "Báo cáo nghiên cứu tài liệu";
    const status = document.createElement("p");
    status.className = "research-status";
    status.textContent = report.status === "grounded"
      ? "Đã truy xuất tài liệu trong workspace"
      : "Chưa đủ bằng chứng để đưa ra kết luận có nguồn";
    const summary = document.createElement("p");
    summary.textContent = report.summary || "";
    root.append(title, status, summary);

    const findings = document.createElement("div");
    findings.className = "research-findings";
    (report.findings || []).forEach(finding => {
      const item = document.createElement("article");
      item.className = "research-finding";
      const statement = document.createElement("p");
      statement.textContent = finding.statement || "";
      const citations = document.createElement("p");
      citations.className = "research-citations";
      citations.textContent = finding.evidenceStatus === "sourced"
        ? (finding.sourceNumbers || []).map(n => "[" + n + "]").join(" ")
        : "Chưa được nguồn hỗ trợ — cần kiểm chứng";
      item.append(statement, citations);
      if (finding.limitation) {
        const limitation = document.createElement("p");
        limitation.textContent = "Giới hạn: " + finding.limitation;
        item.append(limitation);
      }
      findings.append(item);
    });
    root.append(findings);

    const evidence = document.createElement("section");
    evidence.className = "research-evidence";
    const evidenceTitle = document.createElement("h4");
    evidenceTitle.textContent = "Nguồn tài liệu đã truy xuất";
    evidence.append(evidenceTitle);
    (report.evidence || []).forEach(source => {
      const row = document.createElement("p");
      row.textContent = "[" + source.sourceNumber + "] "
        + source.fileName + " · đoạn " + source.chunkIndex
        + (source.pageNumber ? " · trang " + source.pageNumber : "");
      evidence.append(row);
    });
    root.append(evidence);
    appendPlannerList(root, "Câu hỏi chưa có đáp án", report.unansweredQuestions);
    appendPlannerList(root, "Giới hạn nghiên cứu", report.limitations);

    const boundary = document.createElement("p");
    boundary.className = "research-boundary";
    boundary.textContent =
      "Nguồn chỉ được lấy từ tài liệu workspace; không có web search. Chỉ số nguồn được kiểm tra sự tồn tại, không bảo đảm mọi diễn giải của mô hình đều chính xác. Không có tool execution hoặc agent dispatch.";
    root.append(boundary);
    result.append(root);
  }

  function renderDeveloperReport(report) {
    const section = document.createElement("section");
    section.className = "developer-report";
    const heading = document.createElement("h3");
    heading.textContent = "Đề xuất phân tích mã nguồn";
    const status = document.createElement("p");
    status.textContent = report.status === "reviewed"
      ? "Đã phân tích các snippet tìm thấy"
      : "Chưa có đoạn mã phù hợp để đánh giá";
    const summary = document.createElement("p");
    summary.textContent = report.summary || "";
    section.append(heading, status, summary);

    (report.findings || []).forEach(finding => {
      const card = document.createElement("article");
      card.className = "developer-finding";
      const observation = document.createElement("p");
      observation.textContent = finding.observation || "";
      const citations = document.createElement("p");
      citations.textContent = (finding.evidenceNumbers || [])
        .map(n => "[" + n + "]").join(" ");
      const suggested = document.createElement("p");
      suggested.textContent = "Đề xuất (chưa thực hiện): " + (finding.suggestedChange || "");
      const verification = document.createElement("p");
      verification.textContent = "Cách kiểm chứng: " + (finding.verificationStep || "");
      card.append(observation, citations, suggested, verification);
      section.append(card);
    });

    const sources = document.createElement("div");
    const sourceTitle = document.createElement("h4");
    sourceTitle.textContent = "Vị trí mã đã tìm được";
    sources.append(sourceTitle);
    (report.evidence || []).forEach(item => {
      const row = document.createElement("p");
      row.textContent = "[" + item.evidenceNumber + "] "
        + item.path + ":" + item.lineNumber + " — " + item.preview;
      sources.append(row);
    });
    section.append(sources);
    appendPlannerList(section, "Giới hạn", report.limitations);
    const note = document.createElement("p");
    note.textContent = "Chưa sửa file, chạy build/test hay tạo commit. Mã nguồn chỉ được tìm theo từ khóa; trích dẫn vị trí không chứng minh mọi diễn giải của AI là đúng.";
    section.append(note);
    result.append(section);
  }

  function renderOfficeDraft(draft) {
    const root = document.createElement("section");
    root.className = "office-draft";
    const title = document.createElement("h3");
    title.textContent = "Bản nháp văn phòng · " + (draft.kind || "");
    const subject = document.createElement("h4");
    subject.textContent = draft.title || "";
    const body = document.createElement("div");
    body.className = "agent-result-copy";
    body.style.whiteSpace = "pre-wrap";
    body.textContent = draft.body || "";
    root.append(title, subject, body);
    appendPlannerList(root, "Việc đề xuất (chưa thực hiện)", draft.actionItems);
    appendPlannerList(root, "Thông tin cần xác nhận", draft.missingInformation);
    const boundary = document.createElement("p");
    boundary.textContent = "Đây chỉ là bản nháp; hệ thống chưa gửi email, tạo lịch/task, chỉnh sửa hoặc tự lưu tài liệu. Hãy rà soát nội dung trước khi dùng.";
    root.append(boundary);
    result.append(root);
  }

  function renderOperatorPlan(plan) {
    const root = document.createElement("section");
    root.className = "operator-plan";
    const title = document.createElement("h3");
    title.textContent = "Kế hoạch thao tác · chưa thực hiện";
    const summary = document.createElement("p");
    summary.textContent = plan.summary || "";
    root.append(title, summary);

    (plan.steps || []).forEach(step => {
      const card = document.createElement("article");
      card.className = "operator-step";
      const heading = document.createElement("h4");
      heading.textContent = step.step + ". " + (step.title || "");
      const channel = document.createElement("p");
      channel.textContent = "Kênh đề xuất: " + (step.channel || "manual")
        + " · Mức rủi ro: " + (step.riskLevel || "");
      const description = document.createElement("p");
      description.textContent = step.description || "";
      const expected = document.createElement("p");
      expected.textContent = "Kết quả mong đợi (chưa xảy ra): " + (step.expectedOutcome || "");
      const verification = document.createElement("p");
      verification.textContent = "Cách kiểm chứng: " + (step.verificationStep || "");
      card.append(heading, channel, description, expected, verification);
      if ((step.dependsOn || []).length) {
        const dependency = document.createElement("p");
        dependency.textContent = "Phụ thuộc bước: " + step.dependsOn.join(", ");
        card.append(dependency);
      }
      if (step.requiresUserConfirmation) {
        const confirmation = document.createElement("strong");
        confirmation.textContent = "Cần người dùng xác nhận trước khi thực hiện bước này; chưa có quyền thực thi.";
        card.append(confirmation);
      }
      root.append(card);
    });

    appendPlannerList(root, "Điều kiện trước khi thao tác", plan.preconditions);
    appendPlannerList(root, "Cần làm rõ", plan.openQuestions);
    appendPlannerList(root, "Giới hạn", plan.limitations);
    const boundary = document.createElement("p");
    boundary.textContent = "Bản kế hoạch này không có nút thực thi. Không điều khiển máy/browser, gọi connector, gửi tin, lưu kế hoạch hoặc dispatch agent.";
    root.append(boundary);
    result.append(root);
  }

  function renderReviewerReport(report) {
    const root = document.createElement("section");
    root.className = "reviewer-report";
    const heading = document.createElement("h3");
    heading.textContent = "Bản rà soát · chưa phê duyệt";
    const status = document.createElement("p");
    status.textContent = report.status === "insufficient-material"
      ? "Chưa có đủ nội dung để rà soát. Hãy dán đoạn văn đầy đủ."
      : "Đã rà soát nội dung được dán; vẫn cần người dùng đối chiếu.";
    const summary = document.createElement("p");
    summary.textContent = report.summary || "";
    root.append(heading, status, summary);

    (report.findings || []).forEach(item => {
      const card = document.createElement("article");
      card.className = "reviewer-finding";
      const title = document.createElement("h4");
      title.textContent = (item.kind || "review") + " — " + (item.observation || "");
      const excerpt = document.createElement("p");
      excerpt.textContent = "Trích nguyên văn: “" + (item.evidenceExcerpt || "") + "”";
      const suggestion = document.createElement("p");
      suggestion.textContent = "Đề xuất (chưa sửa): " + (item.suggestedRevision || "");
      const verification = document.createElement("p");
      verification.textContent = "Cách kiểm chứng: " + (item.verificationStep || "");
      card.append(title, excerpt, suggestion, verification);
      root.append(card);
    });

    appendPlannerList(root, "Câu hỏi cần làm rõ", report.questions);
    appendPlannerList(root, "Giới hạn", report.limitations);
    const boundary = document.createElement("p");
    boundary.textContent = "Chỉ kiểm tra trích đoạn có trong nội dung dán; chưa xác minh sự thật bên ngoài, chạy test, chỉnh sửa tài liệu hoặc phê duyệt kết quả.";
    root.append(boundary);
    result.append(root);
  }

  function renderSecurityReview(review) {
    const root = document.createElement("section");
    root.className = "security-review";
    const heading = document.createElement("h3");
    heading.textContent = "Rà soát mẫu bảo mật · không phải phê duyệt";
    const summary = document.createElement("p");
    summary.textContent = review.summary || "";
    root.append(heading, summary);

    (review.findings || []).forEach(item => {
      const card = document.createElement("article");
      card.className = "security-finding";
      const title = document.createElement("h4");
      title.textContent = (item.title || "") + " (" + (item.severity || "") + ")";
      const rule = document.createElement("p");
      rule.textContent = "Mẫu: " + (item.ruleId || "");
      const explanation = document.createElement("p");
      explanation.textContent = item.explanation || "";
      const check = document.createElement("p");
      check.textContent = "Cần kiểm tra thủ công: " + (item.suggestedCheck || "");
      card.append(title, rule, explanation, check);
      root.append(card);
    });

    appendPlannerList(root, "Giới hạn", review.limitations);
    const boundary = document.createElement("p");
    boundary.textContent = "Kết quả chỉ dựa trên mẫu trong nội dung nhập, không echo nội dung hoặc gửi tới AI provider. Không phát hiện mẫu không có nghĩa an toàn. Không thay đổi quyền, thực thi hoặc chặn thao tác.";
    root.append(boundary);
    result.append(root);
  }

  function appendPlannerList(parent, title, values) {
    if (!Array.isArray(values) || values.length === 0) return;

    const section = document.createElement("section");
    section.className = "planner-plan-list";

    const heading = document.createElement("strong");
    heading.textContent = title;

    const list = document.createElement("ul");
    values.forEach(value => {
      const item = document.createElement("li");
      item.textContent = value;
      list.append(item);
    });

    section.append(heading, list);
    parent.append(section);
  }

  function workspaceHeaders() {
    return {
      "X-PersonalAI-Workspace":
        window.PersonalAiWorkspace?.currentId || "personal"
    };
  }
})();
