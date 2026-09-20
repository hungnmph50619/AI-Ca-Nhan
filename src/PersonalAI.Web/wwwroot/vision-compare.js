(() => {
  "use strict";
  const core = globalThis.MinimapCompareCore;
  const el = id => document.getElementById(id);
  const inputA = el("comparisonA");
  const inputB = el("comparisonB");
  const threshold = el("comparisonThreshold");
  const button = el("runComparison");
  const status = el("comparisonStatus");
  const exportButton = el("exportComparison");
  const exportStatus = el("exportStatus");
  const summary = el("comparisonSummary");
  const metrics = el("comparisonMetrics");
  const sensitivity = el("comparisonSensitivity");
  const transitions = el("comparisonTransitions");
  const filterSelect = el("comparisonFilter");
  const sampleSelect = el("comparisonSample");
  const details = el("comparisonDetails");
  const overlay = el("comparisonOverlay");
  const overlayText = el("overlayText");
  const visibility = el("overlayVisibility");
  const showTruth = el("overlayShowTruth");
  const showA = el("overlayShowA");
  const showB = el("overlayShowB");
  const overlayContext = overlay.getContext("2d");
  let busy = false;
  let selectionVersion = 0;
  let lastResult = null;
  let lastSweep = null;

  function clearResults() {
    lastResult = null;
    lastSweep = null;
    sensitivity.textContent = "Chưa có kết quả theo ngưỡng.";
    exportButton.disabled = true;
    exportStatus.textContent = "Chưa có báo cáo để lưu.";
    summary.textContent = "Chưa có kết quả cho lựa chọn hiện tại.";
    metrics.textContent = "";
    transitions.textContent = "Chưa có số liệu chuyển trạng thái.";
    filterSelect.value = "all";
    filterSelect.disabled = true;
    details.textContent = "Chưa có mẫu để xem.";
    overlay.hidden = true;
    overlayText.textContent = "Chưa có sơ đồ tọa độ.";
    visibility.disabled = true;
    sampleSelect.replaceChildren();
    sampleSelect.disabled = true;
  }
  for (const field of [inputA, inputB, threshold]) {
    field.addEventListener("change", () => {
      selectionVersion++;
      clearResults();
      status.textContent = "Lựa chọn đã thay đổi. Bấm So sánh để tính lại.";
    });
  }
  function readSelected(file, label) {
    if (!file || !/\.json$/i.test(file.name) ||
        file.size < 2 || file.size > 1024 * 1024) {
      throw new Error("Bộ " + label + ": chọn tệp .json từ 2 byte đến 1 MB.");
    }
    return file.text();
  }
  function drawOverlay(item) {
    const ctx = overlayContext;
    if (!ctx) {
      overlay.hidden = true;
      overlayText.textContent = "Trình duyệt không hỗ trợ sơ đồ Canvas. Có thể xem tọa độ bằng chữ.";
      return;
    }
    overlay.hidden = false;
    const padding = 20;
    const span = overlay.width - 2 * padding;
    ctx.clearRect(0, 0, overlay.width, overlay.height);
    ctx.fillStyle = "#0b1521";
    ctx.fillRect(0, 0, overlay.width, overlay.height);
    ctx.save();
    ctx.strokeStyle = "#748ca5";
    ctx.lineWidth = 1;
    ctx.setLineDash([]);
    ctx.strokeRect(padding, padding, span, span);
    ctx.restore();
    const segments = [
      ["Nhãn chuẩn", item.truth_box, "#5df2cf", [], showTruth.checked],
      ["Dự đoán A", item.a_box, "#f7c36b", [9, 4], showA.checked],
      ["Dự đoán B", item.b_box, "#f2a2df", [2, 5], showB.checked]
    ];
    for (const [, coords, color, dashes, enabled] of segments) {
      if (!enabled || coords === null) continue;
      ctx.save();
      ctx.strokeStyle = color;
      ctx.lineWidth = 3;
      ctx.setLineDash(dashes);
      ctx.strokeRect(
        padding + coords[0] * span / 1000,
        padding + coords[1] * span / 1000,
        (coords[2] - coords[0]) * span / 1000,
        (coords[3] - coords[1]) * span / 1000
      );
      ctx.restore();
    }
    overlayText.textContent = "Sơ đồ của mẫu " + item.id + ". " +
      segments.map(([name, box, , , enabled]) =>
        name + ": " + (box === null ? "không có khung" : box.join(", ")) +
        (enabled ? " (đang hiện)" : " (đang ẩn)")).join(". ") +
      ". Tọa độ chuẩn hóa 0–1000; sơ đồ không hiển thị ảnh gốc.";
  }
  function displaySample() {
    if (!lastResult) return;
    const item = lastResult.per_sample.find(entry => entry.id === sampleSelect.value);
    if (!item) {
      details.textContent = "Chưa chọn mẫu để xem.";
      overlay.hidden = true;
      overlayText.textContent = "Chưa có sơ đồ tọa độ.";
      return;
    }
    const iou = value => value === null ? "không áp dụng" : value.toFixed(3);
    details.textContent = "Mẫu " + item.id +
      ". Dự đoán có thay đổi: " + (item.prediction_changed ? "có" : "không") +
      ". A: " + item.a_outcome + " (IoU " + iou(item.a_iou) + ")" +
      ". B: " + item.b_outcome + " (IoU " + iou(item.b_iou) + ").";
    drawOverlay(item);
  }
  sampleSelect.addEventListener("change", displaySample);
  for (const control of [showTruth, showA, showB]) {
    control.addEventListener("change", () => {
      if (lastResult && !sampleSelect.disabled) displaySample();
    });
  }
  function refreshFilteredSamples() {
    const selected = sampleSelect.value;
    sampleSelect.replaceChildren();
    if (!lastResult) {
      sampleSelect.disabled = true;
      details.textContent = "Chưa có mẫu để xem.";
      overlay.hidden = true;
      overlayText.textContent = "Chưa có sơ đồ tọa độ.";
      return;
    }
    const filter = filterSelect.value;
    const rows = lastResult.per_sample.filter(item =>
      filter === "all" ||
      (filter === "changed" ? item.prediction_changed : item.transition === filter));
    for (const item of rows) {
      const option = document.createElement("option");
      option.value = item.id;
      option.textContent = item.id + (item.prediction_changed ? " · khác dự đoán" : "");
      sampleSelect.appendChild(option);
    }
    sampleSelect.disabled = rows.length === 0;
    if (!rows.length) {
      details.textContent = "Không có mẫu nào thuộc nhóm đã chọn.";
      overlay.hidden = true;
      overlayText.textContent = "Không có sơ đồ trong nhóm đã chọn.";
      return;
    }
    sampleSelect.value = rows.some(item => item.id === selected) ? selected : rows[0].id;
    displaySample();
  }
  filterSelect.addEventListener("change", refreshFilteredSamples);
  exportButton.addEventListener("click", () => {
    if (!lastResult || busy || exportButton.disabled) return;
    // Chỉ xuất số đếm và kết quả theo mã mẫu, không xuất ảnh, tên tệp
    // nguồn, khóa API hoặc trạng thái đồng ý gửi ảnh tới Gemini.
    const report = {
      schema: "minimap-paired-comparison-v1",
      samples: lastResult.samples,
      minimum_iou: lastResult.minimum_iou,
      changed_predictions: lastResult.changed_predictions,
      transitions: lastResult.transitions,
      a: lastResult.a,
      b: lastResult.b,
      // Danh sách cho phép: sơ đồ có tọa độ chỉ ở bộ nhớ trình duyệt,
      // không đưa truth_box / a_box / b_box vào báo cáo thống kê.
      // Cùng hai tệp và cùng nhãn chuẩn; chỉ gồm các số đếm và chỉ số.
      threshold_sensitivity: lastSweep,
      per_sample: lastResult.per_sample.map(item => ({
        id: item.id,
        transition: item.transition,
        prediction_changed: item.prediction_changed,
        a_outcome: item.a_outcome,
        b_outcome: item.b_outcome,
        a_iou: item.a_iou,
        b_iou: item.b_iou
      })),
      note: "Báo cáo tọa độ trên cùng mã mẫu và nhãn chuẩn, không chứng minh hiệu quả thực tế."
    };
    const blob = new Blob([JSON.stringify(report, null, 2)], {
      type: "application/json;charset=utf-8"
    });
    const url = URL.createObjectURL(blob);
    try {
      const link = document.createElement("a");
      link.href = url;
      link.download = "minimap-a-b-comparison.json";
      document.body.appendChild(link);
      link.click();
      link.remove();
      exportStatus.textContent = "Đã tạo báo cáo JSON chỉ chứa số đếm và mã mẫu; không kèm ảnh hoặc tên tệp nguồn.";
    } finally {
      setTimeout(() => URL.revokeObjectURL(url), 5000);
    }
  });
  button.addEventListener("click", async () => {
    if (busy) return;
    const a = inputA.files && inputA.files[0];
    const b = inputB.files && inputB.files[0];
    const currentVersion = selectionVersion;
    const minIou = Number(threshold.value);
    busy = true;
    button.disabled = true;
    exportButton.disabled = true;
    clearResults();
    status.textContent = "Đang đọc và kiểm tra hai tệp JSON cục bộ…";
    try {
      if (a && b && a === b) throw new Error("Cần chọn hai tệp khác nhau.");
      // Kiểm tra định dạng và dung lượng trước khi đọc một byte nào.
      if (!a || !b) throw new Error("Hãy chọn cả bộ A và B.");
      for (const [file, label] of [[a, "A"], [b, "B"]]) {
        if (!/\.json$/i.test(file.name) || file.size < 2 || file.size > 1024 * 1024) {
          throw new Error("Bộ " + label + ": chọn tệp .json từ 2 byte đến 1 MB.");
        }
      }
      const texts = await Promise.all([readSelected(a, "A"), readSelected(b, "B")]);
      if (currentVersion !== selectionVersion ||
          (inputA.files && inputA.files[0]) !== a ||
          (inputB.files && inputB.files[0]) !== b) {
        throw new Error("Tệp hoặc ngưỡng đã thay đổi trong khi đọc. Hãy so sánh lại.");
      }
      const dataA = JSON.parse(texts[0]);
      const dataB = JSON.parse(texts[1]);
      const result = core.comparePaired(dataA, dataB, minIou);
      // Không lấy dữ liệu mới và không thay đổi cấu hình: đánh giá lại
      // đúng các dự đoán A/B đang so sánh theo ba ngưỡng minh bạch.
      const sweep = [0.5, 0.75, 0.9].map(value => {
        const measured = value === minIou ? result : core.comparePaired(dataA, dataB, value);
        return {
          minimum_iou: value,
          samples: measured.samples,
          a: measured.a,
          b: measured.b,
          transitions: measured.transitions
        };
      });
      lastResult = result;
      lastSweep = sweep;
      const sweepPercent = value => value === null ? "không xác định" :
        (value * 100).toFixed(1) + "%";
      sensitivity.textContent = sweep.map(entry =>
        "IoU " + entry.minimum_iou.toFixed(2) +
        " — A: TP " + entry.a.tp + ", FP " + entry.a.fp +
        ", FN " + entry.a.fn + ", F1 " + sweepPercent(entry.a.f1) +
        " | B: TP " + entry.b.tp + ", FP " + entry.b.fp +
        ", FN " + entry.b.fn + ", F1 " + sweepPercent(entry.b.f1) +
        " | A sai/B khớp " + entry.transitions.corrected +
        ", A khớp/B sai " + entry.transitions.regressed
      ).join("\n");
      exportButton.disabled = false;
      summary.textContent = "Đã đối chiếu " + result.samples +
        " mã mẫu có cùng nhãn chuẩn; dự đoán thay đổi ở " +
        result.changed_predictions + " mẫu. Ngưỡng IoU: " + minIou.toFixed(2) + ".";
      const percent = n => n === null ? "không xác định" : (n * 100).toFixed(1) + "%";
      const describe = (label, report) =>
        label + " — TP: " + report.tp + "; FP: " + report.fp +
        "; FN: " + report.fn + "; TN: " + report.tn +
        "; precision: " + percent(report.precision) +
        "; recall: " + percent(report.recall) +
        "; F1: " + percent(report.f1) + ". ";
      metrics.textContent = describe("Bộ A", result.a) + describe("Bộ B", result.b);
      transitions.textContent =
        "A sai → B khớp nhãn chuẩn: " + result.transitions.corrected +
        "; A khớp → B sai: " + result.transitions.regressed +
        "; cả hai khớp: " + result.transitions.both_match +
        "; cả hai sai: " + result.transitions.both_mismatch + ".";
      filterSelect.disabled = false;
      visibility.disabled = false;
      refreshFilteredSamples();
      status.textContent = "Đã so sánh cục bộ. Không có tệp nào được gửi lên máy chủ.";
    } catch (error) {
      clearResults();
      status.textContent = "Không thể so sánh: " + String(error.message || error);
    } finally {
      busy = false;
      button.disabled = false;
    }
  });
  clearResults();
})();
