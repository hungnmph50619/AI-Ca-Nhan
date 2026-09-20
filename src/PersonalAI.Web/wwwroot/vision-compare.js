(() => {
  "use strict";
  const core = globalThis.MinimapCompareCore;
  const el = id => document.getElementById(id);
  const inputA = el("comparisonA");
  const inputB = el("comparisonB");
  const threshold = el("comparisonThreshold");
  const button = el("runComparison");
  const status = el("comparisonStatus");
  const summary = el("comparisonSummary");
  const metrics = el("comparisonMetrics");
  const sampleSelect = el("comparisonSample");
  const details = el("comparisonDetails");
  let busy = false;
  let selectionVersion = 0;
  let lastResult = null;

  function clearResults() {
    lastResult = null;
    summary.textContent = "Chưa có kết quả cho lựa chọn hiện tại.";
    metrics.textContent = "";
    details.textContent = "Chưa có mẫu để xem.";
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
  function displaySample() {
    if (!lastResult) return;
    const item = lastResult.per_sample.find(entry => entry.id === sampleSelect.value);
    if (!item) {
      details.textContent = "Chưa chọn mẫu để xem.";
      return;
    }
    const iou = value => value === null ? "không áp dụng" : value.toFixed(3);
    details.textContent = "Mẫu " + item.id +
      ". Dự đoán có thay đổi: " + (item.prediction_changed ? "có" : "không") +
      ". A: " + item.a_outcome + " (IoU " + iou(item.a_iou) + ")" +
      ". B: " + item.b_outcome + " (IoU " + iou(item.b_iou) + ").";
  }
  sampleSelect.addEventListener("change", displaySample);
  button.addEventListener("click", async () => {
    if (busy) return;
    const a = inputA.files && inputA.files[0];
    const b = inputB.files && inputB.files[0];
    const currentVersion = selectionVersion;
    const minIou = Number(threshold.value);
    busy = true;
    button.disabled = true;
    clearResults();
    status.textContent = "Đang đọc và kiểm tra hai tệp JSON cục bộ…";
    try {
      if (a && b && a === b) throw new Error("Cần chọn hai tệp khác nhau.");
      // Kiểm tra định dạng và dung lượng trước khi đọc một byte nào.
      if (!a || !b) throw new Error("Hãy chọn cả bộ A và B.");
      for (const [file, label] of [[a, "A"], [b, "B"]]) {
        if (!/\\.json$/i.test(file.name) || file.size < 2 || file.size > 1024 * 1024) {
          throw new Error("Bộ " + label + ": chọn tệp .json từ 2 byte đến 1 MB.");
        }
      }
      const texts = await Promise.all([readSelected(a, "A"), readSelected(b, "B")]);
      if (currentVersion !== selectionVersion ||
          (inputA.files && inputA.files[0]) !== a ||
          (inputB.files && inputB.files[0]) !== b) {
        throw new Error("Tệp hoặc ngưỡng đã thay đổi trong khi đọc. Hãy so sánh lại.");
      }
      const result = core.comparePaired(JSON.parse(texts[0]), JSON.parse(texts[1]), minIou);
      lastResult = result;
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
      for (const item of result.per_sample) {
        const option = document.createElement("option");
        option.value = item.id;
        option.textContent = item.id + (item.prediction_changed ? " · khác dự đoán" : "");
        sampleSelect.appendChild(option);
      }
      sampleSelect.value = result.per_sample[0].id;
      sampleSelect.disabled = false;
      displaySample();
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
