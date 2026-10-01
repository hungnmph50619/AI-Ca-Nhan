(() => {
  const VERSION = "3.0.0";
  let dialog;
  let content;
  let statusText;

  document.addEventListener("DOMContentLoaded", () => {
    updateVersion();
    ensureUi();
  });

  function updateVersion() {
    document.querySelectorAll(".brand span").forEach(node => {
      node.textContent = `Phiên bản ${VERSION}`;
    });
  }

  function ensureUi() {
    const tools = document.querySelector(".sidebar-tools");
    if (!tools || document.getElementById("osButton")) return;

    const button = document.createElement("button");
    button.className = "settings-button";
    button.id = "osButton";
    button.type = "button";

    const icon = document.createElement("span");
    icon.setAttribute("aria-hidden", "true");
    icon.textContent = "◎";

    const label = document.createElement("span");
    label.textContent = "Personal AI OS";

    const badge = document.createElement("span");
    badge.className = "tool-count";
    badge.id = "osSidebarBadge";
    badge.textContent = "v3.0";

    button.append(icon, label, badge);
    tools.prepend(button);

    dialog = document.createElement("dialog");
    dialog.className = "settings-dialog";
    dialog.id = "osDialog";
    dialog.setAttribute("aria-labelledby", "osTitle");

    const card = document.createElement("div");
    card.className = "settings-card os-dialog os-card";

    const header = document.createElement("header");
    header.className = "settings-header";

    const heading = document.createElement("div");
    const eyebrow = document.createElement("p");
    eyebrow.className = "eyebrow";
    eyebrow.textContent = "HỆ ĐIỀU HÀNH AI CÁ NHÂN · v3.0";

    const title = document.createElement("h2");
    title.id = "osTitle";
    title.textContent = "Bảng điều khiển hệ thống";

    statusText = document.createElement("p");
    statusText.className = "os-status-copy";
    statusText.textContent = "Đang đọc trạng thái hệ thống…";

    heading.append(eyebrow, title);

    const close = document.createElement("button");
    close.className = "dialog-close";
    close.type = "button";
    close.setAttribute("aria-label", "Đóng");
    close.textContent = "×";

    header.append(heading, close);

    content = document.createElement("div");
    content.className = "os-loading";
    content.textContent = "Đang tải danh mục khả năng…";

    card.append(header, statusText, content);
    dialog.append(card);
    document.body.append(dialog);

    button.addEventListener("click", open);
    close.addEventListener("click", () => dialog.close());
    dialog.addEventListener("click", event => {
      if (event.target === dialog) dialog.close();
    });
  }

  async function open() {
    if (!dialog) return;
    dialog.showModal();
    await refresh();
  }

  async function refresh() {
    content.className = "os-loading";
    content.textContent = "Đang tải danh mục khả năng…";

    try {
      const [statusResponse, manifestResponse] = await Promise.all([
        fetch("/api/os/status", { headers: workspaceHeaders() }),
        fetch("/api/os/manifest", { headers: workspaceHeaders() })
      ]);

      if (!statusResponse.ok || !manifestResponse.ok) {
        throw new Error("Không đọc được trạng thái Personal AI OS.");
      }

      const status = await statusResponse.json();
      const manifest = await manifestResponse.json();
      render(status, manifest);
    } catch (error) {
      content.className = "os-error";
      content.textContent = error instanceof Error
        ? error.message
        : "Không đọc được trạng thái Personal AI OS.";
    }
  }

  function render(status, manifest) {
    statusText.textContent =
      `${status.workspaceName} · ${status.readiness.osContractReady ? "Hệ thống sẵn sàng" : "Hệ thống cần kiểm tra"} · trạng thái tiếp theo: ${nextStageLabel(status.nextStage)}`;

    const root = document.createElement("div");

    const summary = document.createElement("section");
    summary.className = "os-summary";
    [
      ["Khả năng", status.readiness.capabilityCount],
      ["Sẵn sàng", status.readiness.readyCount],
      ["Có kiểm soát", status.readiness.controlledCount],
      ["Nền tảng", status.readiness.foundationCount]
    ].forEach(([label, value]) => {
      const item = document.createElement("div");
      item.className = "os-summary-item";
      const small = document.createElement("span");
      small.textContent = label;
      const strong = document.createElement("strong");
      strong.textContent = String(value);
      item.append(small, strong);
      summary.append(item);
    });

    const byId = new Map(
      (status.capabilities || []).map(item => [item.id, item])
    );

    const layers = document.createElement("section");
    layers.className = "os-layer-list";

    (status.layers || []).forEach(layer => {
      const block = document.createElement("article");
      block.className = "os-layer";

      const layerHeading = document.createElement("div");
      layerHeading.className = "os-layer-heading";
      const h3 = document.createElement("h3");
      h3.textContent = layerName(layer.id, layer.name);
      const count = document.createElement("span");
      count.textContent = `${layer.capabilityIds.length} khả năng`;
      layerHeading.append(h3, count);

      const purpose = document.createElement("p");
      purpose.textContent = localizeText(layer.purpose);

      const list = document.createElement("div");
      list.className = "os-capabilities";

      layer.capabilityIds.forEach(id => {
        const capability = byId.get(id);
        if (!capability) return;

        const row = document.createElement("div");
        row.className = "os-capability";

        const name = document.createElement("div");
        name.className = "os-capability-name";
        name.textContent = capabilityName(capability.id, capability.name);

        const state = document.createElement("div");
        state.className = "os-capability-state";
        state.textContent = capabilityState(capability.state);

        const detail = document.createElement("div");
        detail.className = "os-capability-detail";
        detail.textContent = localizeText(capability.detail);

        row.append(name, state, detail);
        list.append(row);
      });

      block.append(layerHeading, purpose, list);
      layers.append(block);
    });

    const boundaries = document.createElement("section");
    boundaries.className = "os-boundaries";
    const boundariesTitle = document.createElement("h3");
    boundariesTitle.textContent = "Ranh giới v3.0";
    const ul = document.createElement("ul");
    (manifest.boundaries || []).forEach(value => {
      const li = document.createElement("li");
      li.textContent = localizeText(value);
      ul.append(li);
    });
    boundaries.append(boundariesTitle, ul);

    root.append(summary, layers, boundaries);
    content.className = "";
    content.replaceChildren(root);
  }

  function nextStageLabel(value) {
    return value === "continuous-stable"
      ? "duy trì ổn định liên tục"
      : localizeText(value || "—");
  }

  function layerName(id, fallback) {
    return {
      knowledge: "Dữ liệu và ngữ cảnh",
      reasoning: "Lập kế hoạch và quyết định",
      execution: "Công cụ và tự động hóa",
      interfaces: "Thiết bị và kết nối bên ngoài",
      coordination: "Điều phối tác nhân AI",
      governance: "Quản trị và khôi phục"
    }[id] || localizeText(fallback);
  }

  function capabilityName(id, fallback) {
    return {
      memory: "Trí nhớ",
      knowledge: "Kho dữ liệu và tìm kiếm",
      "life-context": "Ngữ cảnh đời sống",
      tasks: "Công việc",
      "decision-engine": "Bộ hỗ trợ quyết định",
      tools: "Khung công cụ",
      files: "Tệp trong không gian",
      automation: "Tự động hóa",
      "software-development": "Phát triển phần mềm",
      desktop: "Điều khiển máy tính",
      browser: "Trình duyệt",
      connectors: "Kết nối tài khoản",
      email: "Thư điện tử",
      calendar: "Lịch",
      android: "Ứng dụng Android đồng hành",
      audit: "Nhật ký kiểm toán",
      undo: "Hoàn tác",
      "agent-framework": "Hệ thống tác nhân AI",
      hardening: "Độ tin cậy và bảo mật",
      "continuous-improvement": "Tự cải tiến liên tục"
    }[id] || localizeText(fallback);
  }

  function capabilityState(value) {
    return {
      ready: "Sẵn sàng",
      controlled: "Có kiểm soát",
      foundation: "Nền tảng",
      unconfigured: "Chưa cấu hình",
      unavailable: "Không khả dụng"
    }[String(value || "").toLowerCase()] || localizeText(value);
  }

  function localizeText(value) {
    let text = String(value || "");
    [
      ["workspace-scoped", "được tách riêng theo không gian"],
      ["workspace", "không gian"],
      ["lifecycle", "vòng đời"],
      ["context selection", "lựa chọn ngữ cảnh"],
      ["Context", "Ngữ cảnh"],
      ["context", "ngữ cảnh"],
      ["local storage", "lưu trữ trên máy"],
      ["local", "trên máy"],
      ["parsing", "đọc cấu trúc"],
      ["chunking", "chia đoạn"],
      ["embeddings", "véc-tơ ngữ nghĩa"],
      ["hybrid retrieval", "tìm kiếm kết hợp"],
      ["citations", "trích dẫn"],
      ["consent", "sự đồng ý"],
      ["retention", "thời gian lưu"],
      ["encrypted", "được mã hóa"],
      ["Task Engine", "Bộ máy công việc"],
      ["dependencies", "phụ thuộc"],
      ["recovery", "khôi phục"],
      ["retry", "thử lại"],
      ["step-level permission", "quyền theo từng bước"],
      ["evidence", "bằng chứng"],
      ["trade-off", "điểm được–mất"],
      ["uncertainty", "mức độ chưa chắc chắn"],
      ["auto-action", "tự hành động"],
      ["Tool registry", "Danh mục công cụ"],
      ["validation", "kiểm tra hợp lệ"],
      ["permission policy", "chính sách quyền"],
      ["confirmation", "xác nhận"],
      ["result handling", "xử lý kết quả"],
      ["Filesystem sandbox", "Vùng tệp cách ly"],
      ["SHA guards", "bảo vệ SHA"],
      ["Scheduler", "Bộ lập lịch"],
      ["background", "nền"],
      ["auto-run", "tự chạy"],
      ["auto-confirm", "tự xác nhận"],
      ["Inspect/search/git read/build/test", "Kiểm tra/tìm kiếm/đọc Git/build/test"],
      ["arbitrary shell", "lệnh shell tùy ý"],
      ["Git write", "ghi Git"],
      ["Computer Use", "Điều khiển máy tính"],
      ["capability", "khả năng"],
      ["interactive session", "phiên tương tác"],
      ["Browser Agent", "Tác nhân trình duyệt"],
      ["SSRF guards", "bảo vệ SSRF"],
      ["login/form submit/browser side effects", "đăng nhập/gửi biểu mẫu/hành động gây thay đổi trên trình duyệt"],
      ["Authenticated HTTPS read connector", "Kết nối HTTPS chỉ đọc có xác thực"],
      ["bearer credential", "thông tin xác thực bearer"],
      ["write actions", "thao tác ghi"],
      ["read foundation", "nền tảng chỉ đọc"],
      ["mailbox model", "mô hình hộp thư"],
      ["send action", "thao tác gửi"],
      ["snapshot", "bản ghi"],
      ["auto-sync", "tự đồng bộ"],
      ["calendar write", "ghi lịch"],
      ["Paired-device companion", "Thiết bị đồng hành đã ghép nối"],
      ["hashed token", "mã truy cập đã băm"],
      ["remote tool execution", "chạy công cụ từ xa"],
      ["raw secret/prompt content", "bí mật thô/nội dung lời nhắc"],
      ["Guarded inverse operations", "Thao tác hoàn tác có bảo vệ"],
      ["pre-action state", "trạng thái trước hành động"],
      ["explicit confirmation", "xác nhận rõ ràng"],
      ["workflow", "quy trình"],
      ["handoff", "chuyển giao"],
      ["credential", "thông tin xác thực"],
      ["automatic delegation", "tự giao việc"],
      ["Rate/resource guards", "Giới hạn tần suất/tài nguyên"],
      ["permission audit", "kiểm toán quyền"],
      ["crash marker", "dấu sự cố"],
      ["backup/restore", "sao lưu/khôi phục"],
      ["foundation", "nền tảng"],
      ["continuous self-improvement", "tự cải tiến liên tục"],
      ["nightly scheduler", "bộ lập lịch ban đêm"],
      ["explicit", "rõ ràng"],
      ["Autonomous development", "Phát triển tự động"],
      ["low-risk", "rủi ro thấp"],
      ["bypass permission", "bỏ qua quyền"],
      ["review", "rà soát"],
      ["security", "bảo mật"],
      ["benchmark", "đánh giá chuẩn"],
      ["merge-policy", "chính sách hợp nhất"],
      ["local-sync", "đồng bộ về máy"],
      ["External AI", "AI bên ngoài"],
      ["side effects", "hành động gây thay đổi"],
      ["Emergency stop", "Dừng khẩn cấp"],
      ["execution", "thực thi"],
      ["system log", "nhật ký hệ thống"],
      ["Loop guard", "Bảo vệ chống vòng lặp"],
      ["resource limits", "giới hạn tài nguyên"],
      ["database migration", "nâng cấp cơ sở dữ liệu"],
      ["fail-closed", "chặn khi không an toàn"],
      ["disaster recovery", "khôi phục thảm họa"],
      ["undo/revalidation", "hoàn tác/kiểm tra lại"],
      ["Decision Engine", "Bộ hỗ trợ quyết định"],
      ["recommendation", "đề xuất"],
      ["Email/Calendar", "Thư điện tử/Lịch"],
      ["provider-specific", "theo nhà cung cấp"],
      ["write capability", "khả năng ghi"],
      ["connector/life-context", "kết nối/ngữ cảnh đời sống"],
      ["Parallel tool calls", "Gọi công cụ song song"],
      ["general autonomous agent loop", "vòng lặp tác nhân tự động tổng quát"],
      ["continuous development controller", "bộ điều phối phát triển liên tục"]
    ].forEach(([from, to]) => {
      text = text.split(from).join(to);
    });
    return text;
  }

  function workspaceHeaders() {
    const workspace =
      window.PersonalAiWorkspace?.currentId || "personal";
    return {
      "X-PersonalAI-Workspace": workspace
    };
  }
})();
