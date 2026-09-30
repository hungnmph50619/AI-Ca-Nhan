"use strict";
const assert = require("node:assert/strict");
const test = require("node:test");
const csv = require("../src/PersonalAI.Web/wwwroot/vision-compare-csv.js");
const compare = require("../src/PersonalAI.Web/wwwroot/vision-compare-core.js");

const sample = (id, truth, predicted) =>
  ({id, reviewed:true, truth_box:truth, predicted_box:predicted});

test("CSV có BOM UTF-8, tiêu đề cố định và hàng đúng thứ tự ID", () => {
  const a=[sample("mau-2",null,null),sample("mau-1",[0,0,200,200],null)];
  const b=[sample("mau-1",[0,0,200,200],[0,0,200,200]),sample("mau-2",null,null)];
  const report=compare.comparePaired(a,b,0.5);
  const text=csv.formatCsv(report);
  assert.ok(text.startsWith("\uFEFFsample_id,minimum_iou,transition,"));
  assert.ok(text.endsWith("\r\n"));
  const lines=text.slice(1).trimEnd().split("\r\n");
  assert.equal(lines.length,3);
  assert.equal(lines[0],
    "sample_id,minimum_iou,transition,prediction_changed,a_outcome,b_outcome,a_iou,b_iou");
  assert.equal(lines[1],
    '"mau-1","0.5","corrected","true","false_negative","true_positive",,"1"');
  assert.equal(lines[2],
    '"mau-2","0.5","both_match","false","true_negative","true_negative",,');
  assert.doesNotMatch(text,/truth_box|predicted_box|a_box|b_box|Gemini|api.key/i);
});

test("CSV không ghi tọa độ thô ngay cả khi báo cáo nội bộ có thêm các trường khung", () => {
  const report=compare.comparePaired(
    [sample("one",[123,234,345,456],null)],
    [sample("one",[123,234,345,456],[123,234,345,456])]
  );
  const text=csv.formatCsv(report);
  assert.doesNotMatch(text,/123|234|345|456|truth_box|a_box|b_box/);
  assert.match(text,/corrected/);
});

test("Không xuất dữ liệu rỗng, mã mẫu sai hoặc vượt giới hạn", () => {
  const ok={minimum_iou:0.5,per_sample:[{
    id:"one",transition:"both_match",prediction_changed:false,
    a_outcome:"true_negative",b_outcome:"true_negative",a_iou:null,b_iou:null
  }]};
  assert.throws(()=>csv.formatCsv(null));
  assert.throws(()=>csv.formatCsv({...ok,per_sample:[]}));
  assert.throws(()=>csv.formatCsv({...ok,minimum_iou:NaN}));
  for(const id of ["=1+1","@SUM(A1)","../x","mã-1","x".repeat(65)]){
    assert.throws(()=>csv.formatCsv({...ok,per_sample:[{...ok.per_sample[0],id}]}));
  }
  assert.throws(()=>csv.formatCsv({...ok,per_sample:Array.from({length:2001},()=>ok.per_sample[0])}));
});
