(() => {
  const VERSION = "0.6.4";
  const api = "/api/memory";
  let memories = [];

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init, { once: true });
  else init();

  function init() {
    setVersion();
    window.addEventListener("personalai:memory-rendered", handleMemoryRendered);
    const button = document.querySelector("#memoryButton");
    button?.addEventListener("click", () => setTimeout(upgrade, 50));
    setTimeout(upgrade, 150);
  }

  function setVersion() {
    const brand = document.querySelector(".brand > div:last-child > span");
    if (brand) brand.textContent = `Phiên bản ${VERSION}`;
    const intro = document.querySelector(".memory-intro strong");
    if (intro) intro.textContent = `Trí nhớ v${VERSION}`;
  }

  function handleMemoryRendered(event) {
    const renderedMemories = event.detail?.memories;
    if (Array.isArray(renderedMemories)) {
      memories = renderedMemories;
      renderStats(statsFromMemories(memories));
    }
    setTimeout(decorateRows, 0);
  }

  function upgrade() {
    const card = document.querySelector(".memory-card");
    const list = document.querySelector("#memoryList");
    if (!card || !list || document.querySelector("#memoryV064Toolbar")) return;

    const toolbar = document.createElement("section");
    toolbar.id = "memoryV064Toolbar";
    toolbar.className = "memory-v064-toolbar";
    toolbar.innerHTML = `<div class="memory-v064-stats" id="memoryV064Stats">Đang tải thống kê…</div><div class="memory-v064-actions"><button type="button" class="secondary-button" id="memoryExportButton">Xuất dữ liệu</button><label class="secondary-button memory-import-label">Nhập dữ liệu<input id="memoryImportInput" type="file" accept="application/json,.json" hidden></label><button type="button" class="memory-danger-button" id="memoryDeleteAllButton">Xóa tất cả</button></div>`;

    const header = document.querySelector(".memory-list-header");
    (header || list).insertAdjacentElement("beforebegin", toolbar);
    document.querySelector("#memoryExportButton")?.addEventListener("click", exportMemories);
    document.querySelector("#memoryImportInput")?.addEventListener("change", importMemories);
    document.querySelector("#memoryDeleteAllButton")?.addEventListener("click", deleteAll);
    list.addEventListener("change", toggleEnabled);
    load();
  }

  async function load() {
    try {
      const [memoryResponse, statsResponse] = await Promise.all([
        fetch(api, { cache: "no-store" }),
        fetch(`${api}/stats`, { cache: "no-store" })
      ]);

      if (!memoryResponse.ok || !statsResponse.ok) throw new Error();
      memories = await memoryResponse.json();
      const stats = await statsResponse.json();
      renderStats(stats);
      setTimeout(decorateRows, 0);
    } catch {
      renderStats(null);
    }
  }

  function statsFromMemories(items) {
    return {
      total: items.length,
      enabled: items.filter(memory => memory.isEnabled !== false).length,
      disabled: items.filter(memory => memory.isEnabled === false).length,
      facts: items.filter(memory => memory.kind === "fact").length,
      preferences: items.filter(memory => memory.kind === "preference").length,
      rules: items.filter(memory => memory.kind === "rule").length
    };
  }

  function renderStats(stats) {
    const node = document.querySelector("#memoryV064Stats");
    if (!node) return;
    if (!stats) {
      node.textContent = "Không tải được thống kê.";
      return;
    }
    node.innerHTML = `<strong>${stats.total}</strong> tổng · <strong>${stats.enabled}</strong> đang bật · ${stats.disabled} tắt · ${stats.facts} thông tin · ${stats.preferences} sở thích · ${stats.rules} quy tắc`;
  }

  function decorateRows() {
    document.querySelectorAll("#memoryList .memory-row").forEach(row => {
      if (row.querySelector("[data-memory-toggle]")) return;

      const id = row.querySelector("[data-memory-edit]")?.dataset.memoryEdit
        || row.querySelector("[data-memory-delete-v062]")?.dataset.memoryDeleteV062;
      const memory = memories.find(item => item.id === id);
      if (!memory) return;

      const body = row.querySelector(".memory-row-body");
      const meta = document.createElement("div");
      meta.className = "memory-v064-meta";
      meta.textContent = `Tạo ${formatDate(memory.createdAt)} · Cập nhật ${formatDate(memory.updatedAt)}`;
      body?.appendChild(meta);

      const actions = row.querySelector(".memory-v062-row-actions");
      const label = document.createElement("label");
      label.className = "memory-toggle";
      label.title = memory.isEnabled ? "Đang dùng cho AI" : "Không dùng cho AI";
      label.innerHTML = `<input type="checkbox" data-memory-toggle="${memory.id}" ${memory.isEnabled ? "checked" : ""}><span>${memory.isEnabled ? "Bật" : "Tắt"}</span>`;
      actions?.prepend(label);
      row.classList.toggle("memory-disabled", !memory.isEnabled);
    });
  }

  async function toggleEnabled(event) {
    const input = event.target.closest?.("[data-memory-toggle]");
    if (!input) return;

    input.disabled = true;
    try {
      const response = await fetch(`${api}/${encodeURIComponent(input.dataset.memoryToggle)}/enabled`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ isEnabled: input.checked })
      });
      const updated = await response.json().catch(() => null);
      if (!response.ok || !updated) throw new Error();

      memories = memories.map(memory => memory.id === updated.id ? updated : memory);
      renderStats(statsFromMemories(memories));
      input.closest(".memory-row")?.classList.toggle("memory-disabled", !updated.isEnabled);
      const text = input.parentElement?.querySelector("span");
      if (text) text.textContent = updated.isEnabled ? "Bật" : "Tắt";
      input.parentElement.title = updated.isEnabled ? "Đang dùng cho AI" : "Không dùng cho AI";

      // Keep the v0.6.2 memory editor's in-memory copy synchronized as well.
      document.querySelector("#memoryRefreshButton")?.click();
    } catch {
      input.checked = !input.checked;
      await window.PersonalAiUi.notice("Không thể thay đổi trạng thái trí nhớ.", { title: "Không thể thực hiện" });
    } finally {
      input.disabled = false;
    }
  }

  async function exportMemories() {
    const response = await fetch(`${api}/export`, { cache: "no-store" });
    if (!response.ok) return window.PersonalAiUi.notice("Không thể xuất trí nhớ.", { title: "Không thể xuất dữ liệu" });

    const data = await response.json();
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `personal-ai-tri-nho-${new Date().toISOString().slice(0, 10)}.json`;
    link.click();
    URL.revokeObjectURL(url);
  }

  async function importMemories(event) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) return window.PersonalAiUi.notice("Tệp nhập quá lớn. Tối đa 5 MB.", { title: "Tệp quá lớn" });

    try {
      const parsed = JSON.parse(await file.text());
      const source = Array.isArray(parsed) ? parsed : parsed.memories;
      if (!Array.isArray(source)) throw new Error();
      if (!(await window.PersonalAiUi.confirm(`Nhập ${source.length} trí nhớ? Nội dung trùng sẽ được bỏ qua.`, { title: "Xác nhận nhập trí nhớ", confirmText: "Nhập dữ liệu" }))) return;

      const response = await fetch(`${api}/import`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ memories: source, skipDuplicates: true })
      });
      const result = await response.json();
      if (!response.ok) throw new Error(result.error || "Nhập thất bại.");

      await window.PersonalAiUi.notice(`Đã nhập ${result.imported}, bỏ qua ${result.skipped} trí nhớ trùng.`, { title: "Nhập dữ liệu hoàn tất" });
      location.reload();
    } catch (error) {
      await window.PersonalAiUi.notice(error.message || "Tệp dữ liệu không hợp lệ.", { title: "Không thể nhập dữ liệu" });
    }
  }

  async function deleteAll() {
    if (!memories.length) return window.PersonalAiUi.notice("Chưa có trí nhớ để xóa.", { title: "Không có dữ liệu" });

    const phrase = await window.PersonalAiUi.prompt(`Thao tác này sẽ xóa toàn bộ ${memories.length} trí nhớ. Nhập XOA TAT CA để xác nhận:`, "", { title: "Xác nhận xóa toàn bộ trí nhớ", confirmText: "Tiếp tục", placeholder: "XOA TAT CA" });
    if (phrase !== "XOA TAT CA") return;

    const response = await fetch(api, { method: "DELETE" });
    if (!response.ok) return window.PersonalAiUi.notice("Không thể xóa toàn bộ trí nhớ.", { title: "Không thể xóa dữ liệu" });

    const result = await response.json();
    await window.PersonalAiUi.notice(`Đã xóa ${result.deleted} trí nhớ.`, { title: "Đã xóa dữ liệu" });
    location.reload();
  }

  function formatDate(value) {
    if (!value) return "—";
    try {
      return new Intl.DateTimeFormat("vi-VN", { dateStyle: "short", timeStyle: "short" }).format(new Date(value));
    } catch {
      return value;
    }
  }
})();
