(() => {
  const VERSION = "1.3.0";
  let loading = false;
  let saving = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersion();
    ensureConnectorButton();
    ensureConnectorDialog();
    refreshConnectorBadge().catch(() => {});
  }

  function updateVersion() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;
  }

  function ensureConnectorButton() {
    if (document.querySelector("#connectorButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "connectorButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "⇄";

    const label = document.createElement("span");
    label.textContent = "Kết nối";

    const count = document.createElement("span");
    count.id = "connectorBadge";
    count.className = "tool-count v130-connector-badge";
    count.textContent = "…";
    count.setAttribute("aria-label", "Số kết nối của không gian hiện tại");

    button.append(icon, label, count);

    const browser = document.querySelector("#browserAgentButton");
    const computer = document.querySelector("#computerUseButton");
    const core = document.querySelector("#coreHealthButton");
    const reference = browser || computer || core;
    container.insertBefore(button, reference || null);

    button.addEventListener("click", openConnectorDialog);
  }

  function ensureConnectorDialog() {
    let dialog = document.querySelector("#connectorDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "connectorDialog";
    dialog.className = "settings-dialog v130-connector-dialog";
    dialog.setAttribute("aria-labelledby", "connectorTitle");

    const card = document.createElement("div");
    card.className = "settings-card v130-connector-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "KHẢ NĂNG ĐƯỢC KIỂM SOÁT · THÔNG TIN XÁC THỰC ĐƯỢC MÃ HÓA";

    const title = document.createElement("h2");
    title.id = "connectorTitle";
    title.textContent = "Kết nối v1.3";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng kết nối");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v130-connector-intro";
    intro.innerHTML =
      "<strong>Kết nối chỉ đọc có xác thực</strong>" +
      "<p>v1.3.0 lưu mã Bearer bằng Data Protection và chỉ cho HTTPS GET cùng địa chỉ gốc. Mã truy cập không được hiển thị lại, không đi qua tác nhân trình duyệt và không được đưa vào nhật ký hoạt động công cụ/kiểm toán.</p>";

    const summary = document.createElement("div");
    summary.id = "connectorSummary";
    summary.className = "v130-connector-summary";
    summary.textContent = "Đang đọc…";

    const list = document.createElement("div");
    list.id = "connectorList";
    list.className = "v130-connector-list";
    list.setAttribute("role", "list");

    const empty = document.createElement("div");
    empty.id = "connectorEmpty";
    empty.className = "v130-connector-empty";
    empty.hidden = true;
    empty.innerHTML =
      "<strong>Chưa có kết nối</strong>" +
      "<p>Thêm một địa chỉ gốc HTTPS API và mã Bearer để dùng công cụ connector.http.get.</p>";

    const formTitle = document.createElement("h3");
    formTitle.className = "v130-section-title";
    formTitle.textContent = "Thêm kết nối";

    const form = document.createElement("form");
    form.id = "connectorForm";
    form.className = "v130-connector-form";

    const nameField = field("Tên", "connectorName", "Ví dụ: API nội bộ", "text");
    const urlField = field("URL gốc", "connectorBaseUrl", "https://api.example.com/", "url");
    const tokenField = field("Mã Bearer", "connectorBearerToken", "Mã sẽ được mã hóa trên máy", "password");

    const note = document.createElement("p");
    note.className = "security-copy";
    note.textContent =
      "URL gốc phải là địa chỉ HTTPS công khai trên cổng 443. v1.3 chưa hỗ trợ Gmail/Calendar OAuth, POST, tải lên hoặc gửi webhook.";

    const submit = document.createElement("button");
    submit.id = "connectorSaveButton";
    submit.type = "submit";
    submit.className = "primary-button";
    submit.textContent = "Lưu kết nối";

    form.append(
      nameField.wrapper,
      urlField.wrapper,
      tokenField.wrapper,
      note,
      submit);

    form.addEventListener("submit", async event => {
      event.preventDefault();
      await createConnector(
        nameField.input.value,
        urlField.input.value,
        tokenField.input.value);
    });

    const feedback = document.createElement("div");
    feedback.id = "connectorFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    card.append(
      header,
      intro,
      summary,
      list,
      empty,
      formTitle,
      form,
      feedback);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
    document.body.appendChild(dialog);
    return dialog;
  }

  function field(labelText, id, placeholder, type) {
    const wrapper = document.createElement("label");
    wrapper.className = "v130-field";

    const label = document.createElement("span");
    label.textContent = labelText;

    const input = document.createElement("input");
    input.id = id;
    input.type = type;
    input.placeholder = placeholder;
    input.autocomplete = type === "password" ? "off" : "off";
    input.required = true;

    wrapper.append(label, input);
    return { wrapper, input };
  }

  async function openConnectorDialog() {
    const dialog = ensureConnectorDialog();
    dialog.showModal();
    await loadConnectors();
  }

  async function refreshConnectorBadge() {
    const response = await fetch("/api/connectors", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) return;

    const count = document.querySelector("#connectorBadge");
    if (count) {
      const items = Array.isArray(payload.connections)
        ? payload.connections
        : [];
      count.textContent = String(items.length);
    }
  }

  async function loadConnectors() {
    if (loading) return;
    loading = true;
    setFeedback("");

    try {
      const [statusResponse, listResponse] = await Promise.all([
        fetch("/api/connectors/status", { cache: "no-store" }),
        fetch("/api/connectors", { cache: "no-store" })
      ]);

      const status = await statusResponse.json().catch(() => ({}));
      const list = await listResponse.json().catch(() => ({}));
      if (!statusResponse.ok || !listResponse.ok) {
        throw new Error(
          status.error
          || list.error
          || "Không đọc được nền tảng kết nối.");
      }

      renderConnectors(status, list);
      await refreshConnectorBadge();
    } catch (error) {
      setFeedback(
        error.message || "Không đọc được nền tảng kết nối.",
        true);
    } finally {
      loading = false;
    }
  }

  function renderConnectors(status, response) {
    const summary = document.querySelector("#connectorSummary");
    const list = document.querySelector("#connectorList");
    const empty = document.querySelector("#connectorEmpty");
    if (!summary || !list || !empty) return;

    const items = Array.isArray(response.connections)
      ? response.connections
      : [];

    summary.textContent =
      `${items.length}/${Number(response.maximumConnections) || 20} kết nối · mã hóa thông tin xác thực: ${status.secretsEncrypted ? "bật" : "tắt"} · thao tác ghi: ${status.writeActionsEnabled ? "bật" : "tắt"}`;

    list.replaceChildren();
    items.forEach(item => list.appendChild(createConnectorItem(item)));
    empty.hidden = items.length > 0;
  }

  function createConnectorItem(item) {
    const article = document.createElement("article");
    article.className = "v130-connector-item";
    article.setAttribute("role", "listitem");

    const top = document.createElement("div");
    top.className = "v130-connector-item-top";

    const name = document.createElement("strong");
    name.textContent = item.name || "Kết nối";

    const state = document.createElement("span");
    state.className = "v130-connector-state";
    state.textContent = item.enabled ? "Đang bật" : "Đã tắt";
    top.append(name, state);

    const url = document.createElement("p");
    url.className = "v130-connector-url";
    url.textContent = item.baseUrl || "—";

    const meta = document.createElement("p");
    meta.className = "v130-connector-meta";
    meta.textContent =
      `${item.kind || "http-bearer"} · thông tin xác thực: ${item.hasCredential ? "đã lưu" : "thiếu"}`;

    const actions = document.createElement("div");
    actions.className = "v130-connector-actions";

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "secondary-button";
    remove.textContent = "Xóa";
    remove.addEventListener("click", () => deleteConnector(item));
    actions.appendChild(remove);

    article.append(top, url, meta, actions);
    return article;
  }

  async function createConnector(name, baseUrl, bearerToken) {
    if (saving) return;
    saving = true;
    setFeedback("Đang mã hóa và lưu thông tin xác thực…");

    const button = document.querySelector("#connectorSaveButton");
    if (button) button.disabled = true;

    try {
      const response = await fetch("/api/connectors", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name,
          kind: "http-bearer",
          baseUrl,
          bearerToken,
          confirmed: true
        })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        throw new Error(payload.error || "Không thể lưu kết nối.");
      }

      const form = document.querySelector("#connectorForm");
      if (form) form.reset();
      setFeedback("Đã lưu kết nối. Mã truy cập sẽ không được hiển thị lại.");
      await loadConnectors();
    } catch (error) {
      setFeedback(error.message || "Không thể lưu kết nối.", true);
    } finally {
      saving = false;
      if (button) button.disabled = false;
    }
  }

  async function deleteConnector(item) {
    const ask = window.PersonalAiUi?.confirm;
    if (typeof ask !== "function") {
      setFeedback("Không mở được hộp xác nhận an toàn.", true);
      return;
    }

    const confirmed = await ask(
      `Xóa kết nối "${item.name || "Kết nối"}"?\n\nThông tin xác thực đã mã hóa của kết nối này cũng sẽ bị xóa khỏi máy.`,
      {
        title: "Xác nhận xóa kết nối",
        confirmText: "Xóa kết nối",
        danger: true
      });

    if (!confirmed) return;

    try {
      const response = await fetch(
        `/api/connectors/${encodeURIComponent(item.id)}?confirmed=true`,
        { method: "DELETE" });
      if (!response.ok) {
        const payload = await response.json().catch(() => ({}));
        throw new Error(payload.error || "Không thể xóa kết nối.");
      }

      setFeedback("Đã xóa kết nối và thông tin xác thực đã lưu.");
      await loadConnectors();
    } catch (error) {
      setFeedback(error.message || "Không thể xóa kết nối.", true);
    }
  }

  function setFeedback(message, isError = false) {
    const node = document.querySelector("#connectorFeedback");
    if (!node) return;
    node.textContent = message;
    node.className = isError
      ? "settings-feedback error"
      : "settings-feedback";
  }
})();
