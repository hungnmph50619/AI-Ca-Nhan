(() => {
  const VERSION = "1.1.0";
  let busy = false;
  let knownTasks = [];

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureTaskButton();
    ensureTaskDialog();
    refreshTaskCount().catch(() => {});
  }

  function updateVersion() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;
  }

  function ensureTaskButton() {
    if (document.querySelector("#tasksButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "tasksButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "✓";

    const label = document.createElement("span");
    label.textContent = "Tác vụ";

    const count = document.createElement("span");
    count.id = "tasksSidebarCount";
    count.className = "tool-count";
    count.setAttribute("aria-label", "Số tác vụ");
    count.textContent = "0";

    button.append(icon, label, count);

    const toolsButton = document.querySelector("#toolsButton");
    const settingsButton = document.querySelector("#settingsButton");
    container.insertBefore(button, toolsButton || settingsButton || null);
    button.addEventListener("click", openTaskDialog);
  }

  function ensureTaskDialog() {
    let dialog = document.querySelector("#tasksDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "tasksDialog";
    dialog.className = "settings-dialog v090-task-dialog";
    dialog.setAttribute("aria-labelledby", "tasksTitle");

    const card = document.createElement("div");
    card.className = "settings-card v090-task-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "MỤC TIÊU → KẾ HOẠCH → TỪNG BƯỚC";
    const title = document.createElement("h2");
    title.id = "tasksTitle";
    title.textContent = "Tác vụ";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Tác vụ");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v090-task-intro";
    const introTitle = document.createElement("strong");
    introTitle.textContent = "Tác vụ có Undo Foundation v1.1.0";
    const introText = document.createElement("p");
    introText.textContent = "Tác vụ có thể phụ thuộc vào tác vụ khác và từng bước có thể khai báo phụ thuộc vào các bước trước. PersonalAI chỉ cho chạy khi các phụ thuộc đã hoàn tất; không có chuỗi nào tự chạy." ;
    intro.append(introTitle, introText);

    const form = document.createElement("form");
    form.id = "taskCreateForm";
    form.className = "v090-task-form";
    form.noValidate = true;

    const label = document.createElement("label");
    label.className = "field-label";
    label.setAttribute("for", "taskGoalInput");
    label.textContent = "Mục tiêu";

    const textarea = document.createElement("textarea");
    textarea.id = "taskGoalInput";
    textarea.rows = 3;
    textarea.maxLength = 2000;
    textarea.required = true;
    textarea.placeholder = "Ví dụ: tạo thư mục ghi chú và lưu một tệp kế hoạch ngắn trong thư mục làm việc.";

    const dependencyLabel = document.createElement("label");
    dependencyLabel.className = "field-label";
    dependencyLabel.setAttribute("for", "taskDependencySelect");
    dependencyLabel.textContent = "Phụ thuộc vào tác vụ";

    const dependencySelect = document.createElement("select");
    dependencySelect.id = "taskDependencySelect";
    dependencySelect.multiple = true;
    dependencySelect.size = 4;
    dependencySelect.setAttribute("aria-describedby", "taskDependencyHint");

    const dependencyHint = document.createElement("p");
    dependencyHint.id = "taskDependencyHint";
    dependencyHint.className = "field-hint";
    dependencyHint.textContent = "Không bắt buộc. Giữ Ctrl (Windows) hoặc Command (macOS) để chọn nhiều tác vụ. Tác vụ mới sẽ chờ các tác vụ đã chọn hoàn tất.";

    const formActions = document.createElement("div");
    formActions.className = "v090-task-form-actions";
    const create = document.createElement("button");
    create.id = "taskCreateButton";
    create.type = "submit";
    create.className = "primary-button";
    create.textContent = "Lập kế hoạch";
    formActions.appendChild(create);

    const hint = document.createElement("p");
    hint.className = "field-hint";
    hint.textContent = "Lập kế hoạch chỉ tạo danh sách bước; chưa có công cụ nào được chạy ở giai đoạn này.";

    form.append(
      label,
      textarea,
      dependencyLabel,
      dependencySelect,
      dependencyHint,
      formActions,
      hint);
    form.addEventListener("submit", createTask);

    const feedback = document.createElement("div");
    feedback.id = "taskFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    const listHeader = document.createElement("div");
    listHeader.className = "v090-task-list-header";
    const listTitle = document.createElement("strong");
    listTitle.textContent = "Tác vụ đã lưu trên máy";
    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Tải lại";
    refresh.addEventListener("click", () => loadTasks());
    listHeader.append(listTitle, refresh);

    const list = document.createElement("div");
    list.id = "taskList";
    list.className = "v090-task-list";
    list.setAttribute("role", "list");

    const note = document.createElement("p");
    note.className = "security-copy";
    note.textContent = "v1.1.0 giữ tối đa 50 tác vụ trong mỗi không gian trên máy, hỗ trợ phụ thuộc giữa tác vụ và giữa các bước. Phụ thuộc không làm tăng quyền tự động: mỗi bước vẫn phải được người dùng chủ động chạy và các thao tác có tác động vẫn cần xác nhận." ;

    card.append(header, intro, form, feedback, listHeader, list, note);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
    document.body.appendChild(dialog);
    return dialog;
  }

  async function openTaskDialog() {
    const dialog = ensureTaskDialog();
    dialog.showModal();
    await loadTasks();
  }

  async function refreshTaskCount() {
    const payload = await fetchTasks();
    updateTaskCount(payload.tasks);
  }

  async function fetchTasks() {
    const response = await fetch("/api/tasks", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.error || "Không đọc được danh sách tác vụ.");
    }
    return payload;
  }

  async function loadTasks() {
    if (busy) return;
    setFeedback("Đang tải tác vụ…");
    try {
      const payload = await fetchTasks();
      renderTasks(payload);
      setFeedback("");
    } catch (error) {
      setFeedback(error.message || "Không đọc được danh sách tác vụ.", true);
    }
  }

  async function createTask(event) {
    event.preventDefault();
    if (busy) return;

    const input = document.querySelector("#taskGoalInput");
    if (!input) return;
    const goal = input.value.trim();
    const dependencySelect = document.querySelector("#taskDependencySelect");
    const dependsOnTaskIds = dependencySelect
      ? Array.from(dependencySelect.selectedOptions).map(option => option.value)
      : [];
    if (goal.length < 3) {
      setFeedback("Mục tiêu cần có ít nhất 3 ký tự.", true);
      return;
    }

    setBusy(true);
    setFeedback("AI đang lập kế hoạch an toàn từ các công cụ hiện có…");

    try {
      const response = await fetch("/api/tasks", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ goal, dependsOnTaskIds })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không lập được kế hoạch tác vụ.");
      }

      input.value = "";
      if (dependencySelect) {
        Array.from(dependencySelect.options).forEach(option => {
          option.selected = false;
        });
      }
      setFeedback("Đã tạo kế hoạch và quan hệ phụ thuộc. Chưa có bước nào được chạy.");
      await loadTasksAfterMutation();
    } catch (error) {
      setFeedback(error.message || "Không lập được kế hoạch tác vụ.", true);
    } finally {
      setBusy(false);
    }
  }

  async function executeNext(task) {
    if (busy) return;

    const step = currentStep(task);
    if (!step) {
      setFeedback("Tác vụ không còn bước nào để chạy.", true);
      return;
    }

    const taskBlockers = taskDependencyBlockers(task);
    if (taskBlockers.length > 0) {
      setFeedback(
        "Tác vụ đang chờ: " + taskBlockers.map(item => item.label).join("; "),
        true);
      return;
    }

    const stepBlockers = stepDependencyBlockers(task, step);
    if (stepBlockers.length > 0) {
      setFeedback(
        `Bước ${step.index} đang chờ bước ${stepBlockers.join(", ")} hoàn tất.`,
        true);
      return;
    }

    let confirmed = false;
    if (step.requiresConfirmation) {
      const ask = window.PersonalAiUi?.confirm;
      if (typeof ask !== "function") {
        setFeedback("Không mở được hộp xác nhận an toàn.", true);
        return;
      }

      confirmed = await ask(
        `Chạy bước ${step.index}: ${step.title}?\n\nCông cụ: ${toolDisplayName(step.toolName)}\nQuyền: ${formatPermissions(step.requiredPermissions)}\n\n${formatArguments(step.toolName, step.arguments)}`,
        {
          title: "Xác nhận bước tác vụ",
          confirmText: "Xác nhận và chạy",
          danger: hasDeletePermission(step.requiredPermissions)
        });
      if (!confirmed) return;
    }

    setBusy(true);
    setFeedback(`Đang chạy bước ${step.index}: ${step.title}…`);

    try {
      const response = await fetch(
        `/api/tasks/${encodeURIComponent(task.id)}/execute-next`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ confirmed })
        });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || payload.execution?.error || "Không chạy được bước tác vụ.");
      }

      setFeedback(payload.localSummary || "Bước tác vụ đã hoàn tất.");
      await loadTasksAfterMutation();
    } catch (error) {
      setFeedback(error.message || "Không chạy được bước tác vụ.", true);
      await loadTasksAfterMutation();
    } finally {
      setBusy(false);
    }
  }

  async function resumeTask(task) {
    if (busy) return;

    const ask = window.PersonalAiUi?.confirm;
    if (typeof ask !== "function") {
      setFeedback("Không mở được hộp xác nhận.", true);
      return;
    }

    const confirmed = await ask(
      "Khôi phục tác vụ bị gián đoạn?\n\nMột thao tác có thể đã chạy một phần hoặc đã hoàn tất trước khi ứng dụng dừng. Hãy kiểm tra trạng thái thực tế trước khi tiếp tục. Khôi phục chỉ đưa bước về trạng thái chờ; hệ thống sẽ không tự chạy lại.",
      {
        title: "Khôi phục tác vụ",
        confirmText: "Khôi phục",
        danger: false
      });
    if (!confirmed) return;

    setBusy(true);
    setFeedback("Đang khôi phục tác vụ…");
    try {
      const response = await fetch(
        `/api/tasks/${encodeURIComponent(task.id)}/resume`,
        { method: "POST" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không khôi phục được tác vụ.");
      }

      setFeedback("Đã khôi phục. Chưa có bước nào được tự động chạy lại.");
      await loadTasksAfterMutation();
    } catch (error) {
      setFeedback(error.message || "Không khôi phục được tác vụ.", true);
    } finally {
      setBusy(false);
    }
  }

  async function retryFailedStep(task) {
    if (busy) return;

    const step = currentStep(task);
    if (!step || step.status !== "failed") {
      setFeedback("Không tìm thấy bước có lỗi để thử lại.", true);
      return;
    }

    setBusy(true);
    setFeedback("Đang đánh giá mức an toàn của lần thử lại…");

    try {
      const assessmentResponse = await fetch(
        `/api/tasks/${encodeURIComponent(task.id)}/retry-assessment`,
        { cache: "no-store" });
      const assessment = await assessmentResponse.json().catch(() => ({}));
      if (!assessmentResponse.ok) {
        throw new Error(assessment.error || "Không đánh giá được khả năng thử lại.");
      }

      if (!assessment.canRetry) {
        setFeedback(
          assessment.message || "Bước này không được phép thử lại tự động.",
          true);
        return;
      }

      let confirmedReview = false;
      if (assessment.requiresReviewConfirmation) {
        const ask = window.PersonalAiUi?.confirm;
        if (typeof ask !== "function") {
          setFeedback("Không mở được hộp xác nhận an toàn.", true);
          return;
        }

        confirmedReview = await ask(
          `${assessment.message}\n\nNếu tiếp tục, PersonalAI chỉ đưa bước về trạng thái Chưa chạy. Bạn vẫn phải bấm Chạy bước này sau đó và xác nhận lại nếu công cụ có tác động.`,
          {
            title: "Kiểm tra trước khi thử lại",
            confirmText: "Chuẩn bị thử lại",
            danger: hasDeletePermission(step.requiredPermissions)
          });
        if (!confirmedReview) return;
      }

      const response = await fetch(
        `/api/tasks/${encodeURIComponent(task.id)}/retry`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ confirmedReview })
        });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không chuẩn bị được lần thử lại.");
      }

      setFeedback(
        "Đã chuẩn bị thử lại. Bước đã về trạng thái Chưa chạy và chưa có công cụ nào được tự động thực thi.");
      await loadTasksAfterMutation();
    } catch (error) {
      setFeedback(error.message || "Không chuẩn bị được lần thử lại.", true);
    } finally {
      setBusy(false);
    }
  }

  async function cancelTask(task) {
    if (busy) return;

    const ask = window.PersonalAiUi?.confirm;
    if (typeof ask !== "function") {
      setFeedback("Không mở được hộp xác nhận.", true);
      return;
    }

    const confirmed = await ask(
      "Hủy tác vụ này? Các bước đã hoàn tất sẽ không bị hoàn tác.",
      {
        title: "Hủy tác vụ",
        confirmText: "Hủy tác vụ",
        danger: true
      });
    if (!confirmed) return;

    setBusy(true);
    try {
      const response = await fetch(
        `/api/tasks/${encodeURIComponent(task.id)}/cancel`,
        { method: "POST" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không hủy được tác vụ.");
      }

      setFeedback("Đã hủy tác vụ.");
      await loadTasksAfterMutation();
    } catch (error) {
      setFeedback(error.message || "Không hủy được tác vụ.", true);
    } finally {
      setBusy(false);
    }
  }

  async function loadTasksAfterMutation() {
    try {
      const payload = await fetchTasks();
      renderTasks(payload);
    } catch {
      // Phản hồi chính đã được hiển thị; lần tải lại sau sẽ đồng bộ lại danh sách.
    }
  }

  function renderTasks(payload) {
    const tasks = Array.isArray(payload.tasks) ? payload.tasks : [];
    knownTasks = tasks;
    updateTaskCount(tasks);
    renderDependencyOptions(tasks);

    const list = document.querySelector("#taskList");
    if (!list) return;
    list.replaceChildren();

    if (tasks.length === 0) {
      const empty = document.createElement("div");
      empty.className = "v090-task-empty";
      const title = document.createElement("strong");
      title.textContent = "Chưa có tác vụ";
      const copy = document.createElement("p");
      copy.textContent = "Nhập một mục tiêu ở phía trên để AI lập kế hoạch bằng các công cụ hiện có.";
      empty.append(title, copy);
      list.appendChild(empty);
      return;
    }

    tasks.forEach(task => list.appendChild(createTaskCard(task)));
  }

  function createTaskCard(task) {
    const article = document.createElement("article");
    article.className = "v090-task-item";
    article.setAttribute("role", "listitem");

    const header = document.createElement("div");
    header.className = "v090-task-item-header";
    const goal = document.createElement("strong");
    goal.textContent = task.goal || "Tác vụ";
    const status = document.createElement("span");
    status.className = `v090-task-status status-${String(task.status || "planned")}`;
    status.textContent = taskStatusLabel(task.status);
    header.append(goal, status);

    const provenance = document.createElement("p");
    provenance.className = "field-hint";
    provenance.textContent = `Lập kế hoạch: ${task.planningProvider || "AI"} · mô hình ${task.planningModel || "không rõ"} · ${formatTime(task.createdAt)}`;

    const plan = document.createElement("p");
    plan.className = "v090-task-plan";
    plan.textContent = task.plan || "Kế hoạch tác vụ";

    const dependencyInfo = document.createElement("p");
    dependencyInfo.className = "v090-task-dependencies";
    const taskDependencies = dependencyTaskIds(task);
    dependencyInfo.hidden = taskDependencies.length === 0;
    dependencyInfo.textContent = taskDependencies.length === 0
      ? ""
      : "Phụ thuộc tác vụ: " + taskDependencies
          .map(id => taskDependencyDisplay(id))
          .join(" · ");

    const steps = document.createElement("div");
    steps.className = "v090-task-steps";
    (Array.isArray(task.steps) ? task.steps : []).forEach(step => {
      steps.appendChild(createStepCard(task, step));
    });

    const actions = document.createElement("div");
    actions.className = "v090-task-actions";
    const active = task.status === "planned" || task.status === "running";
    const step = currentStep(task);
    if (active && step) {
      const run = document.createElement("button");
      run.type = "button";
      run.className = step.requiresConfirmation ? "primary-button" : "secondary-button";
      const blockers = taskDependencyBlockers(task);
      run.textContent = blockers.length > 0
        ? "Đang chờ tác vụ phụ thuộc"
        : step.requiresConfirmation
          ? "Xác nhận & chạy bước này"
          : "Chạy bước này";
      run.title = blockers.length > 0
        ? blockers.map(item => item.label).join("; ")
        : "";
      run.disabled = busy;
      run.addEventListener("click", () => executeNext(task));
      actions.appendChild(run);

      const cancel = document.createElement("button");
      cancel.type = "button";
      cancel.className = "secondary-button";
      cancel.textContent = "Hủy tác vụ";
      cancel.disabled = busy;
      cancel.addEventListener("click", () => cancelTask(task));
      actions.appendChild(cancel);
    } else if (task.status === "failed" && step?.status === "failed") {
      const retry = document.createElement("button");
      retry.type = "button";
      retry.className = "secondary-button";
      retry.textContent = "Đánh giá & thử lại bước này";
      retry.title = "PersonalAI sẽ kiểm tra mức an toàn trước khi cho chuẩn bị thử lại.";
      retry.disabled = busy;
      retry.addEventListener("click", () => retryFailedStep(task));
      actions.appendChild(retry);
    } else if (task.status === "interrupted") {
      const warning = document.createElement("p");
      warning.className = "v090-task-interrupted";
      warning.textContent = "Ứng dụng đã dừng khi bước hiện tại đang chạy. Trạng thái thực tế của thao tác có thể chưa xác định; hãy kiểm tra dữ liệu hoặc thư mục làm việc trước khi khôi phục.";
      article.appendChild(warning);

      const resume = document.createElement("button");
      resume.type = "button";
      resume.className = "primary-button";
      resume.textContent = "Khôi phục tác vụ";
      resume.disabled = busy;
      resume.addEventListener("click", () => resumeTask(task));
      actions.appendChild(resume);

      const cancel = document.createElement("button");
      cancel.type = "button";
      cancel.className = "secondary-button";
      cancel.textContent = "Hủy tác vụ";
      cancel.disabled = busy;
      cancel.addEventListener("click", () => cancelTask(task));
      actions.appendChild(cancel);
    }

    if (task.result) {
      const result = document.createElement("div");
      result.className = "v090-task-result";
      const resultTitle = document.createElement("strong");
      resultTitle.textContent = task.status === "completed"
        ? "Kết quả tác vụ"
        : "Trạng thái cuối";
      const resultText = document.createElement("p");
      resultText.textContent = task.result;
      result.append(resultTitle, resultText);
      article.append(
        header,
        provenance,
        plan,
        dependencyInfo,
        steps,
        result,
        actions);
    } else {
      article.append(
        header,
        provenance,
        plan,
        dependencyInfo,
        steps,
        actions);
    }

    return article;
  }

  function createStepCard(task, step) {
    const section = document.createElement("section");
    section.className = "v090-task-step";

    const top = document.createElement("div");
    top.className = "v090-task-step-header";

    const title = document.createElement("strong");
    title.textContent = `${step.index}. ${step.title || "Bước tác vụ"}`;

    const status = document.createElement("span");
    status.textContent = stepStatusLabel(step.status);
    top.append(title, status);

    const description = document.createElement("p");
    description.textContent = step.description || "";

    const meta = document.createElement("div");
    meta.className = "v090-task-step-meta";
    appendMeta(meta, "Công cụ: " + toolDisplayName(step.toolName));
    appendMeta(meta, "Quyền: " + formatPermissions(step.requiredPermissions));
    const stepDependencies = stepDependencyIndexes(step);
    if (stepDependencies.length > 0) {
      appendMeta(meta, "Phụ thuộc bước: " + stepDependencies.join(", "));
    }
    if (step.requiresConfirmation) appendMeta(meta, "Cần xác nhận trước khi chạy");
    if (Number(step.attemptCount || 0) > 0) {
      appendMeta(meta, `Đã chạy ${Number(step.attemptCount)} lần`);
    }

    const details = document.createElement("details");
    details.className = "v090-task-step-arguments";
    const summary = document.createElement("summary");
    summary.textContent = "Xem tham số đã lập kế hoạch";
    const pre = document.createElement("pre");
    pre.textContent = formatArguments(step.toolName, step.arguments);
    details.append(summary, pre);

    section.append(top, description, meta, details);

    if (step.localSummary) {
      const result = document.createElement("p");
      result.className = "v090-task-step-result";
      result.textContent = step.localSummary;
      section.appendChild(result);
    }

    const isCurrent = (task.status === "planned" || task.status === "running" || task.status === "interrupted")
      && Number(task.currentStep) === Number(step.index);
    section.classList.toggle("current", isCurrent);
    return section;
  }

  function appendMeta(container, text) {
    const span = document.createElement("span");
    span.textContent = text;
    container.appendChild(span);
  }

  function currentStep(task) {
    const steps = Array.isArray(task.steps) ? task.steps : [];
    const current = Number(task.currentStep);
    return steps.find(step => Number(step.index) === current) || null;
  }

  function dependencyTaskIds(task) {
    return Array.isArray(task?.dependsOnTaskIds)
      ? task.dependsOnTaskIds.filter(Boolean)
      : [];
  }

  function stepDependencyIndexes(step) {
    return Array.isArray(step?.dependsOn)
      ? step.dependsOn
          .map(value => Number(value))
          .filter(value => Number.isInteger(value) && value > 0)
      : [];
  }

  function findKnownTask(id) {
    return knownTasks.find(task => String(task.id) === String(id)) || null;
  }

  function taskDependencyDisplay(id) {
    const task = findKnownTask(id);
    if (!task) return "không còn tồn tại";
    return `${task.goal || "Tác vụ"} — ${taskStatusLabel(task.status)}`;
  }

  function taskDependencyBlockers(task) {
    return dependencyTaskIds(task)
      .map(id => {
        const dependency = findKnownTask(id);
        if (!dependency) {
          return { id, label: `${id} (không còn tồn tại)` };
        }
        if (dependency.status === "completed") return null;
        return {
          id,
          label: `${dependency.goal || "Tác vụ"} (${taskStatusLabel(dependency.status)})`
        };
      })
      .filter(Boolean);
  }

  function stepDependencyBlockers(task, step) {
    const steps = Array.isArray(task?.steps) ? task.steps : [];
    return stepDependencyIndexes(step).filter(index => {
      const dependency = steps.find(
        candidate => Number(candidate.index) === Number(index));
      return !dependency || dependency.status !== "completed";
    });
  }

  function renderDependencyOptions(tasks) {
    const select = document.querySelector("#taskDependencySelect");
    if (!select) return;

    const selected = new Set(
      Array.from(select.selectedOptions).map(option => option.value));
    select.replaceChildren();

    tasks
      .filter(task => task.status !== "cancelled")
      .forEach(task => {
        const option = document.createElement("option");
        option.value = task.id;
        option.textContent = `${task.goal || "Tác vụ"} — ${taskStatusLabel(task.status)}`;
        option.selected = selected.has(String(task.id));
        select.appendChild(option);
      });

    if (select.options.length === 0) {
      const option = document.createElement("option");
      option.disabled = true;
      option.textContent = "Chưa có tác vụ nào để chọn làm phụ thuộc";
      select.appendChild(option);
    }
  }

  function setBusy(value) {
    busy = value;
    const create = document.querySelector("#taskCreateButton");
    const input = document.querySelector("#taskGoalInput");
    if (create) create.disabled = value;
    if (input) input.disabled = value;
    document.querySelectorAll("#taskList button").forEach(button => {
      button.disabled = value;
    });
  }

  function setFeedback(message, error = false) {
    const node = document.querySelector("#taskFeedback");
    if (!node) return;
    node.textContent = message || "";
    node.className = `settings-feedback ${error ? "error" : ""}`.trim();
  }

  function updateTaskCount(tasks) {
    const count = document.querySelector("#tasksSidebarCount");
    if (count) count.textContent = String(Array.isArray(tasks) ? tasks.length : 0);
  }

  function taskStatusLabel(status) {
    return {
      planned: "Đã lập kế hoạch",
      running: "Đang thực hiện",
      completed: "Đã hoàn tất",
      failed: "Có lỗi",
      interrupted: "Bị gián đoạn",
      cancelled: "Đã hủy"
    }[status] || "Không rõ";
  }

  function stepStatusLabel(status) {
    return {
      pending: "Chưa chạy",
      running: "Đang chạy",
      completed: "Đã xong",
      failed: "Có lỗi",
      interrupted: "Bị gián đoạn"
    }[status] || "Không rõ";
  }

  function toolDisplayName(toolName) {
    return {
      "app.summary": "Tổng quan ứng dụng",
      "documents.search": "Tìm trong tài liệu",
      "local.calculate": "Máy tính",
      "local.clock": "Đồng hồ hệ thống",
      "local.date_math": "Tính toán ngày giờ",
      "local.text_stats": "Thống kê văn bản",
      "memory.search": "Tìm trong trí nhớ",
      "computer.screen.info": "Thông tin màn hình",
      "computer.cursor.position": "Vị trí con trỏ",
      "computer.windows.list": "Danh sách cửa sổ",
      "computer.window.active": "Cửa sổ đang hoạt động",
      "computer.window.focus": "Chuyển focus cửa sổ",
      "computer.cursor.move": "Di chuyển con trỏ",
      "workspace.create_directory": "Tạo thư mục",
      "workspace.delete": "Xóa tệp hoặc thư mục",
      "workspace.list": "Liệt kê thư mục làm việc",
      "workspace.move": "Di chuyển hoặc đổi tên",
      "workspace.read_text": "Đọc tệp văn bản",
      "workspace.write_text": "Ghi tệp văn bản"
    }[toolName] || "Công cụ";
  }

  function formatPermissions(values) {
    const labels = {
      READ: "ĐỌC",
      WRITE: "GHI",
      DELETE: "XÓA",
      EXTERNAL: "BÊN NGOÀI",
      SENSITIVE: "NHẠY CẢM",
      COMPUTER: "ĐIỀU KHIỂN MÁY"
    };
    const permissions = Array.isArray(values) ? values : [];
    return permissions.map(value => labels[String(value).toUpperCase()] || "KHÔNG XÁC ĐỊNH").join(", ") || "Không có";
  }

  function hasDeletePermission(values) {
    return Array.isArray(values)
      && values.some(value => String(value).toUpperCase() === "DELETE");
  }

  function formatArguments(toolName, args) {
    const value = args && typeof args === "object" ? args : {};
    const rows = [];
    const add = (label, item) => {
      if (item === undefined || item === null || item === "") return;
      rows.push(label + ": " + String(item));
    };

    switch (toolName) {
      case "local.calculate":
        add("Biểu thức", value.expression);
        break;
      case "local.text_stats":
        add("Văn bản", value.text);
        break;
      case "local.date_math":
        add("Phép tính", value.operation === "difference" ? "Tính chênh lệch" : "Cộng khoảng thời gian");
        add("Mốc bắt đầu", value.start);
        add("Mốc kết thúc", value.end);
        add("Số ngày", value.days);
        add("Số giờ", value.hours);
        add("Số phút", value.minutes);
        break;
      case "memory.search":
      case "documents.search":
        add("Nội dung cần tìm", value.query);
        add("Số kết quả tối đa", value.limit);
        break;
      case "computer.windows.list":
        add("Số cửa sổ tối đa", value.limit);
        break;
      case "computer.window.focus":
        add("Window ID", value.windowId);
        break;
      case "computer.cursor.move":
        add("Tọa độ X", value.x);
        add("Tọa độ Y", value.y);
        break;
      case "computer.screen.info":
      case "computer.cursor.position":
      case "computer.window.active":
        break;
      case "workspace.list":
        add("Thư mục", value.path || ".");
        add("Số mục tối đa", value.maxEntries);
        break;
      case "workspace.read_text":
        add("Đường dẫn tệp", value.path);
        add("Số ký tự tối đa", value.maxCharacters);
        break;
      case "workspace.write_text":
        add("Đường dẫn tệp", value.path);
        add("Nội dung", value.content);
        add("Cách ghi", writeModeLabel(value.mode));
        add("Mã băm SHA-256 kỳ vọng", value.expectedSha256);
        break;
      case "workspace.create_directory":
        add("Đường dẫn thư mục", value.path);
        break;
      case "workspace.move":
        add("Đường dẫn nguồn", value.sourcePath);
        add("Đường dẫn đích", value.destinationPath);
        add("Mã băm SHA-256 kỳ vọng", value.expectedSha256);
        break;
      case "workspace.delete":
        add("Đường dẫn cần xóa", value.path);
        add("Mã băm SHA-256 kỳ vọng", value.expectedSha256);
        break;
      default:
        return "Tham số đã được máy chủ kiểm tra.";
    }

    return rows.length ? rows.join("\n") : "Không có tham số.";
  }

  function writeModeLabel(mode) {
    return {
      create: "Tạo mới",
      overwrite: "Ghi đè",
      append: "Nối thêm"
    }[String(mode || "").toLowerCase()] || "Ghi";
  }

  function formatTime(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? "không rõ thời gian"
      : new Intl.DateTimeFormat("vi-VN", {
          dateStyle: "short",
          timeStyle: "short"
        }).format(date);
  }
})();
