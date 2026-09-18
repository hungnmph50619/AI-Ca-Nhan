(() => {
  const VERSION = "0.8.9";
  let loading = false;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersionLabels();
    ensureToolsButton();
    ensureToolsDialog();
    refreshCount().catch(() => {});
  }

  function updateVersionLabels() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;
  }

  function ensureToolsButton() {
    if (document.querySelector("#toolsButton")) return;
    const container = document.querySelector(".sidebar-tools");
    if (!container) return;

    const button = document.createElement("button");
    button.id = "toolsButton";
    button.type = "button";
    button.className = "settings-button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "⌘";
    const label = document.createElement("span");
    label.textContent = "Công cụ";
    const count = document.createElement("span");
    count.id = "toolsSidebarCount";
    count.className = "tool-count";
    count.setAttribute("aria-label", "Số công cụ đã đăng ký");
    count.textContent = "0";
    button.append(icon, label, count);

    const settings = document.querySelector("#settingsButton");
    container.insertBefore(button, settings || null);
    button.addEventListener("click", openToolsDialog);
  }

  function ensureToolsDialog() {
    let dialog = document.querySelector("#toolsDialog");
    if (dialog) return dialog;

    dialog = document.createElement("dialog");
    dialog.id = "toolsDialog";
    dialog.className = "settings-dialog v080-tool-dialog";
    dialog.setAttribute("aria-labelledby", "toolsTitle");

    const card = document.createElement("div");
    card.className = "settings-card v080-tool-card";

    const header = document.createElement("header");
    header.className = "settings-header";
    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "CÔNG CỤ NỘI BỘ TRÊN MÁY NÀY";
    const title = document.createElement("h2");
    title.id = "toolsTitle";
    title.textContent = "Công cụ";
    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.type = "button";
    close.className = "dialog-close";
    close.setAttribute("aria-label", "Đóng Công cụ");
    close.textContent = "×";
    close.addEventListener("click", () => dialog.close());
    header.append(heading, close);

    const intro = document.createElement("div");
    intro.className = "v080-framework-intro";
    intro.innerHTML = "<strong>Nhật ký công cụ v0.8.9</strong><p>Mỗi lần công cụ được gọi, PersonalAI ghi lại dấu vết thực thi trên máy gồm thời điểm, trạng thái, quyền, xác nhận và dấu băm của dữ liệu vào/ra. Nhật ký không lưu toàn bộ nội dung tệp hoặc kết quả nhạy cảm.</p>";

    const permissionLegend = document.createElement("div");
    permissionLegend.className = "v080-permission-legend";
    permissionLegend.innerHTML = "<span>ĐỌC</span><span>GHI</span><span>XÓA</span><span>BÊN NGOÀI</span><span>NHẠY CẢM</span>";

    const status = document.createElement("p");
    status.id = "toolsStatus";
    status.className = "field-hint v080-tool-status";
    status.textContent = "Đang đọc danh mục công cụ…";

    const list = document.createElement("div");
    list.id = "toolsList";
    list.className = "v080-tool-list";
    list.setAttribute("role", "list");

    const auditHeading = document.createElement("div");
    auditHeading.className = "v080-audit-heading";
    const auditTitle = document.createElement("strong");
    auditTitle.textContent = "Nhật ký thực thi gần đây";
    const auditRefresh = document.createElement("button");
    auditRefresh.type = "button";
    auditRefresh.className = "secondary-button";
    auditRefresh.textContent = "Tải lại nhật ký";
    auditRefresh.addEventListener("click", () => loadAudit());
    auditHeading.append(auditTitle, auditRefresh);

    const auditList = document.createElement("div");
    auditList.id = "toolsAuditList";
    auditList.className = "v080-audit-list";

    const note = document.createElement("p");
    note.className = "security-copy";
    note.textContent = "v0.8.9 lưu nhật ký công cụ bền vững trên máy và không cung cấp chức năng xóa nhật ký cho AI. Nội dung đầu vào/đầu ra không được lưu nguyên văn; chỉ lưu dấu băm và kích thước để kiểm tra dấu vết." ;

    card.append(header, intro, permissionLegend, status, list, auditHeading, auditList, note);
    dialog.appendChild(card);
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
    document.body.appendChild(dialog);
    return dialog;
  }

  async function openToolsDialog() {
    const dialog = ensureToolsDialog();
    dialog.showModal();
    await Promise.all([loadCatalog(), loadAudit()]);
  }

  async function refreshCount() {
    const payload = await fetchCatalog();
    const count = document.querySelector("#toolsSidebarCount");
    if (count) count.textContent = String(Array.isArray(payload.tools) ? payload.tools.length : 0);
  }

  async function loadCatalog() {
    if (loading) return;
    loading = true;
    const status = document.querySelector("#toolsStatus");
    if (status) status.textContent = "Đang đọc danh mục công cụ…";

    try {
      const payload = await fetchCatalog();
      renderCatalog(payload);
    } catch (error) {
      if (status) status.textContent = error.message || "Không đọc được danh mục công cụ.";
    } finally {
      loading = false;
    }
  }

  async function fetchCatalog() {
    const response = await fetch("/api/tools", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.error || "Không đọc được danh mục công cụ.");
    return payload;
  }

  function renderCatalog(payload) {
    const tools = Array.isArray(payload.tools) ? payload.tools : [];
    const count = document.querySelector("#toolsSidebarCount");
    if (count) count.textContent = String(tools.length);

    const status = document.querySelector("#toolsStatus");
    if (status) {
      status.textContent = `${tools.length} công cụ đã đăng ký · khung công cụ ${payload.frameworkVersion || VERSION}`;
    }

    const list = document.querySelector("#toolsList");
    if (!list) return;
    list.replaceChildren();

    if (tools.length === 0) {
      const empty = document.createElement("p");
      empty.className = "field-hint";
      empty.textContent = "Chưa có công cụ nào được đăng ký.";
      list.appendChild(empty);
      return;
    }

    tools.forEach(tool => list.appendChild(createToolCard(tool)));
  }

  async function loadAudit() {
    const list = document.querySelector("#toolsAuditList");
    if (!list) return;
    list.replaceChildren(documentElement("p", "field-hint", "Đang đọc nhật ký công cụ…"));

    try {
      const response = await fetch("/api/tools/audit?limit=30", { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không đọc được nhật ký công cụ.");
      renderAudit(Array.isArray(payload.entries) ? payload.entries : []);
    } catch (error) {
      list.replaceChildren(documentElement("p", "field-hint", error.message || "Không đọc được nhật ký công cụ."));
    }
  }

  function renderAudit(entries) {
    const list = document.querySelector("#toolsAuditList");
    if (!list) return;
    list.replaceChildren();

    if (!entries.length) {
      list.appendChild(documentElement("p", "field-hint", "Chưa có lần thực thi công cụ nào được ghi nhận."));
      return;
    }

    entries.forEach(entry => {
      const item = document.createElement("article");
      item.className = "v080-audit-item";

      const heading = document.createElement("div");
      heading.className = "v080-audit-item-heading";
      const name = document.createElement("strong");
      name.textContent = toolDisplayName(entry.toolName);
      const time = document.createElement("span");
      time.textContent = formatAuditTime(entry.startedAt);
      heading.append(name, time);

      const summary = document.createElement("p");
      summary.textContent = [
        localizeExecutionStatus(entry.status),
        entry.durationMs == null ? null : entry.durationMs + " mili giây",
        entry.confirmed ? "đã xác nhận" : "không cần xác nhận",
        localizeReversibility(entry.reversibility)
      ].filter(Boolean).join(" · ");

      const meta = document.createElement("div");
      meta.className = "v080-tool-meta";
      (entry.requiredPermissions || []).forEach(permission => {
        const badge = document.createElement("span");
        badge.className = "v080-permission-badge";
        badge.textContent = localizePermission(permission);
        meta.appendChild(badge);
      });

      const actor = document.createElement("span");
      actor.textContent = "Người dùng qua PersonalAI";
      meta.appendChild(actor);

      const details = document.createElement("details");
      const detailsTitle = document.createElement("summary");
      detailsTitle.textContent = "Xem dấu vết kỹ thuật";
      const detailsBody = document.createElement("p");
      detailsBody.textContent =
        "Dữ liệu vào: " + (entry.inputBytes ?? 0) + " byte · SHA-256 " + shortenHash(entry.inputSha256)
        + (entry.outputSha256
          ? " · Dữ liệu ra: " + (entry.outputBytes ?? 0) + " byte · SHA-256 " + shortenHash(entry.outputSha256)
          : "");
      details.append(detailsTitle, detailsBody);

      item.append(heading, summary, meta, details);
      list.appendChild(item);
    });
  }

  function formatAuditTime(value) {
    try {
      return new Intl.DateTimeFormat("vi-VN", {
        dateStyle: "short",
        timeStyle: "medium"
      }).format(new Date(value));
    } catch {
      return "Không rõ thời điểm";
    }
  }

  function localizeExecutionStatus(status) {
    return {
      succeeded: "Đã chạy",
      denied: "Bị từ chối",
      "invalid-input": "Dữ liệu không hợp lệ",
      "not-found": "Không tìm thấy",
      "timed-out": "Quá thời gian",
      failed: "Thất bại"
    }[status] || "Không xác định";
  }

  function localizeReversibility(value) {
    return {
      "not-applicable": "không có thay đổi cần hoàn tác",
      "not-guaranteed": "không bảo đảm hoàn tác tự động",
      "not-automatically-reversible": "không thể hoàn tác tự động",
      "external-dependent": "khả năng hoàn tác phụ thuộc hệ thống ngoài",
      unknown: "chưa xác định khả năng hoàn tác"
    }[value] || "chưa xác định khả năng hoàn tác";
  }

  function shortenHash(value) {
    const text = String(value || "");
    return text.length > 16 ? text.slice(0, 16) + "…" : text || "không có";
  }

  function documentElement(tagName, className, text) {
    const node = document.createElement(tagName);
    if (className) node.className = className;
    node.textContent = text || "";
    return node;
  }

  function toolDisplayName(toolName) {
    const names = {
      "app.summary": "Tổng quan ứng dụng",
      "documents.search": "Tìm trong tài liệu",
      "local.calculate": "Máy tính",
      "local.clock": "Đồng hồ hệ thống",
      "local.date_math": "Tính toán ngày giờ",
      "local.text_stats": "Thống kê văn bản",
      "memory.search": "Tìm trong trí nhớ",
      "workspace.create_directory": "Tạo thư mục",
      "workspace.delete": "Xóa tệp hoặc thư mục",
      "workspace.list": "Liệt kê thư mục làm việc",
      "workspace.move": "Di chuyển hoặc đổi tên",
      "workspace.read_text": "Đọc tệp văn bản",
      "workspace.write_text": "Ghi tệp văn bản"
    };
    return names[toolName] || "Công cụ";
  }

  function toolDisplayDescription(toolName, fallback) {
    const descriptions = {
      "app.summary": "Đọc tóm tắt trạng thái dữ liệu nội bộ gồm trí nhớ, tài liệu và chỉ mục véc-tơ; không trả nội dung chi tiết.",
      "documents.search": "Tìm các đoạn liên quan trong kho tài liệu bằng tìm kiếm từ khóa, véc-tơ và xếp hạng lại trên máy.",
      "local.calculate": "Tính biểu thức số học trên máy với các phép cộng, trừ, nhân, chia, phần trăm và dấu ngoặc; không dùng lệnh hệ thống.",
      "local.clock": "Đọc giờ UTC, giờ hiện tại của máy chủ và múi giờ hệ thống mà không gọi dịch vụ bên ngoài.",
      "local.date_math": "Cộng khoảng thời gian vào một mốc ISO-8601 hoặc tính chênh lệch giữa hai mốc thời gian.",
      "local.text_stats": "Đếm ký tự, từ, dòng và số byte UTF-8 của một đoạn văn bản ngay trên máy.",
      "memory.search": "Tìm trong các trí nhớ cá nhân đang bật bằng cách xếp hạng từ khóa trên máy.",
      "workspace.create_directory": "Tạo thư mục bên trong thư mục làm việc đã cấp quyền; chặn đường dẫn vượt phạm vi và liên kết tượng trưng.",
      "workspace.delete": "Xóa tệp hoặc thư mục rỗng trong thư mục làm việc; không xóa đệ quy và có thể kiểm tra mã băm SHA-256 trước khi xóa tệp.",
      "workspace.list": "Liệt kê trực tiếp các tệp và thư mục trong thư mục làm việc đã cấp quyền; chỉ trả đường dẫn tương đối và không đi qua liên kết tượng trưng.",
      "workspace.move": "Đổi tên hoặc di chuyển tệp/thư mục trong thư mục làm việc; không ghi đè đích và có thể kiểm tra mã băm SHA-256 cho tệp.",
      "workspace.read_text": "Đọc tệp văn bản UTF-8 trong thư mục làm việc đã cấp quyền, có giới hạn kích thước và chặn đường dẫn vượt phạm vi.",
      "workspace.write_text": "Tạo, ghi đè hoặc nối thêm vào tệp văn bản UTF-8 trong thư mục làm việc; chặn đường dẫn vượt phạm vi và hỗ trợ kiểm tra mã băm SHA-256."
    };
    return descriptions[toolName] || fallback || "";
  }

  function localizePermission(permission) {
    const labels = {
      READ: "ĐỌC",
      WRITE: "GHI",
      DELETE: "XÓA",
      EXTERNAL: "BÊN NGOÀI",
      SENSITIVE: "NHẠY CẢM"
    };
    return labels[String(permission || "").toUpperCase()] || "KHÔNG XÁC ĐỊNH";
  }

  function createToolCard(tool) {
    const article = document.createElement("article");
    article.className = "v080-tool-item";
    article.setAttribute("role", "listitem");

    const header = document.createElement("div");
    header.className = "v080-tool-item-header";
    const name = document.createElement("strong");
    name.textContent = toolDisplayName(tool.name);
    const version = document.createElement("span");
    version.textContent = `v${tool.version || "?"}`;
    header.append(name, version);

    const description = document.createElement("p");
    description.textContent = toolDisplayDescription(tool.name, tool.description);

    const meta = document.createElement("div");
    meta.className = "v080-tool-meta";
    const permissions = Array.isArray(tool.requiredPermissions) ? tool.requiredPermissions : [];
    permissions.forEach(permission => {
      const badge = document.createElement("span");
      badge.className = "v080-permission-badge";
      badge.textContent = localizePermission(permission);
      meta.appendChild(badge);
    });

    const timeout = document.createElement("span");
    timeout.textContent = `${tool.timeoutMs || 0} mili giây`;
    meta.appendChild(timeout);
    const locality = document.createElement("span");
    locality.textContent = tool.localOnly ? "trên máy" : "có thể dùng bên ngoài";
    meta.appendChild(locality);
    if (tool.requiresConfirmation) {
      const confirm = document.createElement("span");
      confirm.textContent = "cần xác nhận";
      meta.appendChild(confirm);
    }

    article.append(header, description, meta);
    return article;
  }
})();
