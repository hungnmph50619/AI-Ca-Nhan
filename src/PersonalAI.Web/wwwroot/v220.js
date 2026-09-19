(() => {
  const VERSION = "2.2.0";

  document.addEventListener("DOMContentLoaded", () => {
    const card = document.querySelector("#agentDialog .agent-card");
    if (!card || document.getElementById("workflowPanel")) return;

    const panel = document.createElement("details");
    panel.id = "workflowPanel";
    panel.className = "workflow-panel";
    panel.innerHTML = '<summary>Workflows · v' + VERSION + ' · Điều phối tuần tự</summary>'
      + '<p>Chọn trước 2–3 agent và mục tiêu của từng bước. Chỉ chuyển kết quả khi bạn tích chọn ở bước nhận. Không tự gọi tool hoặc thực hiện thao tác bên ngoài.</p>'
      + '<form id="workflowForm">'
      + '<div class="workflow-step"><label for="wfAgent1">Bước 1 · Agent</label><select id="wfAgent1" required></select>'
      + '<label for="wfGoal1">Mục tiêu bước 1</label><textarea id="wfGoal1" maxlength="4000" required></textarea></div>'
      + '<div class="workflow-step"><label for="wfAgent2">Bước 2 · Agent</label><select id="wfAgent2" required></select>'
      + '<label for="wfGoal2">Mục tiêu bước 2</label><textarea id="wfGoal2" maxlength="4000" required></textarea>'
      + '<label class="workflow-toggle"><input type="checkbox" id="wfTransfer2">Cho phép chuyển tối đa 1.600 ký tự từ kết quả bước 1 sang bước 2</label></div>'
      + '<label class="workflow-toggle"><input type="checkbox" id="wfStep3">Thêm bước 3</label>'
      + '<div class="workflow-step" id="wfThird" hidden><label for="wfAgent3">Bước 3 · Agent</label><select id="wfAgent3"></select>'
      + '<label for="wfGoal3">Mục tiêu bước 3</label><textarea id="wfGoal3" maxlength="4000"></textarea>'
      + '<label class="workflow-toggle"><input type="checkbox" id="wfTransfer3">Cho phép chuyển tối đa 1.600 ký tự từ kết quả bước 2 sang bước 3</label></div>'
      + '<fieldset class="workflow-context"><legend>Context bổ sung (mặc định tắt; chỉ bật nguồn cần thiết)</legend>'
      + '<label><input type="checkbox" id="wfKnowledge"> Knowledge</label>'
      + '<label><input type="checkbox" id="wfMemory"> Memory</label>'
      + '<label><input type="checkbox" id="wfTasks"> Tasks</label>'
      + '<label><input type="checkbox" id="wfLife"> Life context</label></fieldset>'
      + '<label class="workflow-toggle"><input type="checkbox" id="wfConfirm" required>Tôi đã chọn và xác nhận toàn bộ các bước; các đầu ra có thể được gửi đến AI provider của agent được chọn.</label>'
      + '<p class="workflow-boundary">Các bước có thể dùng AI provider đã cấu hình; không nhập bí mật. Chuyển đầu ra chỉ được thực hiện khi bạn bật checkbox ở từng bước. Không có agent tự chọn, task queue, chạy song song hay tool execution.</p>'
      + '<button class="primary-button" id="wfRun" type="submit" disabled>Chạy workflow đã xác nhận</button>'
      + '<p id="wfFeedback" role="status" aria-live="polite"></p>'
      + '</form><section id="wfResults" class="workflow-results" hidden></section>';

    card.append(panel);
    const form = panel.querySelector("#workflowForm");
    const feedback = panel.querySelector("#wfFeedback");
    const results = panel.querySelector("#wfResults");
    const run = panel.querySelector("#wfRun");
    const third = panel.querySelector("#wfThird");
    const thirdCheckbox = panel.querySelector("#wfStep3");
    let loaded = false;

    const field = id => panel.querySelector("#" + id);
    const bool = id => field(id).checked;

    thirdCheckbox.addEventListener("change", () => {
      third.hidden = !thirdCheckbox.checked;
      field("wfAgent3").required = thirdCheckbox.checked;
      field("wfGoal3").required = thirdCheckbox.checked;
      if (!thirdCheckbox.checked) field("wfTransfer3").checked = false;
    });

    panel.addEventListener("toggle", async () => {
      if (!panel.open || loaded) return;
      run.disabled = true;
      feedback.textContent = "Đang đọc danh sách agent…";
      try {
        const response = await fetch("/api/agents", {
          headers: { "X-PersonalAI-Workspace": window.PersonalAiWorkspace?.currentId || "personal" }
        });
        if (!response.ok) throw new Error("Không đọc được danh sách agent.");
        const catalog = await response.json();
        const agents = Array.isArray(catalog.agents) ? catalog.agents : [];
        if (!agents.length) throw new Error("Chưa có agent nào khả dụng.");
        ["wfAgent1", "wfAgent2", "wfAgent3"].forEach(id => {
          const select = field(id);
          select.replaceChildren();
          const blank = document.createElement("option");
          blank.value = "";
          blank.textContent = "Chọn agent";
          select.append(blank);
          agents.forEach(agent => {
            const option = document.createElement("option");
            option.value = agent.id;
            option.textContent = agent.name + " · " + agent.role;
            select.append(option);
          });
        });
        loaded = true;
        run.disabled = false;
        feedback.textContent = "Chọn các bước và xác nhận trước khi chạy.";
      } catch (error) {
        feedback.textContent = error instanceof Error ? error.message : "Không đọc được agent.";
      }
    });

    form.addEventListener("submit", async event => {
      event.preventDefault();
      if (!loaded || !bool("wfConfirm")) return;

      const step = n => ({
        agentId: field("wfAgent" + n).value,
        goal: field("wfGoal" + n).value.trim(),
        includePreviousOutput: n > 1 && bool("wfTransfer" + n)
      });
      const steps = [step(1), step(2)];
      if (thirdCheckbox.checked) steps.push(step(3));

      run.disabled = true;
      results.hidden = true;
      results.replaceChildren();
      feedback.textContent = "Đang chạy tuần tự các bước đã xác nhận…";
      try {
        const response = await fetch("/api/agents/orchestration/execute", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-PersonalAI-Workspace": window.PersonalAiWorkspace?.currentId || "personal"
          },
          body: JSON.stringify({
            steps,
            confirmSelectedWorkflow: true,
            useKnowledge: bool("wfKnowledge"),
            useMemory: bool("wfMemory"),
            useTaskContext: bool("wfTasks"),
            useLifeContext: bool("wfLife")
          })
        });
        const payload = await response.json();
        if (!response.ok) throw new Error(payload.error || "Workflow không hoàn tất.");

        feedback.textContent = payload.status === "completed"
          ? "Đã hoàn tất " + payload.completedSteps.length + " bước. Không có tool execution."
          : "Workflow đã dừng tại bước " + (payload.failedStep || "?") + "; các bước sau không chạy.";

        (payload.completedSteps || []).forEach(item => {
          const section = document.createElement("article");
          section.className = "workflow-step-result";
          const heading = document.createElement("h4");
          heading.textContent = "Bước " + item.step + " · " + item.agentId
            + (item.previousOutputTransferred ? " · Đã chuyển đầu ra trước" : "");
          const body = document.createElement("pre");
          body.className = "workflow-step-output";
          body.textContent = item.result?.message || "Không có nội dung trả về.";
          section.append(heading, body);
          results.append(section);
        });
        if (payload.error) {
          const error = document.createElement("p");
          error.textContent = payload.error;
          results.append(error);
        }
        results.hidden = false;
      } catch (error) {
        feedback.textContent = error instanceof Error ? error.message : "Không thể chạy workflow.";
      } finally {
        run.disabled = !loaded;
      }
    });
  });
})();
