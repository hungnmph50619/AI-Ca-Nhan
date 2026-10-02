(() => {
  async function syncShellStatus() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    const statusDot = document.querySelector("#statusDot");
    const statusText = document.querySelector("#statusText");

    try {
      const response = await fetch("/api/status?ts=" + Date.now(), {
        cache: "no-store"
      });

      if (!response.ok) {
        throw new Error("HTTP " + response.status);
      }

      const status = await response.json();

      if (brandVersion && status.version) {
        brandVersion.textContent = "Phiên bản " + status.version;
      }

      if (statusDot) {
        statusDot.className = "status-dot " + (status.configured ? "online" : "offline");
      }

      if (statusText) {
        statusText.textContent = status.configured
          ? "Sẵn sàng · Nhà cung cấp: " + status.provider + " · Mô hình: " + status.model
          : "Chưa cấu hình khóa truy cập";
      }

      document.documentElement.dataset.personalAiVersion = status.version || "";
    } catch (error) {
      if (statusDot) statusDot.className = "status-dot offline";
      if (statusText) statusText.textContent = "Không kết nối được máy chủ";
      console.warn("[PersonalAI shell status] Không đồng bộ được trạng thái:", error);
    }
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", syncShellStatus, { once: true });
  } else {
    syncShellStatus();
  }

  window.addEventListener("pageshow", syncShellStatus);
  window.setTimeout(syncShellStatus, 1200);
})();
