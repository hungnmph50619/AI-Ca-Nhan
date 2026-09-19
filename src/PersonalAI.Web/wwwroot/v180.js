(() => {
  const VERSION = "2.2.4";
  let loading = false;
  let tasksById = new Map();

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
    if (node) node.textContent = `Phiên bản ${VERSION}`;
  }

  function ensureButton() {
    if (document.querySelector("#automationButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "automationButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "⏱";

    const label = document.createElement("span");
    label.textContent = "Tự động hóa";

    const badge = document.createElement("span");
    badge.id = "automationBadge";
    badge.className = "tool-count v180-automation-badge";
    badge.textContent = "…";

    button.append(icon, label, badge);

    const decision = document.querySelector("#decisionEngineButton");
    const life = document.querySelector("#lifeContextButton");
    const android = document.querySelector("#androidCompanionButton");
    const reference = decision || life || android;
    container.insertBefore(button, reference || null);
    button.addEventListener("click", openDialog);
  }

  function ensureDialog() {
    let dialog = document.querySelector("#automationDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "automationDialog";
    dialog.className = "settings-dialog v180-automation-dialog";
    dialog.setAttribute("aria-labelledby", "automationTitle");

    const card = document.createElement("div");
    card.className = "settings-card v180-automation-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "SCHEDULE · ONE STEP · NO AUTO-CONFIRM";

    const title = document.createElement("h2");
    title.id = "automationTitle";
    title.textContent = "Automation";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Automation");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v180-automation-intro";
    intro.innerHTML =
      "<strong>Background scheduler có guard</strong>" +
      "<p>Mỗi lần đến lịch chỉ tiến tối đa một task step. Chỉ READ/local step không cần confirmation mới được tự chạy. Bất kỳ quyền nhạy cảm hoặc side effect nào đều dừng ở awaiting-confirmation.</p>";

    const summary = document.createElement("div");
    summary.id = "automationSummary";
    summary.className = "v180-automation-summary";
    summary.textContent = "Đang kiểm tra…";

    const listTitle = document.createElement("h3");
    listTitle.className = "v180-section-title";
    listTitle.textContent = "Automation hiện tại";

    const list = document.createElement("div");
    list.id = "automationList";
    list.className = "v180-automation-list";

    const empty = document.createElement("div");
    empty.id = "automationEmpty";
    empty.className = "v180-automation-empty";
    empty.textContent = "Chưa có automation trong workspace hiện tại.";
    empty.hidden = true;

    const formTitle = document.createElement("h3");
    formTitle.className = "v180-section-title";
    formTitle.textContent = "Tạo automation";

    const form = document.createElement("form");
    form.id = "automationForm";
    form.className = "v180-automation-form";

    const nameField = field("Tên", "automationName", "Ví dụ: Đọc báo cáo mỗi 30 phút", "text");
    const taskField = selectField("Task", "automationTask");
    const scheduleField = selectField("Schedule", "automationSchedule");
    scheduleField.input.append(
      option("once", "Một lần"),
      option("interval", "Lặp theo khoảng thời gian"));

    const startField = field("Bắt đầu", "automationStart", "Để trống = ngay", "datetime-local");
    startField.input.required = false;

    const intervalField = field("Khoảng lặp (phút)", "automationInterval", "Tối thiểu 5", "number");
    intervalField.wrapper.id = "automationIntervalField";
    intervalField.input.min = "5";
    intervalField.input.max = "10080";
    intervalField.input.value = "30";
    intervalField.wrapper.hidden = true;

    scheduleField.input.addEventListener("change", () => {
      intervalField.wrapper.hidden = scheduleField.input.value !== "interval";
    });

    const note = document.createElement("p");
    note.className = "security-copy";
    note.textContent =
      "Automation gắn vào task đã tồn tại. Decision recommendation không được tạo/chạy automation tự động. Task step cần confirmation phải được xử lý thủ công ở Tasks trước khi tiếp tục automation.";

    const submit = document.createElement("button");
    submit.type = "submit";
    submit.className = "primary-button";
    submit.textContent = "Tạo automation";

    form.append(
      nameField.wrapper,
      taskField.wrapper,
      scheduleField.wrapper,
      startField.wrapper,
      intervalField.wrapper,
      note,
      submit);

    form.addEventListener("submit", async event => {
      event.preventDefault();
      await createAutomation({
        name: nameField.input.value,
        taskId: taskField.input.value,
        scheduleKind: scheduleField.input.value,
        startValue: startField.input.value,
        intervalValue: intervalField.input.value
      });
    });

    const safety = document.createElement("div");
    safety.className = "v180-automation-safety";
    safety.innerHTML =
      "<strong>Không tự xác nhận</strong>" +
      "<p>Scheduler luôn gọi Task Engine với confirmed=false. WRITE, DELETE, EXTERNAL, SENSITIVE, COMPUTER, BROWSER, CONNECTOR và DEVELOPMENT không được background auto-run.</p>";

    const feedback = document.createElement("div");
    feedback.id = "automationFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    const actions = document.createElement("div");
    actions.className = "v180-actions";
    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Làm mới";
    refresh.addEventListener("click", load);
    actions.appendChild(refresh);

    card.append(
      header,
      intro,
      summary,
      listTitle,
      list,
      empty,
      formTitle,
      form,
      safety,
      feedback,
      actions);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });

    document.body.appendChild(dialog);
    return dialog;
  }

  function field(labelText, id, placeholder, type) {
    const wrapper = document.createElement("label");
    wrapper.className = "v180-field";

    const label = document.createElement("span");
    label.textContent = labelText;

    const input = document.createElement("input");
    input.id = id;
    input.type = type;
    input.placeholder = placeholder;
    input.required = true;

    wrapper.append(label, input);
    return { wrapper, input };
  }

  function selectField(labelText, id) {
    const wrapper = document.createElement("label");
    wrapper.className = "v180-field";

    const label = document.createElement("span");
    label.textContent = labelText;

    const input = document.createElement("select");
    input.id = id;
    input.required = true;

    wrapper.append(label, input);
    return { wrapper, input };
  }

  function option(value, label) {
    const node = document.createElement("option");
    node.value = value;
    node.textContent = label;
    return node;
  }

  async function openDialog() {
    const dialog = ensureDialog();
    dialog.showModal();
    await load();
  }

  async function refreshBadge() {
    const response = await fetch("/api/automations", { cache: "no-store" });
    if (!response.ok) return;
    const payload = await response.json().catch(() => ({}));
    const items = Array.isArray(payload.automations) ? payload.automations : [];
    updateBadge(items.filter(item => item.enabled).length);
  }

  async function load() {
    if (loading) return;
    loading = true;
    setFeedback("");

    try {
      const [statusResponse, automationResponse, taskResponse] = await Promise.all([
        fetch("/api/automations/status", { cache: "no-store" }),
        fetch("/api/automations", { cache: "no-store" }),
        fetch("/api/tasks", { cache: "no-store" })
      ]);

      const status = await statusResponse.json().catch(() => ({}));
      const automations = await automationResponse.json().catch(() => ({}));
      const tasks = await taskResponse.json().catch(() => ({}));

      if (!statusResponse.ok || !automationResponse.ok || !taskResponse.ok) {
        throw new Error(
          status.error
          || automations.error
          || tasks.error
          || "Không đọc được Automation Foundation.");
      }

      render(status, automations, tasks);
    } catch (error) {
      setFeedback(error.message || "Không đọc được Automation Foundation.", true);
    } finally {
      loading = false;
    }
  }

  function render(status, response, taskResponse) {
    const summary = document.querySelector("#automationSummary");
    const list = document.querySelector("#automationList");
    const empty = document.querySelector("#automationEmpty");
    const taskSelect = document.querySelector("#automationTask");
    if (!summary || !list || !empty || !taskSelect) return;

    const automations = Array.isArray(response.automations)
      ? response.automations
      : [];
    const tasks = Array.isArray(taskResponse.tasks)
      ? taskResponse.tasks
      : [];

    tasksById = new Map(tasks.map(task => [String(task.id), task]));

    const active = automations.filter(item => item.enabled).length;
    summary.textContent =
      `${active} đang bật · ${automations.length}/${response.maximumAutomations || status.maximumAutomationsPerWorkspace || 50} automation · poll ${status.schedulerPollSeconds || 30}s · auto-confirm: ${status.autoConfirmationEnabled ? "BẬT" : "TẮT"}`;

    list.replaceChildren();
    automations.forEach(item => list.appendChild(automationCard(item)));
    empty.hidden = automations.length > 0;

    const previous = taskSelect.value;
    taskSelect.replaceChildren();
    const eligible = tasks.filter(task =>
      !["completed", "failed", "cancelled"].includes(task.status));
    if (eligible.length === 0) {
      taskSelect.appendChild(option("", "Không có task đang hoạt động"));
      taskSelect.disabled = true;
    } else {
      taskSelect.disabled = false;
      eligible.forEach(task => {
        const node = option(task.id, `${task.goal} · ${task.status}`);
        taskSelect.appendChild(node);
      });
      if (eligible.some(task => String(task.id) === previous)) {
        taskSelect.value = previous;
      }
    }

    updateBadge(active);
  }

  function automationCard(item) {
    const article = document.createElement("article");
    article.className = "v180-automation-item";

    const top = document.createElement("div");
    top.className = "v180-automation-top";

    const main = document.createElement("div");
    const name = document.createElement("strong");
    name.textContent = item.name || "Automation";

    const task = tasksById.get(String(item.taskId));
    const meta = document.createElement("span");
    meta.textContent =
      `${stateLabel(item.state)} · ${item.scheduleKind === "interval" ? `mỗi ${item.intervalMinutes} phút` : "một lần"} · task: ${task?.goal || item.taskId}`;
    main.append(name, meta);

    const state = document.createElement("span");
    state.className = `v180-state is-${safeClass(item.state)}`;
    state.textContent = stateLabel(item.state);
    top.append(main, state);

    const schedule = document.createElement("p");
    schedule.className = "v180-automation-meta";
    schedule.textContent =
      `Next: ${formatDate(item.nextRunAt)} · Last: ${item.lastRunStatus || "none"} · Runs: ${item.runCount || 0}`;

    const message = document.createElement("p");
    message.className = "v180-automation-message";
    message.textContent = item.lastMessage || "Chưa chạy.";

    const actions = document.createElement("div");
    actions.className = "v180-item-actions";

    if (item.state === "scheduled") {
      const run = button("Chạy ngay", () => runNow(item));
      actions.appendChild(run);
    }

    if (["awaiting-confirmation", "interrupted"].includes(item.state)) {
      actions.appendChild(button("Tiếp tục sau review", () => resume(item)));
    } else if (item.enabled) {
      actions.appendChild(button("Tạm dừng", () => setEnabled(item, false)));
    } else if (!["completed", "failed"].includes(item.state)) {
      actions.appendChild(button("Bật lại", () => setEnabled(item, true)));
    }

    actions.appendChild(button("Xóa", () => remove(item), true));

    article.append(top, schedule, message, actions);
    return article;
  }

  function button(label, handler, danger = false) {
    const node = document.createElement("button");
    node.type = "button";
    node.className = "secondary-button";
    if (danger) node.classList.add("v180-danger");
    node.textContent = label;
    node.addEventListener("click", handler);
    return node;
  }

  async function createAutomation(values) {
    const taskId = String(values.taskId || "").trim();
    if (!taskId) {
      setFeedback("Hãy chọn một task đang hoạt động.", true);
      return;
    }

    const payload = {
      name: String(values.name || "").trim(),
      taskId,
      scheduleKind: values.scheduleKind,
      confirmed: true
    };

    if (values.startValue) {
      const date = new Date(values.startValue);
      if (Number.isNaN(date.getTime())) {
        setFeedback("Thời điểm bắt đầu không hợp lệ.", true);
        return;
      }
      payload.startAt = date.toISOString();
    }

    if (values.scheduleKind === "interval") {
      payload.intervalMinutes = Number(values.intervalValue);
    }

    try {
      const response = await fetch("/api/automations", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(body.error || "Không thể tạo automation.");
      }

      const form = document.querySelector("#automationForm");
      if (form) form.reset();
      const interval = document.querySelector("#automationIntervalField");
      if (interval) interval.hidden = true;
      setFeedback("Đã tạo automation. Scheduler sẽ không tự xác nhận step nhạy cảm.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thể tạo automation.", true);
    }
  }

  async function runNow(item) {
    const confirmFn = window.PersonalAiUi?.confirm;
    if (typeof confirmFn !== "function") {
      setFeedback("Không mở được hộp xác nhận an toàn.", true);
      return;
    }

    const confirmed = await confirmFn(
      `Kích hoạt automation "${item.name}" ngay bây giờ?\n\nXác nhận này chỉ kích hoạt scheduler. Nó KHÔNG xác nhận thay cho task step có side effect.`,
      {
        title: "Chạy automation",
        confirmText: "Kích hoạt"
      });

    if (!confirmed) return;

    try {
      const response = await fetch(
        `/api/automations/${encodeURIComponent(item.id)}/run-now`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ confirmed: true })
        });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(body.error || "Không thể chạy automation.");
      }

      setFeedback(body.requiresConfirmation
        ? "Automation đã dừng ở bước cần confirmation. Hãy xử lý bước đó trong Tasks, sau đó bấm Tiếp tục sau review."
        : (body.message || "Automation đã chạy."));
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thể chạy automation.", true);
    }
  }

  async function setEnabled(item, enabled) {
    try {
      const response = await fetch(
        `/api/automations/${encodeURIComponent(item.id)}/enabled`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ enabled, confirmed: true })
        });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(body.error || "Không thể đổi trạng thái automation.");
      }

      setFeedback(enabled ? "Đã bật automation." : "Đã tạm dừng automation.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thể đổi trạng thái automation.", true);
    }
  }

  async function resume(item) {
    const confirmFn = window.PersonalAiUi?.confirm;
    if (typeof confirmFn !== "function") {
      setFeedback("Không mở được hộp xác nhận an toàn.", true);
      return;
    }

    const confirmed = await confirmFn(
      "Chỉ tiếp tục sau khi bạn đã kiểm tra task và xử lý bước cần xác nhận/gián đoạn nếu có.",
      {
        title: "Tiếp tục automation",
        confirmText: "Đã review, tiếp tục"
      });
    if (!confirmed) return;

    try {
      const response = await fetch(
        `/api/automations/${encodeURIComponent(item.id)}/resume`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ confirmed: true })
        });
      const body = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(body.error || "Không thể tiếp tục automation.");
      }

      setFeedback("Automation đã được đưa lại vào lịch.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thể tiếp tục automation.", true);
    }
  }

  async function remove(item) {
    const confirmFn = window.PersonalAiUi?.confirm;
    if (typeof confirmFn !== "function") {
      setFeedback("Không mở được hộp xác nhận an toàn.", true);
      return;
    }

    const confirmed = await confirmFn(
      `Xóa automation "${item.name}"? Task gốc sẽ không bị xóa.`,
      {
        title: "Xóa automation",
        confirmText: "Xóa",
        danger: true
      });
    if (!confirmed) return;

    try {
      const response = await fetch(
        `/api/automations/${encodeURIComponent(item.id)}?confirmed=true`,
        { method: "DELETE" });
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(body.error || "Không thể xóa automation.");
      }

      setFeedback("Đã xóa automation.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thể xóa automation.", true);
    }
  }

  function updateBadge(value) {
    const badge = document.querySelector("#automationBadge");
    if (badge) badge.textContent = String(value);
  }

  function setFeedback(message, isError = false) {
    const node = document.querySelector("#automationFeedback");
    if (!node) return;
    node.textContent = message;
    node.className = isError
      ? "settings-feedback error"
      : "settings-feedback";
  }

  function stateLabel(state) {
    return {
      scheduled: "Đã lên lịch",
      running: "Đang chạy",
      paused: "Tạm dừng",
      "awaiting-confirmation": "Chờ xác nhận",
      completed: "Hoàn tất",
      failed: "Có lỗi",
      interrupted: "Gián đoạn"
    }[state] || state || "Không rõ";
  }

  function safeClass(value) {
    return String(value || "unknown")
      .replace(/[^a-z0-9-]/gi, "-")
      .toLowerCase();
  }

  function formatDate(value) {
    if (!value) return "—";
    const date = new Date(value);
    return Number.isNaN(date.getTime())
      ? String(value)
      : date.toLocaleString("vi-VN");
  }
})();
