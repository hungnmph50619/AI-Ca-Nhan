(() => {
  const VERSION = "1.6.0";

  window.PersonalAiContextManager = Object.freeze({
    version: VERSION,
    maximumCharacters: 7600,
    strategy: "workspace-scoped-budgeted-context-v2-life-context"
  });

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) {
      brandVersion.textContent = `Phiên bản ${VERSION}`;
    }

    const topbar = document.querySelector(".topbar");
    const clearButton = document.querySelector("#clearButton");
    if (!topbar || !clearButton || document.querySelector("#contextManagerBadge")) {
      return;
    }

    const badge = document.createElement("span");
    badge.id = "contextManagerBadge";
    badge.className = "context-manager-badge";
    badge.textContent = "Trình quản lý ngữ cảnh";
    badge.title = "Chọn Trí nhớ, Tài liệu, Công việc và Ngữ cảnh đời sống đã được bạn cho phép trong không gian hiện tại, theo giới hạn ngữ cảnh.";
    clearButton.before(badge);
  }
})();
