(() => {
  const VERSION = "2.1.1";
  let dialog;
  let select;
  let goal;
  let feedback;
  let result;
  let meta;
  let runButton;
  let agents = [];

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
    label.textContent = "Agents";

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
    eyebrow.textContent = "AGENT FRAMEWORK · v2.1";
    const title = document.createElement("h2");
    title.id = "agentTitle";
    title.textContent = "Chạy agent";
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
    status.textContent = "Đang đọc Agent Framework…";

    const grid = document.createElement("div");
    grid.className = "agent-grid";

    const labelSelect = document.createElement("label");
    labelSelect.className = "field-label";
    labelSelect.setAttribute("for", "agentSelect");
    labelSelect.textContent = "Agent";

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
      ["agentUseKnowledge", "Knowledge", true],
      ["agentUseMemory", "Memory", true],
      ["agentUseTasks", "Tasks", true],
      ["agentUseLife", "Life Context", true]
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
      "v2.1.1 chỉ chạy agent khi bạn chủ động bấm chạy. Planner chỉ chia goal thành plan; không tạo task thật, không dispatch agent, không tự chạy tool và không tự xác nhận side effect.";

    feedback = document.createElement("div");
    feedback.className = "agent-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    runButton = document.createElement("button");
    runButton.className = "primary-button";
    runButton.type = "submit";
    runButton.textContent = "Chạy agent";
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
        throw new Error("Không đọc được Agent Framework.");
      }

      const status = await statusResponse.json();
      const catalog = await catalogResponse.json();
      agents = Array.isArray(catalog.agents) ? catalog.agents : [];

      statusNode.textContent =
        `${agents.length} agent · explicit invocation · tool execution: ${status.toolExecutionEnabled ? "bật" : "tắt"} · tiếp theo: ${status.nextStage}`;

      select.replaceChildren();
      agents.forEach(agent => {
        const option = document.createElement("option");
        option.value = agent.id;
        option.textContent = `${agent.name} · ${agent.role}`;
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
        : "Không đọc được Agent Framework.";
      select.disabled = true;
      runButton.disabled = true;
    }
  }

  function renderAgentMeta() {
    const agent = agents.find(item => item.id === select.value);
    meta.replaceChildren();
    if (!agent) return;

    const isPlanner = agent.role === "planner";
    goal.placeholder = isPlanner
      ? "Ví dụ: Phát triển AI Cá Nhân từ v2.1.1 đến v2.2 theo từng bước an toàn."
      : "Ví dụ: Phân tích những việc tôi đang làm và đề xuất bước tiếp theo.";
    runButton.textContent = isPlanner ? "Lập kế hoạch" : "Chạy agent";

    [
      ["Role", agent.role],
      ["Capabilities", (agent.capabilities || []).join(", ")],
      ["Bound tools", (agent.tools || []).join(", ") || "Không có"]
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
        throw new Error(payload.error || "Agent execution thất bại.");
      }

      feedback.textContent =
        `${payload.agentName} · ${payload.provider || "local"} · ${payload.status}`;
      renderExecutionResult(payload);
    } catch (error) {
      feedback.textContent = error instanceof Error
        ? error.message
        : "Agent execution thất bại.";
    } finally {
      runButton.disabled = agents.length === 0;
    }
  }

  function renderExecutionResult(payload) {
    result.replaceChildren();

    if (payload.plan) {
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
      "Plan này chỉ được chuẩn bị để bạn xem. v2.1.1 không tự tạo Task Engine task, không dispatch agent và không persist plan tự động.";
    wrapper.append(boundary);

    result.append(wrapper);
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
