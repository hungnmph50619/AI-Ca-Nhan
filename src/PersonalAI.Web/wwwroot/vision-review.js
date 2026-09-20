(() => {
  "use strict";
  const core = globalThis.MinimapReviewCore;
  const el = id => document.getElementById(id);
  const fileInput = el("imageFile");
  const canvas = el("imageCanvas");
  const context = canvas.getContext("2d");
  const predictionMode = el("predictionMode");
  const predictionBox = el("predictionBox");
  const truthAbsent = el("truthAbsent");
  const reviewed = el("reviewed");
  const message = el("message");
  const samples = el("samples");
  const exportButton = el("exportDataset");
  const resetButton = el("resetDataset");
  const fields = ["x1", "y1", "x2", "y2"].map(el);

  let image = null;
  let imageUrl = null;
  let loadVersion = 0;
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
    fileInput.value = "";
    releaseImage();
    predictionMode.value = "";
    predictionBox.value = "";
    predictionBox.disabled = true;
    truthAbsent.checked = false;
    reviewed.checked = false;
    fields.forEach(field => { field.value = ""; field.disabled = false; });
  }
  function refreshSamples() {
    samples.textContent = records.length
      ? `Đã xác minh ${records.length} mẫu: ${records.map(item => item.id).join(", ")}.`
      : "Chưa có mẫu nào.";
    exportButton.disabled = !records.length;
    resetButton.disabled = !records.length;
  }

  fileInput.addEventListener("change", () => {
    const version = ++loadVersion;
    releaseImage();
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
    predictionBox.disabled = predictionMode.value !== "found";
    reviewed.checked = false;
    redraw();
  });
  predictionBox.addEventListener("input", () => { reviewed.checked = false; redraw(); });
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
      const id = `mau-${String(++serial).padStart(4, "0")}`;
      records.push(core.record(id, truth, predicted, true));
      resetCurrent();
      refreshSamples();
      notify(`Đã thêm ${id}. Chọn ảnh tiếp theo hoặc tải tệp JSON.`);
    } catch (error) { notify(error.message || "Không thể thêm mẫu."); }
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
