(() => {
  const ADVANCED_KEY = "personal-ai-simple-ux-advanced-open";
  let syncing = false;

  function createButton(id, className, text) {
    const button = document.createElement("button");
    button.id = id;
    button.type = "button";
    button.className = className;
    button.textContent = text;
    return button;
  }

  function syncSidebar() {
    if (syncing) return;
    const sidebar = document.querySelector("#sidebar");
    const tools = sidebar?.querySelector(".sidebar-tools");
    const settings = document.querySelector("#settingsButton");
    if (!sidebar || !tools || !settings) return;

    syncing = true;
    try {
      let scroll = sidebar.querySelector(".simple-sidebar-scroll");
      if (!scroll) {
        scroll = document.createElement("div");
        scroll.className = "simple-sidebar-scroll";

        const conversation = sidebar.querySelector(".conversation-section");
        if (conversation) scroll.appendChild(conversation);

        const mainLabel = document.createElement("div");
        mainLabel.className = "simple-section-label";
        mainLabel.textContent = "Chức năng";
        scroll.appendChild(mainLabel);

        const main = document.createElement("div");
        main.className = "simple-main-tools";
        scroll.appendChild(main);

        const advancedToggle = createButton(
          "simpleAdvancedToggle",
          "simple-advanced-toggle",
          "Nâng cao");
        advancedToggle.setAttribute("aria-expanded", "false");
        scroll.appendChild(advancedToggle);

        const advanced = document.createElement("div");
        advanced.className = "simple-advanced-tools";
        advanced.id = "simpleAdvancedTools";
        advanced.hidden = true;
        scroll.appendChild(advanced);

        const footer = document.createElement("div");
        footer.className = "simple-sidebar-footer";
        footer.appendChild(settings);

        const privacy = sidebar.querySelector(".privacy-note");
        sidebar.insertBefore(scroll, privacy || null);
        sidebar.insertBefore(footer, privacy || null);

        advancedToggle.addEventListener("click", () => {
          const open = advanced.hidden;
          advanced.hidden = !open;
          advancedToggle.setAttribute("aria-expanded", String(open));
          try {
            localStorage.setItem(ADVANCED_KEY, open ? "1" : "0");
          } catch {}
        });

        try {
          if (localStorage.getItem(ADVANCED_KEY) === "1") {
            advanced.hidden = false;
            advancedToggle.setAttribute("aria-expanded", "true");
          }
        } catch {}
      }

      const main = sidebar.querySelector(".simple-main-tools");
      const advanced = sidebar.querySelector(".simple-advanced-tools");
      const mainIds = new Set([
        "knowledgeButton",
        "automationButton",
        "computerUseButton"
      ]);

      Array.from(tools.children).forEach(node => {
        if (!(node instanceof HTMLElement)) return;
        if (node === settings) return;

        if (mainIds.has(node.id)) {
          if (node.parentElement !== main) main?.appendChild(node);
        } else {
          if (node.parentElement !== advanced) advanced?.appendChild(node);
        }
      });

      ["knowledgeButton", "automationButton", "computerUseButton"].forEach(id => {
        const node = document.getElementById(id);
        if (node && node.parentElement !== main) main?.appendChild(node);
      });

      if (settings.parentElement?.classList.contains("simple-sidebar-footer") !== true) {
        sidebar.querySelector(".simple-sidebar-footer")?.appendChild(settings);
      }
    } finally {
      syncing = false;
    }
  }

  function syncComposerOptions() {
    const wrap = document.querySelector(".composer-wrap");
    const form = document.querySelector("#chatForm");
    const send = document.querySelector("#sendButton");
    if (!wrap || !form || !send) return;

    let button = document.querySelector("#simpleComposerOptionsButton");
    let panel = document.querySelector("#simpleComposerOptions");

    if (!button) {
      button = createButton(
        "simpleComposerOptionsButton",
        "simple-composer-options-button",
        "⋯");
      button.setAttribute("aria-label", "Tùy chọn trò chuyện");
      button.setAttribute("title", "Tùy chọn");
      button.setAttribute("aria-expanded", "false");
      form.insertBefore(button, send);
    }

    if (!panel) {
      panel = document.createElement("div");
      panel.id = "simpleComposerOptions";
      panel.className = "simple-composer-options";
      panel.hidden = true;
      wrap.insertBefore(panel, form);

      button.addEventListener("click", event => {
        event.stopPropagation();
        const open = panel.hidden;
        panel.hidden = !open;
        button.setAttribute("aria-expanded", String(open));
      });

      document.addEventListener("click", event => {
        if (panel.hidden) return;
        if (panel.contains(event.target) || button.contains(event.target)) return;
        panel.hidden = true;
        button.setAttribute("aria-expanded", "false");
      });

      document.addEventListener("keydown", event => {
        if (event.key !== "Escape" || panel.hidden) return;
        panel.hidden = true;
        button.setAttribute("aria-expanded", "false");
        button.focus();
      });
    }

    const knowledge = document.querySelector("#knowledgeChatControls");
    const toolToggle = document.querySelector(".tool-orchestration-toggle-row");
    if (knowledge && knowledge.parentElement !== panel) panel.appendChild(knowledge);
    if (toolToggle && toolToggle.parentElement !== panel) panel.appendChild(toolToggle);
  }

  function simplifyStatus() {
    const text = document.querySelector("#statusText");
    if (!text) return;

    const full = text.textContent?.trim() || "";
    if (!full) return;

    if (full.includes("Nhà cung cấp:") || full.includes("Mô hình:")) {
      text.title = full;
      text.textContent = "Sẵn sàng";
    }
  }

  function normalizeComputerStatus() {
    const badge = document.querySelector("#computerUseBadge");
    if (!badge) return;

    const current = (badge.textContent || "").trim().toUpperCase();
    const labels = {
      "ĐỨNG": "Sẵn sàng",
      "IDLE": "Sẵn sàng",
      "PAUSED": "Tạm dừng",
      "TẠM DỪNG": "Tạm dừng",
      "RUNNING": "Đang chạy",
      "ĐANG CHẠY": "Đang chạy"
    };
    if (labels[current]) badge.textContent = labels[current];
  }

  function syncAll() {
    syncSidebar();
    syncComposerOptions();
    simplifyStatus();
    normalizeComputerStatus();
  }

  const observer = new MutationObserver(() => {
    window.clearTimeout(observer._timer);
    observer._timer = window.setTimeout(syncAll, 25);
  });

  function start() {
    syncAll();
    observer.observe(document.body, {
      childList: true,
      subtree: true,
      characterData: true
    });
    window.setTimeout(syncAll, 250);
    window.setTimeout(syncAll, 1000);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", start, { once: true });
  } else {
    start();
  }
})();
