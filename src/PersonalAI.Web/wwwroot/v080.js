(() => {
  const VERSION = "0.8.8";
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
    intro.innerHTML = "<strong>Hoàn tất câu trả lời từ kết quả công cụ v0.8.8</strong><p>Sau khi một đề xuất gọi hàm gốc được bạn cho phép chạy, PersonalAI có thể gửi kết quả trở lại đúng nhà cung cấp và mô hình để tạo câu trả lời cuối. Lượt tiếp tục không cấp thêm công cụ.</p>";

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

    const note = document.createElement("p");
    note.className = "security-copy";
    note.textContent = "v0.8.8 giữ quy trình đề xuất → quyền → xác nhận → thực thi. Kết quả công cụ được tóm tắt trên máy trước; chỉ khi bạn xác nhận thì kết quả mới được gửi về đúng nhà cung cấp và mô hình, đồng thời lượt tiếp tục không được phép gọi thêm công cụ." ;

    card.append(header, intro, permissionLegend, status, list, note);
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
    await loadCatalog();
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
