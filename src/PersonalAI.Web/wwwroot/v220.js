(() => {
  const VERSION = "2.2.6";

  document.addEventListener("DOMContentLoaded", () => {
    const card = document.querySelector("#agentDialog .agent-card");
    if (!card || document.getElementById("workflowPanel")) return;

    const panel = document.createElement("details");
    panel.id = "workflowPanel";
    panel.className = "workflow-panel";
    panel.lang = "vi";
    panel.setAttribute("aria-label", "Thiết lập quy trình nhiều bước");
    panel.innerHTML = '<summary id="wfSummary" aria-controls="workflowForm">Quy trình nhiều bước · v' + VERSION + ' · Thực hiện theo thứ tự</summary>'
      + '<p id="wfInstructions">Chọn trước 2–3 tác nhân AI và mục tiêu từng bước. Dùng phím Tab để di chuyển giữa các ô. Dữ liệu chuyển giao tối đa 1.600 ký tự và sẽ bị chặn nếu phát hiện mẫu thông tin xác thực. Mỗi bước giới hạn thời gian, không tự chạy công cụ.</p>'
      + '<form id="workflowForm" aria-describedby="wfInstructions wfBoundary">'
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
      + '<button type="button" id="wfPreview">Xem trước các bước và nguồn dữ liệu</button>'
      + '<section id="wfPreviewResult" class="workflow-step-result" aria-live="polite" hidden></section>'
      + '<label class="workflow-toggle"><input type="checkbox" id="wfConfirm" required disabled>Tôi đã đọc bản xem trước và đồng ý chạy đúng các bước, mục tiêu và nguồn dữ liệu được hiển thị.</label>'
      + '<p class="workflow-boundary" id="wfBoundary">Quy trình giới hạn tối đa 120 giây, mỗi bước tối đa 50 giây (dịch vụ AI cần hỗ trợ huỷ yêu cầu để dừng đúng hạn). Các bước có thể gửi nội dung tới dịch vụ AI; không nhập bí mật. Bộ lọc mẫu thông tin xác thực không thay thế kiểm tra dữ liệu thủ công. Chỉ chuyển dữ liệu sau khi người dùng xem và duyệt chính xác đầu ra của bước trước ở bước kiểm tra riêng. Không tự chọn tác nhân AI, chạy song song hoặc sử dụng công cụ.</p>'
      + '<button class="primary-button" id="wfRun" type="submit" disabled>Chạy quy trình đã xác nhận</button>'
      + '<p id="wfFeedback" role="status" aria-live="polite" aria-atomic="true" tabindex="-1"></p>'
      + '</form><section id="wfResults" class="workflow-results" role="region" aria-label="Kết quả quy trình" hidden></section>';

    card.append(panel);
    const form = panel.querySelector("#workflowForm");
    const feedback = panel.querySelector("#wfFeedback");
    const results = panel.querySelector("#wfResults");
    const run = panel.querySelector("#wfRun");
    const previewButton = panel.querySelector("#wfPreview");
    const previewResult = panel.querySelector("#wfPreviewResult");
    const third = panel.querySelector("#wfThird");
    const thirdCheckbox = panel.querySelector("#wfStep3");
    panel.querySelector("#wfPreviewResult").setAttribute("aria-label", "Bản xem trước các bước");
    panel.querySelector("#wfPreviewResult").setAttribute("role", "region");
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

    // Thông báo lỗi có thể nhận tiêu điểm để người dùng dùng bàn phím và trình đọc màn hình biết lý do.
    function showError(message) {
      feedback.setAttribute("role", "alert");
      feedback.textContent = message;
      feedback.focus();
    }
    function showStatus(message) {
      feedback.setAttribute("role", "status");
      feedback.textContent = message;
    }

    form.addEventListener("invalid", event => {
      const control = event.target;
      if (control instanceof HTMLSelectElement) control.setCustomValidity("Hãy chọn tác nhân AI cho bước này.");
      else if (control instanceof HTMLTextAreaElement) control.setCustomValidity("Hãy nhập mục tiêu cho bước này.");
      else if (control.id === "wfConfirm") control.setCustomValidity("Hãy xem trước và xác nhận cấu hình trước khi chạy.");
    }, true);
    form.addEventListener("input", event => event.target.setCustomValidity?.(""), true);
    form.addEventListener("change", event => event.target.setCustomValidity?.(""), true);

    let pendingReview = false;
    let checkedConfiguration = null;
    const currentWorkspace = () => window.PersonalAiWorkspace?.currentId || "personal";

    const currentConfiguration = () => {
      const step = n => ({
        agentId: field("wfAgent" + n).value,
        goal: field("wfGoal" + n).value.trim(),
        includePreviousOutput: n > 1 && bool("wfTransfer" + n)
      });
      const steps = [step(1), step(2)];
      if (thirdCheckbox.checked) steps.push(step(3));
      return {
        steps,
        useKnowledge: bool("wfKnowledge"),
        useMemory: bool("wfMemory"),
        useTaskContext: bool("wfTasks"),
        useLifeContext: bool("wfLife")
      };
    };

    function invalidatePreview() {
      checkedConfiguration = null;
      field("wfConfirm").checked = false;
      field("wfConfirm").disabled = true;
      previewResult.replaceChildren();
      previewResult.hidden = true;
      run.disabled = true;
      if (loaded && !pendingReview) feedback.textContent = "Cấu hình đã thay đổi: hãy xem trước và xác nhận lại trước khi chạy.";
    }

    form.addEventListener("input", event => {
      if (event.target?.id !== "wfConfirm") invalidatePreview();
    });
    form.addEventListener("change", event => {
      if (event.target?.id !== "wfConfirm") invalidatePreview();
    });
    field("wfConfirm").addEventListener("change", () => {
      run.disabled = !loaded || pendingReview || !checkedConfiguration || !bool("wfConfirm");
    });

    previewButton.addEventListener("click", async () => {
      if (!loaded || pendingReview) return;
      // Không bắt buộc xác nhận lại mới được xem trước; chỉ kiểm tra các ô nhập mục tiêu và tác nhân.
      for (const control of form.querySelectorAll("select[required], textarea[required]")) {
        if (!control.reportValidity()) return;
      }
      previewButton.disabled = true;
      invalidatePreview();
      showStatus("Đang kiểm tra cấu hình, chưa chạy tác nhân AI…");
      panel.setAttribute("aria-busy", "true");
      try {
        const workspaceId = currentWorkspace();
        const config = currentConfiguration();
        const snapshot = JSON.stringify(config);
        const response = await fetch("/api/agents/orchestration/preview", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-PersonalAI-Workspace": workspaceId
          },
          body: snapshot
        });
        const preview = await response.json();
        if (!response.ok) throw new Error(preview.error || "Không xem trước được quy trình.");
        if (snapshot !== JSON.stringify(currentConfiguration()) || workspaceId !== currentWorkspace())
          throw new Error("Cấu hình đã thay đổi trong lúc kiểm tra. Hãy xem trước lại.");
        checkedConfiguration = { workspaceId, snapshot, digest: preview.configurationDigest };
        const title = document.createElement("h4");
        title.tabIndex = -1;
        title.textContent = "Bản xem trước — chưa chạy tác nhân AI";
        previewResult.append(title);
        (preview.steps || []).forEach(item => {
          const paragraph = document.createElement("p");
          const text = config.steps[item.step - 1];
          paragraph.textContent = "Bước " + item.step + " · " + displayAgent(item.agentId)
            + " · Mục tiêu: " + text.goal
            + (item.includePreviousOutput ? " · Có đề nghị chuyển đầu ra bước trước (cần duyệt riêng)" : " · Không chuyển đầu ra")
            + (item.canUseAiProvider ? " · Có thể dùng dịch vụ AI đã cấu hình" : " · Xử lý cục bộ");
          previewResult.append(paragraph);
        });
        const sources = document.createElement("p");
        sources.textContent = "Nguồn bổ sung được chọn: "
          + ([config.useKnowledge && "Tài liệu", config.useMemory && "Thông tin ghi nhớ",
              config.useTaskContext && "Công việc", config.useLifeContext && "Thông tin cuộc sống"]
              .filter(Boolean).join(", ") || "Không có");
        const warning = document.createElement("p");
        warning.textContent = "Bạn đang xem đúng cấu hình sẽ được gửi đi. Nếu đổi bất kỳ bước hay nguồn dữ liệu nào, phải xem trước và xác nhận lại. Bản xem trước không cấp thêm quyền thực thi công cụ.";
        previewResult.append(sources, warning);
        previewResult.hidden = false;
        field("wfConfirm").disabled = false;
        showStatus("Đã tạo bản xem trước. Hãy đọc kỹ rồi tích xác nhận để chạy.");
        title.focus();
      } catch (error) {
        showError(error instanceof Error ? error.message : "Không xem trước được quy trình.");
      } finally {
        previewButton.disabled = !loaded;
        panel.removeAttribute("aria-busy");
      }
    });

    async function presentWorkflow(payload) {
      results.replaceChildren();
      pendingReview = payload.status === "awaiting-review";
      run.disabled = !loaded || pendingReview || !checkedConfiguration || !bool("wfConfirm");
      showStatus(pendingReview
        ? "Quy trình đã tạm dừng: xem đúng dữ liệu trước khi đồng ý chuyển sang tác nhân tiếp theo."
        : payload.status === "completed"
          ? "Đã hoàn tất " + payload.completedSteps.length + " bước. Không có tự chạy công cụ."
          : payload.status === "timed-out"
            ? "Quy trình dừng vì hết thời gian ở bước " + (payload.failedStep || "?") + "."
            : "Quy trình dừng tại bước " + (payload.failedStep || "?") + "; không chạy các bước sau.");

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
      let reviewHeading = null;
      if (pendingReview && checkpoint) {
        const section = document.createElement("section");
        section.className = "workflow-step-result";
        const heading = document.createElement("h4");
        heading.textContent = "Duyệt dữ liệu trước khi chuyển đến bước "
          + checkpoint.receivingStep + " · " + displayAgent(checkpoint.receivingAgentId);
        heading.tabIndex = -1;
        reviewHeading = heading;
        const warning = document.createElement("p");
        warning.textContent = "Đây là đúng nội dung (tối đa 1.600 ký tự) sẽ chuyển sang bước tiếp theo. Nội dung có thể được gửi tới dịch vụ AI đã cấu hình. Bộ lọc mẫu không nhận diện được mọi bí mật. Chỉ tiếp tục sau khi bạn đã đọc và đồng ý chia sẻ.";
        const preview = document.createElement("pre");
        preview.tabIndex = 0;
        preview.setAttribute("role", "region");
        preview.setAttribute("aria-label", "Nội dung cụ thể chuẩn bị chuyển cho tác nhân tiếp theo");
        preview.className = "workflow-step-output";
        preview.textContent = checkpoint.preview || "";
        const consent = document.createElement("label");
        consent.className = "workflow-toggle";
        const checkbox = document.createElement("input");
        checkbox.type = "checkbox";
        checkbox.id = "wfReviewConsent";
        consent.append(checkbox, document.createTextNode(
          " Tôi đã xem đúng nội dung ở trên và đồng ý chuyển nội dung này sang bước kế tiếp."));
        const approve = document.createElement("button");
        approve.type = "button";
        approve.textContent = "Duyệt và chạy bước kế tiếp";
        approve.disabled = true;
        approve.setAttribute("aria-describedby", "wfReviewConsent");
        const decline = document.createElement("button");
        decline.type = "button";
        decline.textContent = "Dừng quy trình, không chuyển dữ liệu";
        checkbox.addEventListener("change", () => {
          approve.disabled = !checkbox.checked;
        });

        async function decide(approved) {
          approve.disabled = true;
          decline.disabled = true;
          showStatus(approved
            ? "Đang chạy bước đã được bạn duyệt…"
            : "Đang dừng quy trình…");
          panel.setAttribute("aria-busy", "true");
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
            showError(error instanceof Error ? error.message : "Không thể duyệt.");
            decline.disabled = false;
            approve.disabled = !checkbox.checked;
          } finally {
            panel.removeAttribute("aria-busy");
          }
        }

        approve.addEventListener("click", () => { if (checkbox.checked) void decide(true); });
        decline.addEventListener("click", () => { void decide(false); });
        section.append(heading, warning, preview, consent, approve, decline);
        results.append(section);
      } else if (pendingReview) {
        pendingReview = false;
        showError("Thiếu dữ liệu xác nhận: quy trình không được tiếp tục.");
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
      // Kết quả đã hiển thị không được mở lại nút chạy khi bản xem trước đã hết hiệu lực.
      run.disabled = !loaded || pendingReview || !checkedConfiguration || !bool("wfConfirm");
      const focusHeading = reviewHeading || results.querySelector("h4");
      if (focusHeading) {
        focusHeading.tabIndex = -1;
        focusHeading.focus();
      }
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
      showStatus("Đang tải danh sách tác nhân AI…");
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
        run.disabled = true;
        showStatus("Hãy chọn các bước, xem trước cấu hình rồi xác nhận trước khi chạy.");
      } catch (error) {
        showError(error instanceof Error ? error.message : "Không tải được danh sách tác nhân AI.");
      }
    });

    form.addEventListener("submit", async event => {
      event.preventDefault();
      if (!loaded || pendingReview || !checkedConfiguration || !bool("wfConfirm")) return;
      const configuration = currentConfiguration();
      if (checkedConfiguration.snapshot !== JSON.stringify(configuration)
          || checkedConfiguration.workspaceId !== currentWorkspace()) {
        invalidatePreview();
        showError("Cấu hình hoặc không gian làm việc đã đổi. Hãy xem trước và xác nhận lại.");
        return;
      }
      const approvedDigest = checkedConfiguration.digest;
      const steps = configuration.steps;

      run.disabled = true;
      results.hidden = true;
      results.replaceChildren();
      showStatus("Đang chạy các bước đã xác nhận theo thứ tự…");
      panel.setAttribute("aria-busy", "true");
      try {
        const response = await fetch("/api/agents/orchestration/execute", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-PersonalAI-Workspace": window.PersonalAiWorkspace?.currentId || "personal"
          },
          body: JSON.stringify({
            ...configuration,
            confirmSelectedWorkflow: true,
            reviewedConfigurationDigest: approvedDigest
          })
        });
        const payload = await response.json();
        if (!response.ok) throw new Error(payload.error || "Quy trình chưa hoàn tất.");

        checkedConfiguration = null;
        field("wfConfirm").checked = false;
        field("wfConfirm").disabled = true;
        previewResult.hidden = true;
        await presentWorkflow(payload);
      } catch (error) {
        showError(error instanceof Error ? error.message : "Không thể chạy quy trình.");
      } finally {
        run.disabled = !loaded || pendingReview || !checkedConfiguration || !bool("wfConfirm");
        panel.removeAttribute("aria-busy");
      }
    });
  });
})();
