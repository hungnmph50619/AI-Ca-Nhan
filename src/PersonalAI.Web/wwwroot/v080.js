(() => {
  const VERSION = "1.8.0";
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
    intro.innerHTML = "<strong>Nhật ký hoạt động công cụ v1.8.0</strong><p>Mỗi đề xuất, lần chạy và lần gửi kết quả ra nhà cung cấp AI được ghi dấu trên máy. Nhật ký không lưu toàn bộ tham số hay nội dung kết quả; chỉ lưu thông tin vận hành và mã băm để đối chiếu.</p>";

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
    note.textContent = "v1.8.0 giữ nguyên quy trình đề xuất → quyền → xác nhận → thực thi. Nhật ký hoạt động được lưu cục bộ tối đa 30 ngày hoặc 2.000 sự kiện; dữ liệu tham số và kết quả đầy đủ không được ghi vào nhật ký." ;

    const activity = createActivitySection();
    card.append(header, intro, permissionLegend, status, list, activity, note);
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
    await Promise.all([loadCatalog(), loadActivity()]);
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

  function createActivitySection() {
    const section = document.createElement("section");
    section.className = "v089-activity";

    const header = document.createElement("div");
    header.className = "v089-activity-header";

    const copy = document.createElement("div");
    const title = document.createElement("strong");
    title.textContent = "Nhật ký hoạt động gần đây";
    const hint = document.createElement("span");
    hint.textContent = "Chỉ lưu dấu vết vận hành, quyền, trạng thái và mã băm; không lưu toàn bộ nội dung tham số hoặc kết quả.";
    copy.append(title, hint);

    const refresh = document.createElement("button");
    refresh.id = "toolsActivityRefresh";
    refresh.type = "button";
    refresh.className = "secondary-button";
    refresh.textContent = "Tải lại";
    refresh.addEventListener("click", () => loadActivity());

    header.append(copy, refresh);

    const status = document.createElement("p");
    status.id = "toolsActivityStatus";
    status.className = "field-hint";
    status.textContent = "Đang đọc nhật ký hoạt động…";

    const list = document.createElement("div");
    list.id = "toolsActivityList";
    list.className = "v089-activity-list";
    list.setAttribute("role", "list");

    section.append(header, status, list);
    return section;
  }

  async function loadActivity() {
    const status = document.querySelector("#toolsActivityStatus");
    const list = document.querySelector("#toolsActivityList");
    if (!status || !list) return;

    status.textContent = "Đang đọc nhật ký hoạt động…";
    try {
      const response = await fetch("/api/tools/activity?limit=30", { cache: "no-store" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không đọc được nhật ký hoạt động.");
      renderActivity(payload);
    } catch (error) {
      status.textContent = error.message || "Không đọc được nhật ký hoạt động.";
      list.replaceChildren();
    }
  }

  function renderActivity(payload) {
    const status = document.querySelector("#toolsActivityStatus");
    const list = document.querySelector("#toolsActivityList");
    if (!status || !list) return;

    const items = Array.isArray(payload.items) ? payload.items : [];
    const retentionDays = Number(payload.retentionDays || 30);
    const maximumEntries = Number(payload.maximumEntries || 2000);
    status.textContent = items.length + " hoạt động gần nhất · lưu tối đa " + retentionDays + " ngày hoặc " + maximumEntries.toLocaleString("vi-VN") + " sự kiện";
    list.replaceChildren();

    if (items.length === 0) {
      const empty = document.createElement("p");
      empty.className = "v089-activity-empty";
      empty.textContent = "Chưa có hoạt động công cụ nào được ghi nhận.";
      list.appendChild(empty);
      return;
    }

    items.forEach(item => list.appendChild(createActivityItem(item)));
  }

  function createActivityItem(item) {
    const article = document.createElement("article");
    article.className = "v089-activity-item";
    article.setAttribute("role", "listitem");

    const header = document.createElement("div");
    header.className = "v089-activity-item-header";
    const tool = document.createElement("strong");
    tool.textContent = toolDisplayName(item.toolName);
    const time = document.createElement("span");
    time.className = "v089-activity-time";
    time.textContent = formatActivityTime(item.occurredAt);
    header.append(tool, time);

    const event = document.createElement("div");
    event.className = "v089-activity-event";
    event.textContent = localizeActivityEvent(item.eventType, item.status);

    const meta = document.createElement("div");
    meta.className = "v089-activity-meta";
    const permissions = Array.isArray(item.requiredPermissions)
      ? item.requiredPermissions.map(localizePermission).join(", ")
      : "";
    if (permissions) appendMeta(meta, "Quyền: " + permissions);
    if (item.planningProvider) {
      appendMeta(meta, "Nhà cung cấp: " + item.planningProvider + (item.planningModel ? " · " + item.planningModel : ""));
    }
    if (item.confirmed === true) appendMeta(meta, "Đã xác nhận thao tác");
    if (item.confirmed === false) appendMeta(meta, "Chưa xác nhận thao tác");
    if (item.externalConfirmed === true) appendMeta(meta, "Đã xác nhận gửi kết quả ra ngoài");
    if (item.externalConfirmed === false) appendMeta(meta, "Không xác nhận gửi kết quả ra ngoài");
    if (Number.isFinite(Number(item.durationMs))) appendMeta(meta, "Thời gian chạy: " + Number(item.durationMs) + " mili giây");

    const argumentHash = shortHash(item.argumentsSha256);
    if (argumentHash) appendMeta(meta, "Dấu vết tham số: " + argumentHash, true);
    const outputHash = shortHash(item.outputSha256);
    if (outputHash) appendMeta(meta, "Dấu vết kết quả: " + outputHash, true);

    article.append(header, event, meta);
    return article;
  }

  function appendMeta(container, text, hash = false) {
    const span = document.createElement("span");
    span.textContent = text;
    if (hash) span.className = "v089-activity-hash";
    container.appendChild(span);
  }

  function localizeActivityEvent(eventType, status) {
    const labels = {
      "proposal-created": "Đã tạo đề xuất công cụ",
      "direct-execution-completed": "Đã chạy công cụ trực tiếp",
      "execution-denied": "Lần chạy bị từ chối",
      "execution-completed": status === "succeeded" ? "Đã chạy công cụ thành công" : "Đã kết thúc lần chạy công cụ",
      "ai-synthesis-denied": "Không gửi kết quả để AI diễn giải",
      "ai-synthesis-completed": "Đã gửi kết quả để AI diễn giải",
      "native-continuation-denied": "Không gửi kết quả để hoàn tất câu trả lời",
      "native-continuation-completed": "Đã hoàn tất câu trả lời bằng AI",
      "task-step-execution-denied": "Bước tác vụ chưa được xác nhận",
      "task-step-execution-completed": status === "succeeded" ? "Đã chạy bước tác vụ thành công" : "Đã kết thúc bước tác vụ"
    };
    return labels[eventType] || "Hoạt động công cụ";
  }

  function formatActivityTime(value) {
    if (!value) return "—";
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return "—";
    return new Intl.DateTimeFormat("vi-VN", { dateStyle: "short", timeStyle: "medium" }).format(date);
  }

  function shortHash(value) {
    const text = String(value || "").trim();
    return text.length >= 12 ? text.slice(0, 12) : text;
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
      "computer.screen.info": "Thông tin màn hình",
      "computer.cursor.position": "Vị trí con trỏ",
      "computer.windows.list": "Danh sách cửa sổ",
      "computer.window.active": "Cửa sổ đang hoạt động",
      "computer.window.focus": "Chuyển focus cửa sổ",
      "computer.cursor.move": "Di chuyển con trỏ",
      "browser.session.info": "Trạng thái phiên duyệt",
      "browser.navigate": "Điều hướng web",
      "browser.page.observe": "Đọc trang web",
      "browser.link.open": "Mở liên kết",
      "connectors.list": "Danh sách connector",
      "connector.http.get": "Đọc connector HTTPS",
      "dev.workspace.inspect": "Phân tích workspace code",
      "dev.search.text": "Tìm trong source",
      "dev.git.status": "Git status",
      "dev.git.diff": "Git diff",
      "dev.dotnet.restore": "dotnet restore",
      "dev.dotnet.build": "dotnet build",
      "dev.dotnet.test": "dotnet test",
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
      "computer.screen.info": "Đọc kích thước desktop và số màn hình trên Windows; không thay đổi trạng thái máy.",
      "computer.cursor.position": "Đọc tọa độ con trỏ hiện tại; không di chuyển con trỏ.",
      "computer.windows.list": "Liệt kê cửa sổ đang hiển thị. Tiêu đề cửa sổ có thể nhạy cảm nên luôn cần xác nhận.",
      "computer.window.active": "Đọc cửa sổ foreground. Tiêu đề cửa sổ có thể nhạy cảm nên luôn cần xác nhận.",
      "computer.window.focus": "Yêu cầu Windows chuyển focus sang cửa sổ đã chọn; cần quyền điều khiển máy và xác nhận.",
      "computer.cursor.move": "Di chuyển con trỏ tới một tọa độ đã chọn; không click và luôn cần xác nhận.",
      "browser.session.info": "Đọc metadata phiên Browser Agent hiện tại; không tạo network request.",
      "browser.navigate": "HTTP GET tới URL công khai qua SSRF guard; luôn cần xác nhận BÊN NGOÀI + TRÌNH DUYỆT.",
      "browser.page.observe": "Đọc snapshot text và link của trang hiện tại; không tạo network request mới.",
      "browser.link.open": "Mở link theo index từ snapshot hiện tại qua URL/DNS guard.",
      "connectors.list": "Liệt kê connector đã cấu hình trong workspace; metadata được coi là nhạy cảm.",
      "connector.http.get": "Đọc JSON/text qua authenticated HTTPS GET cùng origin bằng credential đã mã hóa.",
      "dev.workspace.inspect": "Quét source/project manifest trong workspace mà không chạy shell hoặc process.",
      "dev.search.text": "Tìm chuỗi trong source text với giới hạn file/hit và bỏ qua build output.",
      "dev.git.status": "Đọc git status bằng executable git trực tiếp; không có Git write action.",
      "dev.git.diff": "Đọc git diff với external diff/textconv/submodule traversal bị tắt.",
      "dev.dotnet.restore": "Chạy dotnet restore cho project/solution đã chọn; không nhận arbitrary CLI args.",
      "dev.dotnet.build": "Chạy dotnet build --no-restore cho target trong workspace; project build code có thể thực thi nên luôn cần xác nhận.",
      "dev.dotnet.test": "Chạy dotnet test --no-restore cho target trong workspace; test code là code execution và không được retry tự động.",
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
      SENSITIVE: "NHẠY CẢM",
      COMPUTER: "ĐIỀU KHIỂN MÁY",
      BROWSER: "TRÌNH DUYỆT",
      CONNECTOR: "KẾT NỐI",
      DEVELOPMENT: "PHÁT TRIỂN PHẦN MỀM"
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
