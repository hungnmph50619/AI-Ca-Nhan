/* Công cụ thuần cho gán nhãn ngoại tuyến: không gọi API và không lưu ảnh. */
(function (root) {
  "use strict";

  function box(value) {
    if (!Array.isArray(value) || value.length !== 4 ||
        !value.every(n => Number.isInteger(n) && n >= 0 && n <= 1000) ||
        value[0] >= value[2] || value[1] >= value[3]) {
      throw new Error("Khung phải gồm [x1,y1,x2,y2] nguyên 0–1000 với góc hợp lệ.");
    }
    return [...value];
  }

  function parsePrediction(raw) {
    if (typeof raw !== "string" || !raw.trim()) {
      throw new Error("Cần nhập tọa độ AI đề xuất hoặc chọn 'AI không tìm thấy'.");
    }
    let parsed;
    try { parsed = JSON.parse(raw.trim()); }
    catch { throw new Error("Tọa độ AI phải là JSON hợp lệ."); }
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      if (parsed.found === false) {
        throw new Error("AI báo không tìm thấy khung; chọn 'AI không tìm thấy'.");
      }
      parsed = parsed.normalizedBox;
    }
    return box(parsed);
  }

  function normalizeDrag(start, end) {
    const coords = [
      Math.min(start[0], end[0]), Math.min(start[1], end[1]),
      Math.max(start[0], end[0]), Math.max(start[1], end[1])
    ];
    return box(coords);
  }

  function scaledPoint(clientX, clientY, bounds) {
    if (bounds.width <= 0 || bounds.height <= 0) throw new Error("Khung ảnh không hợp lệ.");
    return [
      Math.max(0, Math.min(1000, Math.round((clientX - bounds.left) * 1000 / bounds.width))),
      Math.max(0, Math.min(1000, Math.round((clientY - bounds.top) * 1000 / bounds.height)))
    ];
  }

  function record(id, truth, predicted, reviewed) {
    if (typeof id !== "string" || !id.trim() || reviewed !== true) {
      throw new Error("Phải kiểm tra nhãn thủ công trước khi thêm mẫu.");
    }
    return {
      id,
      reviewed: true,
      truth_box: truth === null ? null : box(truth),
      predicted_box: predicted === null ? null : box(predicted)
    };
  }

  function overlapScore(truth, predicted) {
    const left = Math.max(truth[0], predicted[0]);
    const top = Math.max(truth[1], predicted[1]);
    const right = Math.min(truth[2], predicted[2]);
    const bottom = Math.min(truth[3], predicted[3]);
    const intersection = Math.max(0, right - left) * Math.max(0, bottom - top);
    const truthArea = (truth[2] - truth[0]) * (truth[3] - truth[1]);
    const predictedArea = (predicted[2] - predicted[0]) * (predicted[3] - predicted[1]);
    return intersection / (truthArea + predictedArea - intersection);
  }

  function evaluateRecords(items, threshold = 0.5) {
    if (!Array.isArray(items) || items.length === 0) {
      throw new Error("Chưa có mẫu gán nhãn để đánh giá.");
    }
    if (typeof threshold !== "number" || !Number.isFinite(threshold) ||
        threshold <= 0 || threshold > 1) {
      throw new Error("Ngưỡng IoU phải lớn hơn 0 và không vượt quá 1.");
    }
    const seen = new Set();
    let tp = 0, fp = 0, fn = 0, tn = 0;
    const details = [];
    for (const item of items) {
      if (!item || typeof item !== "object" || Array.isArray(item) ||
          typeof item.id !== "string" || !item.id.trim() || seen.has(item.id) ||
          item.reviewed !== true ||
          !Object.prototype.hasOwnProperty.call(item, "truth_box") ||
          !Object.prototype.hasOwnProperty.call(item, "predicted_box")) {
        throw new Error("Mẫu phải có mã riêng, hai khung và nhãn đã xác minh.");
      }
      seen.add(item.id);
      const truth = item.truth_box === null ? null : box(item.truth_box);
      const predicted = item.predicted_box === null ? null : box(item.predicted_box);
      const iou = truth && predicted ? overlapScore(truth, predicted) : null;
      let outcome;
      if (!truth && !predicted) { tn++; outcome = "true_negative"; }
      else if (truth && predicted && iou >= threshold) { tp++; outcome = "true_positive"; }
      else if (!truth) { fp++; outcome = "false_positive"; }
      else if (!predicted) { fn++; outcome = "false_negative"; }
      else { fp++; fn++; outcome = "mismatched_box"; }
      details.push({ id: item.id, iou, outcome });
    }
    const precision = tp + fp ? tp / (tp + fp) : null;
    const recall = tp + fn ? tp / (tp + fn) : null;
    const f1 = precision !== null && recall !== null && precision + recall
      ? 2 * precision * recall / (precision + recall) : null;
    return { samples: items.length, threshold, tp, fp, fn, tn, precision, recall, f1, details };
  }

  // Kết quả Gemini chỉ là tọa độ đề xuất, không trở thành nhãn chuẩn.
  function normalizeLocateResponse(data, width, height) {
    if (!data || typeof data !== "object" || Array.isArray(data) ||
        data.provider !== "Gemini" || data.verified !== false || data.needsReview !== true ||
        data.width !== width || data.height !== height) {
      throw new Error("Kết quả AI không phù hợp với ảnh đã chọn hoặc thiếu trạng thái cần xác minh.");
    }
    if (data.found === false && data.normalizedBox === null) {
      return { mode: "absent", coordinates: null };
    }
    if (data.found === true) {
      return { mode: "found", coordinates: box(data.normalizedBox) };
    }
    throw new Error("Dữ liệu tọa độ AI không hợp lệ; chưa cập nhật nhãn.");
  }

  const api = { box, parsePrediction, normalizeDrag, scaledPoint, record,
    overlapScore, evaluateRecords, normalizeLocateResponse };
  root.MinimapReviewCore = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
