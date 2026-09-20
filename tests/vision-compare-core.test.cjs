"use strict";
const assert = require("node:assert/strict");
const test = require("node:test");
const compare = require("../src/PersonalAI.Web/wwwroot/vision-compare-core.js");

function row(id, truth, predicted, reviewed = true) {
  return { id, reviewed, truth_box: truth, predicted_box: predicted };
}

test("Ghép cặp đúng ID, độc lập thứ tự hàng, đo hai bộ trên cùng nhãn chuẩn", () => {
  const a = [
    row("one", [0,0,200,200], null),
    row("two", null, null),
    row("three", [100,100,400,400], [100,100,400,400])
  ];
  const b = [
    row("three", [100,100,400,400], [100,100,400,400]),
    row("one", [0,0,200,200], [0,0,200,200]),
    row("two", null, [50,50,300,300])
  ];
  const result = compare.comparePaired(a, b);
  assert.equal(result.samples, 3);
  assert.equal(result.changed_predictions, 2);
  assert.deepEqual([result.a.tp,result.a.fn,result.a.fp], [1,1,0]);
  assert.deepEqual([result.b.tp,result.b.fn,result.b.fp], [2,0,1]);
  assert.deepEqual(result.per_sample.map(row => row.id), ["one","three","two"]);
  assert.equal(result.per_sample[0].a_outcome,"false_negative");
  assert.equal(result.per_sample[0].b_outcome,"true_positive");
  assert.deepEqual(result.per_sample[0].truth_box,[0,0,200,200]);
  assert.equal(result.per_sample[0].a_box,null);
  assert.deepEqual(result.per_sample[0].b_box,[0,0,200,200]);
  assert.equal(result.per_sample[1].truth_box[0],100);
  assert.equal(result.per_sample[0].a_iou,null);
  assert.equal(result.per_sample[0].b_iou,1);
  assert.deepEqual(result.transitions,{
    corrected:1, regressed:1, both_match:1, both_mismatch:0
  });
  assert.deepEqual(result.per_sample.map(item=>item.transition),
    ["corrected","both_match","regressed"]);
});

test("Tệp giống hệt nhau không có dự đoán thay đổi", () => {
  const items = [row("mau-0001",null,null)];
  const result = compare.comparePaired(items, items);
  assert.equal(result.changed_predictions,0);
  assert.deepEqual(result.a,result.b);
  assert.deepEqual(result.transitions,{
    corrected:0,regressed:0,both_match:1,both_mismatch:0
  });
  assert.equal(result.a.precision,null);
});

test("Không cho phép bộ dữ liệu thiếu mẫu hoặc khác nhãn chuẩn", () => {
  const baseline = [row("one",[100,100,400,400],null)];
  for (const candidate of [
    [],
    [row("two",[100,100,400,400],null)],
    [row("one",[101,100,400,400],null)],
    [row("one",[100,100,400,400],null,false)],
    [row("one",[100,100,400,400],[-1,0,100,100])],
    [row("one",[100,100,400,400],null),row("one",[100,100,400,400],null)],
    [{...row("one",[100,100,400,400],null), extra:"bad"}]
  ]) {
    assert.throws(() => compare.comparePaired(baseline,candidate));
  }
});

test("Ngưỡng IoU phải hợp lệ và ảnh hưởng phân loại như công cụ Python", () => {
  const a = [row("one",[0,0,200,200],[100,0,300,200])];
  assert.equal(compare.comparePaired(a,a,.3).a.tp,1);
  assert.equal(compare.comparePaired(a,a,.5).a.tp,0);
  for(const bad of [0,-1,1.1,NaN,Infinity,"0.5"]){
    assert.throws(()=>compare.comparePaired(a,a,bad));
  }
});


test("Hai dự đoán khác nhau nhưng cùng sai không bị tính là khớp nhãn", () => {
  const truth=[100,100,400,400];
  const a=[row("x",truth,[0,0,50,50])];
  const b=[row("x",truth,[600,600,900,900])];
  const result=compare.comparePaired(a,b);
  assert.deepEqual(result.transitions,{
    corrected:0,regressed:0,both_match:0,both_mismatch:1
  });
  assert.equal(result.changed_predictions,1);
  assert.equal(result.per_sample[0].transition,"both_mismatch");
});
