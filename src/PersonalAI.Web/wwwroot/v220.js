(() => {
  const VERSION = "2.2.4";

  document.addEventListener("DOMContentLoaded", () => {
    const card = document.querySelector("#agentDialog .agent-card");
    if (!card || document.getElementById("workflowPanel")) return;

    const panel = document.createElement("details");
    panel.id = "workflowPanel";
    panel.className = "workflow-panel";
    panel.innerHTML = '<summary>Quy trình nhiều bước · v' + VERSION + ' · Thực hiện theo thứ tự</summary>'
      + '<p>Chọn trước 2–3 tác nhân AI và mục tiêu từng bước. Dữ liệu chuyển giao tối đa 1.600 ký tự và sẽ bị chặn nếu phát hiện mẫu thông tin xác thực. Mỗi bước giới hạn thời gian, không tự chạy công cụ.</p>'
      + '<form id="workflowForm">'
      + '<div class="workflow-step"><label for="wfAgent1">Bước 1 · Tác nhân AI</label><select id="wfAgent1" required></select>'
      + '<label for="wfGoal1">Mục tiêu bước 1</label><textarea id="wfGoal1" maxlength="4000" required></textarea></div>'
      + '<div class="workflow-step"><label for="wfAgent2">Bước 2 · Tác nhân AI</label><select id="wfAgent2" required></select>'
      + '<label for="wfGoal2">Mục tiêu bước 2</label><textarea id="wfGoal2" maxlength="4000" required></textarea>'
      + '<label class="workflow-toggle"><input type="checkbox" id="wfTransfer2">Cho phép chuyển tối đa 1.600 ký tự từ kết quả bước 1 sang bước 2</label></div>'
      + '<label class="workflow-toggle"><input type="checkbox" id="wfStep3">Thêm bước 3</label>'
      + '<div class="workflow-step" id="wfThird" hidden><label for="wfAgent3">Bước 3 · Tác nhân AI</label><select id="wfAgent3"></select>'
      + '<label for="wfGoal3">Mục tiêu bước 3</label><textarea id="wfGoal3" maxlength="4000"></textarea>'
      + '<label class="workflow-toggle"><input type="checkbox" id="wfTransfer3">Cho phép chuyển tối đa 1.600 ký tự từ kết quả bước 2 sang bước 3</label></div>'
      + '<fieldset class="workflow-context"><legend>Nguồn dữ liệu bổ sung (mặc định tắt; chỉ bật nguồn cần thiết)</legend>'
      + '<label><input type="checkbox" id="wfKnowledge"> Tài liệu của bạn</label>'
      + '<label><input type="checkbox" id="wfMemory"> Thông tin đã ghi nhớ</label>'
      + '<label><input type="checkbox" id="wfTasks"> Công việc</label>'
      + '<label><input type="checkbox" id="wfLife"> Thông tin cuộc sống</label></fieldset>'
      + '<label class="workflow-toggle"><input type="checkbox" id="wfConfirm" required>Tôi đã chọn và xác nhận toàn bộ các bước; các đầu ra có thể được gửi đến dịch vụ AI của tác nhân được chọn.</label>'
      + '<p class="workflow-boundary">Quy trình giới hạn tối đa 120 giây, mỗi bước tối đa 50 giây (dịch vụ AI cần hỗ trợ huỷ yêu cầu để dừng đúng hạn). Các bước có thể gửi nội dung tới dịch vụ AI; không nhập bí mật. Bộ lọc mẫu thông tin xác thực không thay thế kiểm tra dữ liệu thủ công. Chỉ chuyển dữ liệu sau khi người dùng xem và duyệt chính xác đầu ra của bước trước ở bước kiểm tra riêng. Không tự chọn tác nhân AI, chạy song song hoặc sử dụng công cụ.</p>'
      + '<button class="primary-button" id="wfRun" type="submit" disabled>Chạy quy trình đã xác nhận</button>'
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
    const vietnameseAgents = {
      "core.personal-assistant": "Trợ lý cá nhân",
      "planning.planner": "Tác nhân lập kế hoạch",
      "research.researcher": "Tác nhân nghiên cứu",
      "development.developer": "Tác nhân phân tích mã",
      "office.office-assistant": "Tác nhân văn phòng",
      "operations.operator": "Tác nhân lập kế hoạch thao tác",
      "quality.reviewer": "Tác nhân rà soát",
      "security.security-reviewer": "Tác nhân kiểm tra bảo mật"
    };
    const displayAgent = agentId => vietnameseAgents[agentId] || "Tác nhân AI";

    const field = id => panel.querySelector("#" + id);
    const bool = id => field(id).checked;

    let pendingReview = false;

    async function presentWorkflow(payload) {
      results.replaceChildren();
      pendingReview = payload.status === "awaiting-review";
      run.disabled = !loaded || pendingReview;
      feedback.textContent = pendingReview
        ? "Quy trình đã tạm dừng: xem đúng dữ liệu trước khi đồng ý chuyển sang tác nhân tiếp theo."
        : payload.status === "completed"
          ? "Đã hoàn tất " + payload.completedSteps.length + " bước. Không có tự chạy công cụ."
          : payload.status === "timed-out"
            ? "Quy trình dừng vì hết thời gian ở bước " + (payload.failedStep || "?") + "."
            : "Quy trình dừng tại bước " + (payload.failedStep || "?") + "; không chạy các bước sau.";

      (payload.completedSteps || []).forEach(item => {
        const section = document.createElement("article");
        section.className = "workflow-step-result";
        const heading = document.createElement("h4");
        heading.textContent = "Bước " + item.step + " · " + displayAgent(item.agentId)
          + (item.previousOutputTransferred ? " · Đã duyệt và chuyển kết quả bước trước" : "");
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

      const checkpoint = payload.handoffCheckpoint;
      if (pendingReview && checkpoint) {
        const section = document.createElement("section");
        section.className = "workflow-step-result";
        const heading = document.createElement("h4");
        heading.textContent = "Duyệt dữ liệu trước khi chuyển đến bước "
          + checkpoint.receivingStep + " · " + displayAgent(checkpoint.receivingAgentId);
        const warning = document.createElement("p");
        warning.textContent = "Đây là đúng nội dung (tối đa 1.600 ký tự) sẽ chuyển sang bước tiếp theo. Nội dung có thể được gửi tới dịch vụ AI đã cấu hình. Bộ lọc mẫu không nhận diện được mọi bí mật. Chỉ tiếp tục sau khi bạn đã đọc và đồng ý chia sẻ.";
        const preview = document.createElement("pre");
        preview.className = "workflow-step-output";
        preview.textContent = checkpoint.preview || "";
        const consent = document.createElement("label");
        consent.className = "workflow-toggle";
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        consent.append(checkbox, document.createTextNode(
          " Tôi đã xem đúng nội dung ở trên và đồng ý chuyển nội dung này sang bước kế tiếp."));
        const approve = document.createElement("button");
        approve.type = "button";
        approve.textContent = "Duyệt và chạy bước kế tiếp";
        approve.disabled = true;
        const decline = document.createElement("button");
        decline.type = "button";
        decline.textContent = "Dừng quy trình, không chuyển dữ liệu";
        checkbox.addEventListener("change", () => {
          approve.disabled = !checkbox.checked;
        });

        async function decide(approved) {
          approve.disabled = true;
          decline.disabled = true;
          feedback.textContent = approved
            ? "Đang chạy bước đã được bạn duyệt…"
            : "Đang dừng quy trình…";
          try {
            const response = await fetch("/api/agents/orchestration/resume", {
              method: "POST",
              headers: {
                "Content-Type": "application/json",
                "X-PersonalAI-Workspace": window.PersonalAiWorkspace?.currentId || "personal"
              },
              body: JSON.stringify({
                workflowId: checkpoint.workflowId,
                reviewToken: checkpoint.reviewToken,
                previewDigest: checkpoint.previewDigest,
                approveTransfer: approved
              })
            });
            const next = await response.json();
            if (!response.ok) throw new Error(next.error || "Không thể duyệt dữ liệu chuyển giao.");
            await presentWorkflow(next);
          } catch (error) {
            feedback.textContent = error instanceof Error ? error.message : "Không thể duyệt.";
            decline.disabled = false;
            approve.disabled = !checkbox.checked;
          }
        }

        approve.addEventListener("click", () => { if (checkbox.checked) void decide(true); });
        decline.addEventListener("click", () => { void decide(false); });
        section.append(heading, warning, preview, consent, approve, decline);
        results.append(section);
      } else if (pendingReview) {
        pendingReview = false;
        feedback.textContent = "Thiếu dữ liệu xác nhận: quy trình không được tiếp tục.";
      }

      // Chẩn đoán chỉ đọc siêu dữ liệu thuộc không gian làm việc hiện tại.
      // Không lấy mục tiêu, đầu ra hoặc mã duyệt từ API chẩn đoán.
      if (payload.workflowId) {
        try {
          const diagnosticResponse = await fetch(
            "/api/agents/orchestration/diagnostics/" + encodeURIComponent(payload.workflowId),
            { headers: { "X-PersonalAI-Workspace": window.PersonalAiWorkspace?.currentId || "personal" } }
          );
          if (diagnosticResponse.ok) {
            const diagnosis = await diagnosticResponse.json();
            const details = document.createElement("section");
            details.className = "workflow-step-result";
            const title = document.createElement("h4");
            title.textContent = "Giải thích trạng thái: " + (diagnosis.statusLabel || "Chưa xác định");
            const summary = document.createElement("p");
            summary.textContent = diagnosis.summary || "";
            details.append(title, summary);
            if (diagnosis.stopReasonLabel) {
              const reason = document.createElement("p");
              reason.textContent = "Lý do dừng: " + diagnosis.stopReasonLabel;
              details.append(reason);
            }
            (diagnosis.suggestedChecks || []).forEach(item => {
              const paragraph = document.createElement("p");
              paragraph.textContent = "Gợi ý: " + item;
              details.append(paragraph);
            });
            results.append(details);
          }
        } catch {
          // Chẩn đoán là chức năng đọc thêm, không thay đổi kết quả quy trình.
        }
      }

      results.hidden = false;
      run.disabled = !loaded || pendingReview;
    }

    thirdCheckbox.addEventListener("change", () => {
      third.hidden = !thirdCheckbox.checked;
      field("wfAgent3").required = thirdCheckbox.checked;
      field("wfGoal3").required = thirdCheckbox.checked;
      if (!thirdCheckbox.checked) field("wfTransfer3").checked = false;
    });

    panel.addEventListener("toggle", async () => {
      if (!panel.open || loaded) return;
      run.disabled = true;
      feedback.textContent = "Đang tải danh sách tác nhân AI…";
      try {
        const response = await fetch("/api/agents", {
          headers: { "X-PersonalAI-Workspace": window.PersonalAiWorkspace?.currentId || "personal" }
        });
        if (!response.ok) throw new Error("Không tải được danh sách tác nhân AI.");
        const catalog = await response.json();
        const agents = Array.isArray(catalog.agents) ? catalog.agents : [];
        if (!agents.length) throw new Error("Chưa có tác nhân AI nào có thể sử dụng.");
        ["wfAgent1", "wfAgent2", "wfAgent3"].forEach(id => {
          const select = field(id);
          select.replaceChildren();
          const blank = document.createElement("option");
          blank.value = "";
          blank.textContent = "Chọn tác nhân AI";
          select.append(blank);
          agents.forEach(agent => {
            const option = document.createElement("option");
            option.value = agent.id;
            option.textContent = displayAgent(agent.id);
            select.append(option);
          });
        });
        loaded = true;
        run.disabled = false;
        feedback.textContent = "Chọn các bước, kiểm tra nội dung và xác nhận trước khi chạy.";
      } catch (error) {
        feedback.textContent = error instanceof Error ? error.message : "Không tải được danh sách tác nhân AI.";
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
      feedback.textContent = "Đang chạy các bước đã xác nhận theo thứ tự…";
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
        if (!response.ok) throw new Error(payload.error || "Quy trình chưa hoàn tất.");

        await presentWorkflow(payload);
      } catch (error) {
        feedback.textContent = error instanceof Error ? error.message : "Không thể chạy quy trình.";
      } finally {
        run.disabled = !loaded || pendingReview;
      }
    });
  });
})();
