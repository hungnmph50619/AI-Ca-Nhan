(() => {
  "use strict";
  const core = globalThis.MinimapReviewCore;
  const el = id => document.getElementById(id);
  const fileInput = el("imageFile");
  const canvas = el("imageCanvas");
  const context = canvas.getContext("2d");
  const predictionMode = el("predictionMode");
  const predictionBox = el("predictionBox");
  const locateConsent = el("locateConsent");
  const locateButton = el("locateButton");
  const locateStatus = el("locateStatus");
  const truthAbsent = el("truthAbsent");
  const reviewed = el("reviewed");
  const message = el("message");
  const samples = el("samples");
  const exportButton = el("exportDataset");
  const resetButton = el("resetDataset");
  const importFile = el("reviewImportFile");
  const importMode = el("reviewImportMode");
  const importButton = el("reviewImportButton");
  const importStatus = el("reviewImportStatus");
  let importing = false;
  const qualityThreshold = el("qualityThreshold");
  const qualitySummary = el("qualitySummary");
  const qualityErrors = el("qualityErrors");
  const fields = ["x1", "y1", "x2", "y2"].map(el);

  let image = null;
  let imageUrl = null;
  let loadVersion = 0;
  let editVersion = 0;
  let locateBusy = false;

  function updateLocateReady() {
    const file = fileInput.files && fileInput.files[0];
    locateButton.disabled = locateBusy || !image || !file ||
      !locateConsent.checked || file.size < 24 || file.size > 2 * 1024 * 1024 ||
      !["image/png", "image/jpeg"].includes(file.type);
  }
  locateConsent.addEventListener("change", updateLocateReady);
  let dragStart = null;
  let previewBox = null;
  let serial = 0;
  let records = [];

  function notify(text) { message.textContent = text; }
  function releaseImage() {
    if (imageUrl) URL.revokeObjectURL(imageUrl);
    imageUrl = null;
    image = null;
    dragStart = null;
    previewBox = null;
    canvas.hidden = true;
    updateLocateReady();
  }
  function coordinates() {
    return core.box(fields.map(field => {
      // Avoid a blank field becoming 0 or the browser's number coercion.
      if (!/^(0|[1-9][0-9]{0,3})$/.test(field.value.trim())) {
        throw new Error("Hãy nhập đầy đủ bốn tọa độ nguyên 0–1000 cho khung chuẩn.");
      }
      return Number(field.value);
    }));
  }
  function setCoordinates(box) {
    fields.forEach((field, index) => { field.value = String(box[index]); });
  }
  function drawBox(box, color, dashed) {
    if (!box) return;
    const x = box[0] * canvas.width / 1000;
    const y = box[1] * canvas.height / 1000;
    const w = (box[2] - box[0]) * canvas.width / 1000;
    const h = (box[3] - box[1]) * canvas.height / 1000;
    context.save();
    context.strokeStyle = color;
    context.lineWidth = Math.max(2, canvas.width / 300);
    context.setLineDash(dashed ? [Math.max(5, canvas.width / 100), 5] : []);
    context.strokeRect(x, y, w, h);
    context.restore();
  }
  function redraw() {
    if (!image) return;
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.drawImage(image, 0, 0);
    if (predictionMode.value === "found" && predictionBox.value.trim()) {
      try { drawBox(core.parsePrediction(predictionBox.value), "#f7c36b", true); }
      catch { /* Nhập dở thì đợi xác minh khi thêm mẫu. */ }
    }
    if (previewBox) drawBox(previewBox, "#5df2cf", false);
    else if (!truthAbsent.checked) {
      try { drawBox(coordinates(), "#5df2cf", false); }
      catch { /* Chưa nhập đủ bốn số. */ }
    }
  }
  function resetCurrent() {
    loadVersion++;
    fileInput.value = "";
    releaseImage();
    predictionMode.value = "";
    predictionBox.value = "";
    predictionBox.disabled = true;
    locateConsent.checked = false;
    locateStatus.textContent = "Chưa gửi ảnh đến Gemini.";
    updateLocateReady();
    truthAbsent.checked = false;
    reviewed.checked = false;
    fields.forEach(field => { field.value = ""; field.disabled = false; });
  }
  function refreshQuality() {
    if (!records.length) {
      qualitySummary.textContent = "Chưa có mẫu để tính độ chính xác.";
      qualityErrors.textContent = "";
      return;
    }
    try {
      const summary = core.evaluateRecords(records, Number(qualityThreshold.value));
      const pct = n => n === null ? "không xác định" : (n * 100).toFixed(1) + "%";
      qualitySummary.textContent = `Đã kiểm tra ${summary.samples} mẫu · IoU ≥ ${summary.threshold.toFixed(2)} · ` +
        `Đúng khung (TP): ${summary.tp}; báo sai (FP): ${summary.fp}; bỏ sót (FN): ${summary.fn}; ` +
        `đúng khi không có khung (TN): ${summary.tn}. Precision: ${pct(summary.precision)}; ` +
        `Recall: ${pct(summary.recall)}; F1: ${pct(summary.f1)}.`;
      const wrong = summary.details.filter(item => item.outcome !== "true_positive" && item.outcome !== "true_negative");
      qualityErrors.textContent = wrong.length
        ? `Cần xem lại ${wrong.length} mẫu: ${wrong.map(item => item.id).join(", ")}.`
        : "Không có mẫu bị đánh dấu sai theo ngưỡng hiện tại; cần thêm ảnh độc lập để kiểm chứng.";
    } catch (error) {
      qualitySummary.textContent = "Không thể tính kết quả: " + String(error.message || error);
      qualityErrors.textContent = "";
    }
  }
  function refreshSamples() {
    samples.textContent = records.length
      ? `Đã xác minh ${records.length} mẫu: ${records.map(item => item.id).join(", ")}.`
      : "Chưa có mẫu nào.";
    exportButton.disabled = !records.length;
    resetButton.disabled = !records.length;
    refreshQuality();
  }
  qualityThreshold.addEventListener("change", refreshQuality);

  fileInput.addEventListener("change", () => {
    const version = ++loadVersion;
    releaseImage();
    locateConsent.checked = false;
    locateStatus.textContent = "Chưa gửi ảnh đến Gemini.";
    updateLocateReady();
    predictionMode.value = "";
    predictionBox.value = "";
    predictionBox.disabled = true;
    truthAbsent.checked = false;
    reviewed.checked = false;
    fields.forEach(field => { field.value = ""; field.disabled = false; });
    const file = fileInput.files && fileInput.files[0];
    if (!file) { notify("Chưa chọn ảnh."); return; }
    if (!["image/png", "image/jpeg"].includes(file.type) ||
        file.size < 24 || file.size > 2 * 1024 * 1024) {
      notify("Chỉ chọn ảnh PNG/JPG hợp lệ không quá 2 MB."); return;
    }
    const url = URL.createObjectURL(file);
    imageUrl = url;
    const candidate = new Image();
    candidate.onload = () => {
      if (version !== loadVersion) return;
      const width = candidate.naturalWidth;
      const height = candidate.naturalHeight;
      if (width < 64 || height < 64 || width > 4096 || height > 4096 ||
          width * height > 6000000) {
        releaseImage();
        notify("Kích thước ảnh không phù hợp (64–4096 mỗi chiều, tối đa 6 triệu pixel).");
        return;
      }
      image = candidate;
      canvas.width = width;
      canvas.height = height;
      canvas.hidden = false;
      updateLocateReady();
      redraw();
      notify("Đã mở ảnh cục bộ. Chọn trạng thái đề xuất AI, sau đó xác minh khung chuẩn.");
    };
    candidate.onerror = () => {
      if (version !== loadVersion) return;
      releaseImage();
      notify("Không giải mã được ảnh; hãy chọn một ảnh PNG/JPG hợp lệ.");
    };
    candidate.src = url;
  });

  predictionMode.addEventListener("change", () => {
    editVersion++;
    predictionBox.disabled = predictionMode.value !== "found";
    reviewed.checked = false;
    redraw();
  });
  predictionBox.addEventListener("input", () => { editVersion++; reviewed.checked = false; redraw(); });
  // Không gọi mô hình khi chọn ảnh hoặc tích checkbox. Chỉ nút bấm này mới
  // gửi một ảnh đã chọn, sau xác nhận riêng cho ảnh đó.
  locateButton.addEventListener("click", async () => {
    const file = fileInput.files && fileInput.files[0];
    const selectedImage = image;
    if (locateBusy || !file || !image || !locateConsent.checked) return;
    const version = loadVersion;
    const originalEditVersion = editVersion;
    locateBusy = true;
    updateLocateReady();
    locateStatus.textContent = "Đang kiểm tra cấu hình Gemini…";
    try {
      const statusResponse = await fetch("/api/vision/minimap/locate/status", {
        method: "GET", credentials: "same-origin", cache: "no-store"
      });
      if (!statusResponse.ok) throw new Error("Không kết nối được dịch vụ đề xuất khung cục bộ.");
      const status = await statusResponse.json();
      if (!status.available) throw new Error("Hãy chọn Gemini hỗ trợ ảnh và lưu khóa trong Cài đặt AI.");
      if (version !== loadVersion || image !== selectedImage ||
          (fileInput.files && fileInput.files[0]) !== file ||
          !locateConsent.checked) return;

      const form = new FormData();
      form.append("image", file, file.name);
      form.append("confirmed", "true");
      locateStatus.textContent = "Đang gửi riêng ảnh đã đồng ý tới Gemini để lấy khung đề xuất…";
      const response = await fetch("/api/vision/minimap/locate", {
        method: "POST",
        credentials: "same-origin",
        headers: { "X-Xerath-Vision": "1" },
        body: form
      });
      let data;
      try { data = await response.json(); }
      catch { throw new Error("Máy chủ không trả JSON hợp lệ."); }
      if (!response.ok) throw new Error(data.error || "Gemini chưa tìm được khung.");
      if (version !== loadVersion || image !== selectedImage ||
          (fileInput.files && fileInput.files[0]) !== file) return;
      if (originalEditVersion !== editVersion) {
        throw new Error("Bạn đã sửa kết quả đề xuất trong khi chờ Gemini; không ghi đè dữ liệu vừa nhập.");
      }
      const proposal = core.normalizeLocateResponse(data, selectedImage.naturalWidth,
        selectedImage.naturalHeight);
      predictionMode.value = proposal.mode;
      predictionBox.disabled = proposal.mode !== "found";
      predictionBox.value = proposal.coordinates ? JSON.stringify(proposal.coordinates) : "";
      reviewed.checked = false; // Phải xác minh lại, không dùng dự đoán làm nhãn chuẩn.
      redraw();
      locateStatus.textContent = proposal.coordinates
        ? "Đã có khung đề xuất của Gemini (viền vàng). Hãy tự xác minh khung thực tế (viền xanh)."
        : "Gemini không tìm thấy đủ khung. Hãy tự kiểm tra ảnh và xác minh nhãn chuẩn.";
    } catch (error) {
      if (version === loadVersion && image === selectedImage) {
        locateStatus.textContent = "Không thể lấy đề xuất: " +
          String(error.message || error) + " Bạn vẫn có thể nhập thủ công.";
      }
    } finally {
      locateBusy = false;
      // Mỗi lần gửi cần tích xác nhận lại, kể cả khi mô hình báo lỗi.
      locateConsent.checked = false;
      updateLocateReady();
    }
  });

  truthAbsent.addEventListener("change", () => {
    fields.forEach(field => { field.disabled = truthAbsent.checked; });
    reviewed.checked = false;
    previewBox = null;
    redraw();
  });
  fields.forEach(field => field.addEventListener("input", () => {
    reviewed.checked = false;
    previewBox = null;
    redraw();
  }));

  canvas.addEventListener("pointerdown", event => {
    if (!image || truthAbsent.checked) return;
    dragStart = core.scaledPoint(event.clientX, event.clientY, canvas.getBoundingClientRect());
    canvas.setPointerCapture(event.pointerId);
  });
  canvas.addEventListener("pointermove", event => {
    if (!dragStart || !image) return;
    const end = core.scaledPoint(event.clientX, event.clientY, canvas.getBoundingClientRect());
    try { previewBox = core.normalizeDrag(dragStart, end); }
    catch { previewBox = null; }
    redraw();
  });
  canvas.addEventListener("pointerup", event => {
    if (!dragStart) return;
    const end = core.scaledPoint(event.clientX, event.clientY, canvas.getBoundingClientRect());
    try {
      setCoordinates(core.normalizeDrag(dragStart, end));
      reviewed.checked = false;
      notify("Đã khoanh khung. Vui lòng kiểm tra lại tọa độ và tích xác minh.");
    } catch {
      notify("Khung quá nhỏ. Hãy kéo một vùng đủ lớn hoặc nhập tọa độ bằng bàn phím.");
    }
    dragStart = null;
    previewBox = null;
    redraw();
  });
  canvas.addEventListener("pointercancel", () => {
    dragStart = null;
    previewBox = null;
    redraw();
  });

  el("addSample").addEventListener("click", () => {
    try {
      if (!image) throw new Error("Bạn cần chọn và mở ảnh trước khi thêm mẫu.");
      if (!["found", "absent"].includes(predictionMode.value)) {
        throw new Error("Hãy cho biết AI đã tìm thấy khung hay không.");
      }
      const predicted = predictionMode.value === "absent"
        ? null : core.parsePrediction(predictionBox.value);
      const truth = truthAbsent.checked ? null : coordinates();
      if (!reviewed.checked) throw new Error("Hãy xác minh thủ công nhãn chuẩn của ảnh.");
      if (records.length >= core.MaximumReviewedSamples) {
        throw new Error("Đã đạt giới hạn 2000 mẫu; hãy tải JSON về máy và mở bộ dữ liệu riêng.");
      }
      let id;
      do { id = `mau-${String(++serial).padStart(4, "0")}`; }
      while (records.some(item => item.id === id));
      records.push(core.record(id, truth, predicted, true));
      resetCurrent();
      refreshSamples();
      notify(`Đã thêm ${id}. Chọn ảnh tiếp theo hoặc tải tệp JSON.`);
    } catch (error) { notify(error.message || "Không thể thêm mẫu."); }
  });

  importButton.addEventListener("click", async () => {
    if (importing) return;
    const file = importFile.files && importFile.files[0];
    if (!file || !/\.json$/i.test(file.name) || file.size < 2 || file.size > 1048576) {
      importStatus.textContent = "Hãy chọn tệp .json đã xuất, dung lượng từ 2 byte đến 1 MB.";
      return;
    }
    const mode = importMode.value;
    if (!["append", "replace"].includes(mode)) {
      importStatus.textContent = "Chế độ nhập không hợp lệ.";
      return;
    }
    if (mode === "replace" && records.length &&
        !window.confirm("Thay thế TOÀN BỘ mẫu hiện có trong trang bằng bộ dữ liệu đã chọn? Hãy tải bản sao JSON trước nếu cần.")) {
      importStatus.textContent = "Đã hủy thao tác thay thế; dữ liệu hiện tại không đổi.";
      return;
    }
    importing = true;
    importButton.disabled = true;
    importStatus.textContent = "Đang kiểm tra dữ liệu JSON cục bộ…";
    try {
      const text = await file.text();
      // Không ghi vào records cho đến khi toàn bộ tệp đã qua xác thực.
      const proposed = core.importReviewedData(JSON.parse(text), records, mode);
      records = proposed;
      // Không cấp lại mã mau-0001 nếu vừa khôi phục một tệp có mã tương tự.
      serial = Math.max(serial, ...records.map(item => {
        const match = /^mau-(\d+)$/.exec(item.id);
        return match ? Number(match[1]) : 0;
      }));
      refreshSamples();
      importStatus.textContent = `Đã ${mode === "replace" ? "thay thế bằng" : "nhập thêm"} ${proposed.length} mẫu tổng cộng. Các nhãn trước đó vẫn cần được tin cậy theo nguồn bạn đã tự kiểm tra.`;
    } catch (error) {
      importStatus.textContent = "Không nhập dữ liệu: " + String(error.message || error) +
        " Danh sách hiện tại không thay đổi.";
    } finally {
      importFile.value = "";
      importing = false;
      importButton.disabled = false;
    }
  });

  exportButton.addEventListener("click", () => {
    if (!records.length) return;
    const url = URL.createObjectURL(new Blob(
      [JSON.stringify(records, null, 2)], { type: "application/json;charset=utf-8" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = "minimap-evaluation-reviewed.json";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 5000);
    notify("Đã tạo tệp JSON chứa tọa độ; không kèm ảnh hay tên file.");
  });

  resetButton.addEventListener("click", () => {
    if (!records.length || !window.confirm("Xóa tất cả mẫu chưa xuất trong trang này?")) return;
    records = [];
    serial = 0;
    refreshSamples();
    notify("Đã xóa danh sách mẫu trong trang; không có ảnh nào được tải lên.");
  });
  window.addEventListener("pagehide", releaseImage);
  refreshSamples();
})();
