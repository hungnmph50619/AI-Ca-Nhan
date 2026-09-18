(() => {
  const VERSION = "0.7.6";
  let retryTimer = 0;
  let busy = false;
  let lastQuality = null;

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersionLabels();
    ensureQualityPanel();
    document.querySelector("#knowledgeButton")?.addEventListener("click", () => {
      window.clearTimeout(retryTimer);
      retryTimer = window.setTimeout(() => {
        ensureQualityPanel();
        loadQuality().catch(() => {});
      }, 80);
    });
  }

  function updateVersionLabels() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;

    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Kho dữ liệu kiểm tra chất lượng v${VERSION}`;
  }

  function ensureQualityPanel() {
    if (document.querySelector("#v076QualityPanel")) return;
    const management = document.querySelector("#v075Management");
    const filters = management?.querySelector(".v075-filters");
    if (!management || !filters) {
      window.clearTimeout(retryTimer);
      retryTimer = window.setTimeout(ensureQualityPanel, 80);
      return;
    }

    const panel = document.createElement("div");
    panel.id = "v076QualityPanel";
    panel.className = "v076-quality-panel";
    panel.dataset.status = "loading";
    panel.innerHTML = `
      <div class="v076-quality-copy">
        <strong>Chất lượng tìm kiếm trên máy</strong>
        <span id="v076QualitySummary">Đang kiểm tra cơ sở dữ liệu, tệp gốc, các đoạn dữ liệu và véc-tơ…</span>
      </div>
      <div class="v076-quality-actions">
        <button class="secondary-button" id="v076QualityCheck" type="button">Kiểm tra</button>
        <button class="secondary-button" id="v076QualityRepair" type="button">Sửa chỉ mục an toàn</button>
      </div>`;
    filters.before(panel);

    panel.querySelector("#v076QualityCheck")?.addEventListener("click", () => {
      loadQuality().catch(error => renderError(error.message));
    });
    panel.querySelector("#v076QualityRepair")?.addEventListener("click", repairQuality);
    loadQuality().catch(error => renderError(error.message));
  }

  async function loadQuality() {
    if (busy) return;
    const response = await fetch("/api/knowledge/quality/status", { cache: "no-store" });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(payload.error || "Không kiểm tra được chất lượng tìm kiếm.");
    renderQuality(payload);
  }

  function renderQuality(payload) {
    const panel = document.querySelector("#v076QualityPanel");
    const summary = document.querySelector("#v076QualitySummary");
    if (!panel || !summary) return;

    lastQuality = payload;
    const status = payload.status || "error";
    panel.dataset.status = status;
    const label = status === "healthy" ? "Khỏe"
      : status === "degraded" ? "Cần bảo trì"
        : "Có lỗi";
    const issues = Array.isArray(payload.issues) ? payload.issues.length : 0;
    summary.textContent = `${label} · ${payload.healthyDocuments ?? 0}/${payload.totalDocuments ?? 0} tài liệu khỏe · ${issues} vấn đề · ${payload.missingEmbeddings ?? 0} thiếu véc-tơ · ${payload.duplicateContentDocuments ?? 0} nội dung trùng`;

    const repair = document.querySelector("#v076QualityRepair");
    if (repair) {
      repair.disabled = busy || status === "healthy";
      repair.title = status === "error"
        ? "Chỉ sửa các lỗi chỉ mục an toàn; tệp gốc bị mất hoặc mã kiểm tra không khớp sẽ được bỏ qua."
        : "Lập lại chỉ mục hoặc tạo lại véc-tơ cho các lỗi có thể sửa an toàn.";
    }
  }

  function renderError(message) {
    lastQuality = null;
    const panel = document.querySelector("#v076QualityPanel");
    const summary = document.querySelector("#v076QualitySummary");
    if (panel) panel.dataset.status = "error";
    if (summary) summary.textContent = message || "Không kiểm tra được chất lượng tìm kiếm.";
  }

  async function repairQuality() {
    if (busy) return;
    if (!window.confirm("Sửa các lỗi đoạn dữ liệu/véc-tơ có thể phục hồi an toàn? Tệp gốc bị mất hoặc mã kiểm tra không khớp sẽ không bị tự động thay đổi.")) return;

    busy = true;
    setButtonsDisabled(true);
    const summary = document.querySelector("#v076QualitySummary");
    if (summary) summary.textContent = "Đang sửa chỉ mục trên máy…";

    try {
      const response = await fetch("/api/knowledge/quality/repair", { method: "POST" });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(payload.error || "Không sửa được chỉ mục tìm kiếm.");
      renderQuality(payload.statusAfterRepair || {});
      document.querySelector("#knowledgeRefreshButton")?.click();
    } catch (error) {
      renderError(error.message);
    } finally {
      busy = false;
      setButtonsDisabled(false);
      if (lastQuality) renderQuality(lastQuality);
    }
  }

  function setButtonsDisabled(disabled) {
    document.querySelectorAll("#v076QualityPanel button").forEach(button => {
      button.disabled = disabled;
    });
  }
})();
