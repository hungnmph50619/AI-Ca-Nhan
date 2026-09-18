(() => {
  const VERSION = "0.6.5";
  const DEFAULT_TEMPORARY_DAYS = 30;
  let memories = [];

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init, { once: true });
  else init();

  function init() {
    setVersion();
    window.addEventListener("personalai:memory-rendered", event => {
      if (Array.isArray(event.detail?.memories)) memories = event.detail.memories;
      setTimeout(() => {
        decorateRows();
        renderLifecycleStats();
      }, 0);
    });
    window.addEventListener("personalai:memory-edit", event => {
      applyEditorLifecycle(event.detail?.memory);
    });
    window.addEventListener("personalai:memory-edit-reset", () => applyEditorLifecycle(null));
    window.addEventListener("personalai:memory-lifecycle-changed", syncExpirationVisibility);
    document.querySelector("#memoryButton")?.addEventListener("click", () => setTimeout(upgrade, 40));
    setTimeout(upgrade, 120);
  }

  function setVersion() {
    const brand = document.querySelector(".brand > div:last-child > span");
    if (brand) brand.textContent = `Phiên bản ${VERSION}`;
    const memoryIntro = document.querySelector(".memory-intro strong");
    if (memoryIntro) memoryIntro.textContent = `Trí nhớ v${VERSION}`;
    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Hỏi đáp có nguồn v${VERSION}`;
  }

  function upgrade() {
    const form = document.querySelector("#memoryForm");
    const kind = document.querySelector("#memoryKind");
    if (!form || !kind) return;

    if (!document.querySelector("#memoryLifecycleFields")) {
      const lifecycle = document.createElement("div");
      lifecycle.id = "memoryLifecycleFields";
      lifecycle.className = "memory-v065-lifecycle";
      lifecycle.innerHTML = `
        <label>
          <span class="field-label">Vòng đời</span>
          <select id="memoryRetention">
            <option value="long-term">Dài hạn</option>
            <option value="temporary">Tạm thời</option>
          </select>
        </label>
        <label id="memoryExpiresField" hidden>
          <span class="field-label">Hết hạn</span>
          <input id="memoryExpiresAt" type="datetime-local">
        </label>
        <p class="field-hint memory-v065-hint">Trí nhớ tạm thời mặc định hết hạn sau 30 ngày. Trí nhớ cũ hoặc hết hạn sẽ không được đưa vào ngữ cảnh trò chuyện.</p>`;
      kind.insertAdjacentElement("afterend", lifecycle);
      document.querySelector("#memoryRetention")?.addEventListener("change", () => {
        ensureTemporaryExpiry();
        syncExpirationVisibility();
      });
    }

    syncExpirationVisibility();
    decorateRows();
    loadMemoriesForLifecycle();
  }

  async function loadMemoriesForLifecycle() {
    try {
      const response = await fetch("/api/memory", { cache: "no-store" });
      if (!response.ok) return;
      const payload = await response.json();
      if (Array.isArray(payload)) memories = payload;
      decorateRows();
      renderLifecycleStats();
    } catch {
      // The core memory UI already reports loading errors.
    }
  }

  function syncExpirationVisibility() {
    const retention = document.querySelector("#memoryRetention");
    const expiresField = document.querySelector("#memoryExpiresField");
    if (!retention || !expiresField) return;
    expiresField.hidden = retention.value !== "temporary";
  }

  function ensureTemporaryExpiry() {
    const retention = document.querySelector("#memoryRetention");
    const expires = document.querySelector("#memoryExpiresAt");
    if (!retention || !expires || retention.value !== "temporary" || expires.value) return;
    const date = new Date(Date.now() + DEFAULT_TEMPORARY_DAYS * 24 * 60 * 60 * 1000);
    expires.value = toLocalDateTime(date);
  }

  function applyEditorLifecycle(memory) {
    const retention = document.querySelector("#memoryRetention");
    const expires = document.querySelector("#memoryExpiresAt");
    if (!retention || !expires) return;
    retention.value = memory?.retention || "long-term";
    expires.value = memory?.expiresAt ? toLocalDateTime(new Date(memory.expiresAt)) : "";
    if (retention.value === "temporary" && !expires.value) ensureTemporaryExpiry();
    syncExpirationVisibility();
  }

  function decorateRows() {
    document.querySelectorAll("#memoryList .memory-row").forEach(row => {
      row.querySelector(".memory-v065-badges")?.remove();
      row.querySelector(".memory-v065-expiry")?.remove();

      const id = row.querySelector("[data-memory-edit]")?.dataset.memoryEdit
        || row.querySelector("[data-memory-delete-v062]")?.dataset.memoryDeleteV062;
      const memory = memories.find(item => item.id === id);
      if (!memory) return;

      const body = row.querySelector(".memory-row-body");
      const badges = document.createElement("div");
      badges.className = "memory-v065-badges";
      badges.appendChild(makeBadge(memory.retention === "temporary" ? "Tạm thời" : "Dài hạn", memory.retention === "temporary" ? "temporary" : "long-term"));
      if (memory.isExpired) badges.appendChild(makeBadge("Hết hạn", "expired"));
      else if (memory.isStale) badges.appendChild(makeBadge("Cần xem lại", "stale"));
      body?.insertBefore(badges, body.querySelector("p"));

      if (memory.retention === "temporary" && memory.expiresAt) {
        const expiry = document.createElement("div");
        expiry.className = "memory-v065-expiry";
        expiry.textContent = memory.isExpired
          ? `Đã hết hạn ${formatDate(memory.expiresAt)}`
          : `Hết hạn ${formatDate(memory.expiresAt)}`;
        body?.appendChild(expiry);
      } else if (memory.isStale) {
        const stale = document.createElement("div");
        stale.className = "memory-v065-expiry";
        stale.textContent = "Thông tin này đã lâu chưa được cập nhật và sẽ không tự động dùng khi trò chuyện.";
        body?.appendChild(stale);
      }

      row.classList.toggle("memory-expired", !!memory.isExpired);
      row.classList.toggle("memory-stale", !!memory.isStale && !memory.isExpired);
    });
  }

  function makeBadge(text, type) {
    const badge = document.createElement("span");
    badge.className = `memory-v065-badge memory-v065-badge-${type}`;
    badge.textContent = text;
    return badge;
  }

  function renderLifecycleStats() {
    const node = document.querySelector("#memoryV064Stats");
    if (!node) return;
    node.querySelector(".memory-v065-stats")?.remove();
    const temporary = memories.filter(memory => memory.retention === "temporary").length;
    const expired = memories.filter(memory => memory.isExpired).length;
    const stale = memories.filter(memory => memory.isStale).length;
    const extra = document.createElement("span");
    extra.className = "memory-v065-stats";
    extra.textContent = ` · ${temporary} tạm thời · ${expired} hết hạn · ${stale} cần xem lại`;
    node.appendChild(extra);
  }

  function toLocalDateTime(date) {
    if (!(date instanceof Date) || Number.isNaN(date.getTime())) return "";
    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
    return local.toISOString().slice(0, 16);
  }

  function formatDate(value) {
    try {
      return new Intl.DateTimeFormat("vi-VN", { dateStyle: "short", timeStyle: "short" }).format(new Date(value));
    } catch {
      return value || "—";
    }
  }
})();
