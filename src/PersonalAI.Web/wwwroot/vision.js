(() => {
  "use strict";
  const input = document.getElementById("imageInput");
  const consent = document.getElementById("consentCheck");
  const button = document.getElementById("analyzeButton");
  const preview = document.getElementById("preview");
  const status = document.getElementById("visionStatus");
  const result = document.getElementById("result");
  const meta = document.getElementById("resultMeta");
  const heading = document.getElementById("resultHeading");
  const maximumImageBytes = 2 * 1024 * 1024;
  let available = false;
  let busy = false;
  let previewUrl = null;

  function updateReady() {
    const file = input.files && input.files[0];
    button.disabled = busy || !available || !consent.checked || !file ||
      file.size < 24 || file.size > maximumImageBytes ||
      !["image/png", "image/jpeg"].includes(file.type);
  }

  input.addEventListener("change", () => {
    if (previewUrl) URL.revokeObjectURL(previewUrl);
    previewUrl = null;
    preview.hidden = true;
    preview.removeAttribute("src");
    consent.checked = false; // Consent is given per selected image, never reused.
    const file = input.files && input.files[0];
    if (file) {
      if (!["image/png", "image/jpeg"].includes(file.type) ||
          file.size > maximumImageBytes || file.size < 24) {
        result.textContent = "Chỉ chọn một ảnh PNG/JPG hợp lệ và không vượt quá 2 MB.";
      } else {
        previewUrl = URL.createObjectURL(file);
        preview.src = previewUrl;
        preview.hidden = false;
        result.textContent = "Đã chọn ảnh; bạn cần xác nhận trước khi gửi ảnh tới Gemini.";
      }
    }
    meta.textContent = "";
    updateReady();
  });
  consent.addEventListener("change", updateReady);

  async function readJson(response) {
    const data = await response.json();
    if (!response.ok) throw new Error(data.error || "Máy chủ không thể xử lý yêu cầu này.");
    return data;
  }

  button.addEventListener("click", async () => {
    const file = input.files && input.files[0];
    if (!file || !available || !consent.checked || busy) return;
    busy = true;
    updateReady();
    result.textContent = "Đang phân tích ảnh đã lưu…";
    meta.textContent = "Chưa có kết quả: đợi mô hình nhận diện, không dùng để cảnh báo trong trận.";
    try {
      const form = new FormData();
      form.append("image", file, file.name);
      form.append("confirmed", "true");
      const response = await fetch("/api/vision/minimap/analyze", {
        method: "POST",
        body: form,
        credentials: "same-origin"
      });
      const data = await readJson(response);
      result.textContent = data.analysis;
      meta.textContent = "Ảnh đã lưu " + data.width + "×" + data.height +
        " · Gemini: " + data.model +
        " · Kết quả chưa được xác minh, không dùng trên HUD trận đấu.";
      heading.focus();
    } catch (error) {
      result.textContent = "Chưa đọc được ảnh: " + String(error.message || error);
      meta.textContent = "Ảnh không được gửi lại tự động. Hãy kiểm tra cấu hình và thử khi bạn muốn.";
      heading.focus();
    } finally {
      busy = false;
      updateReady();
    }
  });

  fetch("/api/vision/minimap/status", { credentials: "same-origin" })
    .then(readJson)
    .then(data => {
      available = data.available === true;
      status.textContent = available
        ? "Gemini đã được cấu hình. Bạn có thể chọn ảnh đã lưu để phân tích khi đồng ý gửi ảnh."
        : "Chưa có Gemini được cấu hình. Hãy vào Cài đặt AI, chọn Gemini và lưu khóa trước khi đọc ảnh.";
      updateReady();
    })
    .catch(error => {
      status.textContent = "Không đọc được trạng thái AI thị giác: " + String(error.message || error);
      available = false;
      updateReady();
    });
  window.addEventListener("pagehide", () => {
    if (previewUrl) URL.revokeObjectURL(previewUrl);
  });
})();
