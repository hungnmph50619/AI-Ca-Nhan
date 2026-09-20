/* So sánh A/B trên đúng cùng mã mẫu và nhãn chuẩn đã xác minh.
 * Tính toán hoàn toàn trong trình duyệt, không mở ảnh hay gọi API.
 */
(function (root) {
  "use strict";

  const review = root.MinimapReviewCore ||
    (typeof module !== "undefined" ? require("./vision-review-core.js") : null);

  function comparePaired(a, b, threshold = 0.5) {
    if (!Number.isFinite(threshold) || threshold <= 0 || threshold > 1) {
      throw new Error("Ngưỡng IoU phải lớn hơn 0 và không vượt quá 1.");
    }
    // importReviewedData kiểm tra toàn bộ schema, ID và tọa độ trước khi tính.
    const checkedA = review.importReviewedData(a, [], "replace");
    const checkedB = review.importReviewedData(b, [], "replace");
    if (checkedA.length !== checkedB.length) {
      throw new Error("Hai bộ JSON không có cùng số mẫu; cần đúng cùng ảnh và nhãn chuẩn.");
    }
    const indexB = new Map(checkedB.map(item => [item.id, item]));
    for (const item of checkedA) {
      const other = indexB.get(item.id);
      if (!other) {
        throw new Error("Thiếu mã mẫu " + item.id + " trong bộ B; không thể ghép cặp.");
      }
      if (JSON.stringify(item.truth_box) !== JSON.stringify(other.truth_box)) {
        throw new Error("Nhãn chuẩn khác nhau ở mẫu " + item.id + "; không so sánh hai tập lệch nhãn.");
      }
    }

    const ids = checkedA.map(item => item.id).sort();
    const indexA = new Map(checkedA.map(item => [item.id, item]));
    const resultA = review.evaluateRecords(ids.map(id => indexA.get(id)), threshold);
    const resultB = review.evaluateRecords(ids.map(id => indexB.get(id)), threshold);
    const rows = resultA.details.map((entry, i) => {
      const id = entry.id;
      const aBox = indexA.get(id).predicted_box;
      const bBox = indexB.get(id).predicted_box;
      return {
        id,
        prediction_changed: JSON.stringify(aBox) !== JSON.stringify(bBox),
        a_outcome: entry.outcome,
        b_outcome: resultB.details[i].outcome,
        a_iou: entry.iou,
        b_iou: resultB.details[i].iou
      };
    });
    const summarize = report => ({
      samples: report.samples,
      tp: report.tp,
      fp: report.fp,
      fn: report.fn,
      tn: report.tn,
      precision: report.precision,
      recall: report.recall,
      f1: report.f1
    });
    return {
      samples: ids.length,
      minimum_iou: threshold,
      changed_predictions: rows.filter(item => item.prediction_changed).length,
      a: summarize(resultA),
      b: summarize(resultB),
      per_sample: rows
    };
  }

  const api = {comparePaired};
  root.MinimapCompareCore = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
