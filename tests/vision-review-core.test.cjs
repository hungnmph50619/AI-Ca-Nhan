"use strict";
const assert = require("node:assert/strict");
const test = require("node:test");
const core = require("../src/PersonalAI.Web/wwwroot/vision-review-core.js");

test("Khung hợp lệ, không biến đổi dữ liệu gốc", () => {
  const value = [10, 20, 900, 800];
  assert.deepEqual(core.box(value), value);
  assert.notStrictEqual(core.box(value), value);
  assert.deepEqual(core.parsePrediction(" [10,20,900,800] "), value);
  assert.deepEqual(core.parsePrediction('{"found":true,"normalizedBox":[10,20,900,800]}'), value);
});

test("Không chấp nhận kết quả AI sai hoặc không có khung", () => {
  for (const input of ["", "null", "not json", "[1,2,3]", "[1.5,2,3,4]",
    "[0,0,1001,200]", "[500,500,100,600]", '{"found":false,"normalizedBox":null}']) {
    assert.throws(() => core.parsePrediction(input));
  }
});

test("Chuyển tọa độ kéo khung từ vùng canvas đang hiển thị", () => {
  const bounds = { left: 100, top: 200, width: 500, height: 250 };
  assert.deepEqual(core.scaledPoint(100, 200, bounds), [0, 0]);
  assert.deepEqual(core.scaledPoint(350, 325, bounds), [500, 500]);
  assert.deepEqual(core.scaledPoint(700, 0, bounds), [1000, 0]);
  assert.deepEqual(core.normalizeDrag([800, 700], [100, 200]), [100, 200, 800, 700]);
  assert.throws(() => core.normalizeDrag([200, 200], [200, 200]));
});

test("Không xuất nhãn chưa được con người xác minh", () => {
  assert.throws(() => core.record("mau-001", [0, 0, 400, 400], null, false));
  assert.throws(() => core.record("", null, null, true));
  assert.deepEqual(core.record("mau-001", [0, 0, 400, 400], null, true), {
    id: "mau-001", reviewed: true, truth_box: [0, 0, 400, 400], predicted_box: null
  });
  assert.deepEqual(core.record("mau-002", null, null, true), {
    id: "mau-002", reviewed: true, truth_box: null, predicted_box: null
  });
});


test("Đánh giá trùng khớp với quy tắc script Python v2.3.1", () => {
  const data = [
    core.record("tp", [100, 100, 400, 400], [100, 100, 400, 400], true),
    core.record("tn", null, null, true),
    core.record("fp", null, [10, 10, 200, 200], true),
    core.record("fn", [10, 10, 200, 200], null, true),
    core.record("mismatch", [0, 0, 200, 200], [800, 800, 1000, 1000], true)
  ];
  const result = core.evaluateRecords(data);
  assert.deepEqual([result.samples, result.tp, result.tn, result.fp, result.fn], [5, 1, 1, 2, 2]);
  assert.equal(result.precision, 1/3);
  assert.equal(result.recall, 1/3);
  assert.equal(result.f1, 1/3);
  assert.equal(result.details.at(-1).outcome, "mismatched_box");
  assert.equal(result.details.at(-1).iou, 0);
});

test("Ngưỡng IoU ảnh hưởng phân loại và không tự xác nhận dự đoán", () => {
  const data = [core.record("overlap", [0, 0, 200, 200], [100, 0, 300, 200], true)];
  assert.ok(Math.abs(core.overlapScore(data[0].truth_box, data[0].predicted_box) - 1/3) < 1e-10);
  assert.equal(core.evaluateRecords(data, 0.3).tp, 1);
  const strict = core.evaluateRecords(data, 0.5);
  assert.deepEqual([strict.tp, strict.fp, strict.fn], [0, 1, 1]);
  assert.equal(strict.details[0].outcome, "mismatched_box");
});

test("Không dùng giá trị 0% giả cho chỉ số thiếu mẫu dương", () => {
  const allNegative = core.evaluateRecords([core.record("empty", null, null, true)]);
  assert.equal(allNegative.precision, null);
  assert.equal(allNegative.recall, null);
  assert.equal(allNegative.f1, null);
});

test("Từ chối dữ liệu chưa kiểm tra, ID trùng, ngưỡng không hợp lệ", () => {
  const valid = core.record("a", null, null, true);
  for (const threshold of [0, -1, 1.1, NaN, Infinity, "0.5"]) {
    assert.throws(() => core.evaluateRecords([valid], threshold));
  }
  assert.throws(() => core.evaluateRecords([]));
  assert.throws(() => core.evaluateRecords([{...valid, reviewed: false}]));
  assert.throws(() => core.evaluateRecords([valid, valid]));
  assert.throws(() => core.evaluateRecords([{...valid, truth_box: [1, 1, 0, 0]}]));
});
