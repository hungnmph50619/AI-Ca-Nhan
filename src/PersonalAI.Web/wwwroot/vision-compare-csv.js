/* Xuất số liệu so sánh minimap thành CSV để kiểm tra bằng bảng tính.
 * Không xuất ảnh, khung tọa độ thô, tên file hoặc thông tin Gemini.
 */
(function (root) {
  "use strict";

  const COLUMNS = Object.freeze([
    "sample_id", "minimum_iou", "transition", "prediction_changed",
    "a_outcome", "b_outcome", "a_iou", "b_iou"
  ]);

  function cell(value) {
    if (value === null || value === undefined) return "";
    let text = String(value);
    // Phòng công thức trong Excel/LibreOffice, kể cả khi schema ID
    // về sau cho phép ký tự khác chữ/số ASCII.
    if (/^[\s\u0000-\u001f]*[=+\-@]/u.test(text)) text = "'" + text;
    return '"' + text.replace(/"/g, '""') + '"';
  }

  function formatCsv(report) {
    if (!report || !Number.isFinite(report.minimum_iou) ||
        !Array.isArray(report.per_sample) || report.per_sample.length < 1 ||
        report.per_sample.length > 2000) {
      throw new Error("Chưa có báo cáo so sánh hợp lệ để xuất CSV.");
    }
    const lines = [COLUMNS.join(",")];
    for (const item of report.per_sample) {
      if (!item || typeof item.id !== "string" ||
          !/^[A-Za-z0-9_-]{1,64}$/.test(item.id)) {
        throw new Error("Mã mẫu trong báo cáo CSV không hợp lệ.");
      }
      const row = [
        item.id, report.minimum_iou, item.transition,
        item.prediction_changed ? "true" : "false",
        item.a_outcome, item.b_outcome, item.a_iou, item.b_iou
      ];
      lines.push(row.map(cell).join(","));
    }
    // BOM UTF-8 giúp Excel trên Windows nhận dạng đúng tiếng Việt nếu có.
    return "\uFEFF" + lines.join("\r\n") + "\r\n";
  }

  const api = {formatCsv};
  root.MinimapCompareCsv = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
