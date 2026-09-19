(() => {
  const VERSION = "1.6.0";
  let loading = false;

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
    if (document.querySelector("#lifeContextButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "lifeContextButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "◌";

    const label = document.createElement("span");
    label.textContent = "Life Context";

    const badge = document.createElement("span");
    badge.id = "lifeContextBadge";
    badge.className = "tool-count v160-life-badge";
    badge.textContent = "…";

    button.append(icon, label, badge);

    const android = document.querySelector("#androidCompanionButton");
    const dev = document.querySelector("#developmentAgentButton");
    const reference = android || dev;
    container.insertBefore(button, reference || null);

    button.addEventListener("click", openDialog);
  }

  function ensureDialog() {
    let dialog = document.querySelector("#lifeContextDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "lifeContextDialog";
    dialog.className = "settings-dialog v160-life-dialog";
    dialog.setAttribute("aria-labelledby", "lifeContextTitle");

    const card = document.createElement("div");
    card.className = "settings-card v160-life-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "CONSENT · RETENTION · WORKSPACE";

    const title = document.createElement("h2");
    title.id = "lifeContextTitle";
    title.textContent = "Life Context v1.6";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Life Context");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());

    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v160-life-intro";
    intro.innerHTML =
      "<strong>Không tự thu thập dữ liệu đời sống</strong>" +
      "<p>v1.6 chỉ lưu snapshot được bạn chủ động nhập/import. Mỗi source có consent và retention riêng; content được mã hóa local. GPS, calendar, activity và sensor background collection vẫn tắt.</p>";

    const summary = document.createElement("div");
    summary.id = "lifeContextSummary";
    summary.className = "v160-life-summary";
    summary.textContent = "Đang kiểm tra…";

    const formTitle = document.createElement("h3");
    formTitle.className = "v160-section-title";
    formTitle.textContent = "Tạo source";

    const form = document.createElement("form");
    form.id = "lifeContextSourceForm";
    form.className = "v160-source-form";

    const name = createInput("Tên source", "lifeSourceName", "Ví dụ: Lịch cá nhân", "text");

    const kindWrap = document.createElement("label");
    kindWrap.className = "v160-field";
    const kindLabel = document.createElement("span");
    kindLabel.textContent = "Loại";
    const kind = document.createElement("select");
    kind.id = "lifeSourceKind";
    ["manual", "calendar", "location", "activity", "device", "other"].forEach(value => {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = sourceKindLabel(value);
      kind.appendChild(option);
    });
    kindWrap.append(kindLabel, kind);

    const retention = createInput("Retention (ngày)", "lifeSourceRetention", "30", "number");
    retention.input.min = "1";
    retention.input.max = "365";
    retention.input.value = "30";

    const consent = document.createElement("label");
    consent.className = "v160-consent";
    const consentCheck = document.createElement("input");
    consentCheck.id = "lifeSourceConsent";
    consentCheck.type = "checkbox";
    const consentText = document.createElement("span");
    consentText.textContent = "Tôi đồng ý cho PersonalAI lưu và dùng dữ liệu của source này trong Context Manager theo retention đã chọn.";
    consent.append(consentCheck, consentText);

    const create = document.createElement("button");
    create.type = "submit";
    create.className = "primary-button";
    create.textContent = "Tạo source";

    form.append(name.wrapper, kindWrap, retention.wrapper, consent, create);
    form.addEventListener("submit", async event => {
      event.preventDefault();
      await createSource(
        name.input.value,
        kind.value,
        Number(retention.input.value),
        consentCheck.checked);
    });

    const sourceTitle = document.createElement("h3");
    sourceTitle.className = "v160-section-title";
    sourceTitle.textContent = "Sources";

    const list = document.createElement("div");
    list.id = "lifeContextSourceList";
    list.className = "v160-source-list";

    const empty = document.createElement("div");
    empty.id = "lifeContextEmpty";
    empty.className = "v160-empty";
    empty.hidden = true;
    empty.textContent = "Chưa có Life Context source trong workspace hiện tại.";

    const feedback = document.createElement("div");
    feedback.id = "lifeContextFeedback";
    feedback.className = "settings-feedback";
    feedback.setAttribute("role", "status");
    feedback.setAttribute("aria-live", "polite");

    const actions = document.createElement("div");
    actions.className = "v160-actions";
    const refresh = document.createElement("button");
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Làm mới";
    refresh.addEventListener("click", load);
    actions.appendChild(refresh);

    card.append(header, intro, summary, formTitle, form, sourceTitle, list, empty, feedback, actions);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });

    document.body.appendChild(dialog);
    return dialog;
  }

  function createInput(labelText, id, placeholder, type) {
    const wrapper = document.createElement("label");
    wrapper.className = "v160-field";
    const label = document.createElement("span");
    label.textContent = labelText;
    const input = document.createElement("input");
    input.id = id;
    input.type = type;
    input.placeholder = placeholder;
    input.autocomplete = "off";
    input.required = true;
    wrapper.append(label, input);
    return { wrapper, input };
  }

  async function openDialog() {
    const dialog = ensureDialog();
    dialog.showModal();
    await load();
  }

  async function refreshBadge() {
    const response = await fetch("/api/life-context/sources", { cache: "no-store" });
    if (!response.ok) {
      updateBadge("OFF");
      return;
    }
    const payload = await response.json().catch(() => ({}));
    const sources = Array.isArray(payload.sources) ? payload.sources : [];
    const active = sources.filter(item => item.enabled && item.consentGranted).length;
    updateBadge(String(active));
  }

  async function load() {
    if (loading) return;
    loading = true;
    setFeedback("");

    try {
      const responses = await Promise.all([
        fetch("/api/life-context/status", { cache: "no-store" }),
        fetch("/api/life-context/sources", { cache: "no-store" })
      ]);
      const status = await responses[0].json().catch(() => ({}));
      const payload = await responses[1].json().catch(() => ({}));

      if (!responses[0].ok || !responses[1].ok) {
        throw new Error(status.error || payload.error || "Không đọc được Life Context.");
      }

      render(status, payload);
      await refreshBadge();
    } catch (error) {
      setFeedback(error.message || "Không đọc được Life Context.", true);
      updateBadge("OFF");
    } finally {
      loading = false;
    }
  }

  function render(status, payload) {
    const summary = document.querySelector("#lifeContextSummary");
    const list = document.querySelector("#lifeContextSourceList");
    const empty = document.querySelector("#lifeContextEmpty");
    if (!summary || !list || !empty) return;

    const sources = Array.isArray(payload.sources) ? payload.sources : [];
    const active = sources.filter(source => source.enabled && source.consentGranted).length;

    summary.textContent =
      "Workspace " + (payload.workspaceId || "personal")
      + " · " + active + "/" + sources.length + " source đang dùng"
      + " · encryption " + (status.contentEncrypted ? "bật" : "tắt")
      + " · automatic collection " + (status.automaticCollectionEnabled ? "bật" : "tắt");

    list.replaceChildren();
    sources.forEach(source => list.appendChild(sourceCard(source)));
    empty.hidden = sources.length > 0;
  }

  function sourceCard(source) {
    const article = document.createElement("article");
    article.className = "v160-source";

    const head = document.createElement("div");
    head.className = "v160-source-head";

    const meta = document.createElement("div");
    const name = document.createElement("strong");
    name.textContent = source.name || "Life Context";
    const info = document.createElement("span");
    info.textContent =
      sourceKindLabel(source.kind)
      + " · retention " + (source.retentionDays || 0)
      + " ngày · " + (source.entryCount || 0) + " entry";
    meta.append(name, info);

    const state = document.createElement("span");
    state.className = "v160-state " + (source.consentGranted && source.enabled ? "is-on" : "is-off");
    state.textContent = !source.consentGranted
      ? "Consent revoked"
      : source.enabled
        ? "Đang dùng"
        : "Đã tắt";

    head.append(meta, state);

    const snapshot = document.createElement("div");
    snapshot.className = "v160-snapshot";
    const textarea = document.createElement("textarea");
    textarea.rows = 2;
    textarea.maxLength = 2000;
    textarea.placeholder = "Nhập snapshot đời sống bạn muốn PersonalAI được phép dùng…";
    textarea.disabled = !source.consentGranted || !source.enabled;

    const add = document.createElement("button");
    add.type = "button";
    add.className = "secondary-button";
    add.textContent = "Lưu snapshot";
    add.disabled = textarea.disabled;
    add.addEventListener("click", async () => {
      await addEntry(source, textarea.value);
      textarea.value = "";
    });
    snapshot.append(textarea, add);

    const actions = document.createElement("div");
    actions.className = "v160-source-actions";

    if (source.consentGranted) {
      const toggle = document.createElement("button");
      toggle.type = "button";
      toggle.className = "secondary-button";
      toggle.textContent = source.enabled ? "Tắt" : "Bật";
      toggle.addEventListener("click", () => setEnabled(source, !source.enabled));

      const revoke = document.createElement("button");
      revoke.type = "button";
      revoke.className = "secondary-button";
      revoke.textContent = "Thu hồi consent";
      revoke.addEventListener("click", () => revokeConsent(source));

      actions.append(toggle, revoke);
    } else {
      const grant = document.createElement("button");
      grant.type = "button";
      grant.className = "secondary-button";
      grant.textContent = "Cấp lại consent";
      grant.addEventListener("click", () => grantConsent(source));
      actions.appendChild(grant);
    }

    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "secondary-button";
    remove.textContent = "Xóa source";
    remove.addEventListener("click", () => deleteSource(source));
    actions.appendChild(remove);

    article.append(head, snapshot, actions);
    return article;
  }

  async function createSource(name, kind, retentionDays, consentGranted) {
    if (!consentGranted) {
      setFeedback("Bạn phải tích consent trước khi tạo Life Context source.", true);
      return;
    }

    try {
      const response = await fetch("/api/life-context/sources", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: name,
          kind: kind,
          retentionDays: retentionDays,
          consentGranted: true,
          confirmed: true
        })
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không thể tạo source.");

      const form = document.querySelector("#lifeContextSourceForm");
      if (form) form.reset();
      const retention = document.querySelector("#lifeSourceRetention");
      if (retention) retention.value = "30";

      setFeedback("Đã tạo source với consent explicit.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thể tạo source.", true);
    }
  }

  async function addEntry(source, content) {
    const normalized = String(content || "").trim();
    if (normalized.length < 3) {
      setFeedback("Snapshot cần ít nhất 3 ký tự.", true);
      return;
    }

    try {
      const response = await fetch(
        "/api/life-context/sources/" + encodeURIComponent(source.id) + "/entries",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            content: normalized,
            confirmed: true
          })
        });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không thể lưu snapshot.");

      setFeedback("Đã lưu snapshot mã hóa local.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thể lưu snapshot.", true);
    }
  }

  async function setEnabled(source, enabled) {
    try {
      const response = await fetch(
        "/api/life-context/sources/" + encodeURIComponent(source.id) + "/enabled",
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ enabled: enabled, confirmed: true })
        });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không đổi được trạng thái source.");

      setFeedback(enabled ? "Đã bật source." : "Đã tắt source.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không đổi được source.", true);
    }
  }

  async function revokeConsent(source) {
    const confirmed = await confirmAction(
      "Thu hồi consent của \"" + source.name + "\" và xóa toàn bộ snapshot hiện có?\n\nSource sẽ bị tắt ngay.",
      "Thu hồi consent",
      "Thu hồi & xóa");
    if (!confirmed) return;

    await setConsent(source, false, true);
  }

  async function grantConsent(source) {
    const confirmed = await confirmAction(
      "Cấp lại consent cho \"" + source.name + "\"?\n\nSource vẫn ở trạng thái tắt cho tới khi bạn bật lại.",
      "Cấp lại consent",
      "Cấp consent");
    if (!confirmed) return;

    await setConsent(source, true, false);
  }

  async function setConsent(source, granted, purgeExisting) {
    try {
      const response = await fetch(
        "/api/life-context/sources/" + encodeURIComponent(source.id) + "/consent",
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            granted: granted,
            purgeExisting: purgeExisting,
            confirmed: true
          })
        });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không thay đổi được consent.");

      setFeedback(granted
        ? "Đã cấp lại consent. Bạn có thể bật source khi cần."
        : "Đã thu hồi consent và purge snapshot.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thay đổi được consent.", true);
    }
  }

  async function deleteSource(source) {
    const confirmed = await confirmAction(
      "Xóa source \"" + source.name + "\" và toàn bộ dữ liệu liên quan?",
      "Xóa Life Context source",
      "Xóa source");
    if (!confirmed) return;

    try {
      const response = await fetch(
        "/api/life-context/sources/" + encodeURIComponent(source.id) + "?confirmed=true",
        { method: "DELETE" });
      if (!response.ok) {
        const payload = await response.json().catch(() => ({}));
        throw new Error(payload.error || "Không thể xóa source.");
      }

      setFeedback("Đã xóa source.");
      await load();
    } catch (error) {
      setFeedback(error.message || "Không thể xóa source.", true);
    }
  }

  async function confirmAction(message, title, confirmText) {
    const fn = window.PersonalAiUi && window.PersonalAiUi.confirm;
    if (typeof fn !== "function") {
      setFeedback("Không mở được hộp xác nhận an toàn.", true);
      return false;
    }

    return await fn(message, {
      title: title,
      confirmText: confirmText,
      danger: true
    });
  }

  function updateBadge(value) {
    const badge = document.querySelector("#lifeContextBadge");
    if (badge) badge.textContent = value;
  }

  function setFeedback(message, isError) {
    const node = document.querySelector("#lifeContextFeedback");
    if (!node) return;
    node.textContent = message || "";
    node.className = isError
      ? "settings-feedback error"
      : "settings-feedback";
  }

  function sourceKindLabel(value) {
    return {
      manual: "Thủ công",
      calendar: "Lịch",
      location: "Vị trí",
      activity: "Hoạt động",
      device: "Thiết bị",
      other: "Khác"
    }[value] || value || "Source";
  }
})();
