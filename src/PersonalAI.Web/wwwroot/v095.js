(() => {
  const VERSION = "0.9.5";

  window.PersonalAiContextManager = Object.freeze({
    version: VERSION,
    maximumCharacters: 7600,
    strategy: "workspace-scoped-budgeted-context-v1"
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
    badge.textContent = "Context Manager";
    badge.title = "v0.9.5 chỉ chọn Memory, Documents và Tasks liên quan trong workspace hiện tại, theo ngân sách context.";
    clearButton.before(badge);
  }
})();
