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

  const api = { box, parsePrediction, normalizeDrag, scaledPoint, record };
  root.MinimapReviewCore = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
