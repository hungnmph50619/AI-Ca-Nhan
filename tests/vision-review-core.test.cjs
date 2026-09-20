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
