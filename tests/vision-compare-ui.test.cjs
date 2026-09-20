"use strict";
const assert = require("node:assert/strict");
const test = require("node:test");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");
const review = require("../src/PersonalAI.Web/wwwroot/vision-review-core.js");
const compare = require("../src/PersonalAI.Web/wwwroot/vision-compare-core.js");
const source = fs.readFileSync(path.join(__dirname,
  "../src/PersonalAI.Web/wwwroot/vision-compare.js"), "utf8");

function fixture() {
  const elements = new Map();
  function el(id) {
    if (!elements.has(id)) {
      const listeners = new Map();
      elements.set(id, {
        value:"", files:[], disabled:false, textContent:"", children:[],
        addEventListener(type, handler){listeners.set(type,handler);},
        emit(type){return listeners.get(type)?.({});},
        replaceChildren(){this.children=[];this.value="";},
        appendChild(child){this.children.push(child);if(this.children.length===1)this.value=child.value;}
      });
    }
    return elements.get(id);
  }
  el("comparisonThreshold").value="0.5";
  let fetchCount=0;
  const context={
    MinimapReviewCore:review, MinimapCompareCore:compare,
    document:{getElementById:el,createElement:()=>({value:"",textContent:""})},
    fetch(){fetchCount++;throw Error("Trang A/B không được gọi mạng");}
  };
  context.globalThis=context;
  vm.runInNewContext(source,context,{filename:"vision-compare.js"});
  const file=(name,data)=>({name,size:128,text:async()=>JSON.stringify(data)});
  const row=(id,truth,predicted)=>({id,reviewed:true,truth_box:truth,predicted_box:predicted});
  return {el,file,row,fetchCount:()=>fetchCount};
}

test("Bấm so sánh mới tính, không gọi mạng, hiển thị A/B đúng ID",async()=>{
  const ui=fixture();
  ui.el("comparisonA").files=[ui.file("a.json",[ui.row("one",[0,0,200,200],null)])];
  ui.el("comparisonB").files=[ui.file("b.json",[ui.row("one",[0,0,200,200],[0,0,200,200])])];
  assert.match(ui.el("comparisonSummary").textContent,/Chưa có kết quả/);
  await ui.el("runComparison").emit("click");
  assert.equal(ui.fetchCount(),0);
  assert.match(ui.el("comparisonSummary").textContent,/dự đoán thay đổi ở 1 mẫu/);
  assert.match(ui.el("comparisonMetrics").textContent,/Bộ A.*Bộ B/);
  assert.match(ui.el("comparisonDetails").textContent,/false_negative.*true_positive/);
  assert.match(ui.el("comparisonTransitions").textContent,/A sai.*1.*B sai: 0/);
  ui.el("comparisonFilter").value="corrected";
  ui.el("comparisonFilter").emit("change");
  assert.equal(ui.el("comparisonSample").disabled,false);
  assert.match(ui.el("comparisonDetails").textContent,/false_negative.*true_positive/);
  ui.el("comparisonFilter").value="regressed";
  ui.el("comparisonFilter").emit("change");
  assert.equal(ui.el("comparisonSample").disabled,true);
  assert.match(ui.el("comparisonDetails").textContent,/Không có mẫu/);
});

test("Dữ liệu A/B lệch nhãn không tạo báo cáo",async()=>{
  const ui=fixture();
  ui.el("comparisonA").files=[ui.file("a.json",[ui.row("one",[0,0,200,200],null)])];
  ui.el("comparisonB").files=[ui.file("b.json",[ui.row("one",[0,0,201,200],null)])];
  await ui.el("runComparison").emit("click");
  assert.match(ui.el("comparisonStatus").textContent,/không so sánh/i);
  assert.match(ui.el("comparisonSummary").textContent,/Chưa có kết quả/);
  assert.equal(ui.fetchCount(),0);
});

test("Đổi tệp khi đang đọc loại bỏ kết quả cũ",async()=>{
  const ui=fixture();
  let releaseRead;
  const waiting=new Promise(resolve=>{releaseRead=resolve;});
  const first={name:"first.json",size:128,text:async()=>waiting};
  ui.el("comparisonA").files=[first];
  ui.el("comparisonB").files=[ui.file("second.json",[ui.row("one",null,null)])];
  const inProgress=ui.el("runComparison").emit("click");
  ui.el("comparisonA").files=[ui.file("new.json",[ui.row("one",null,null)])];
  ui.el("comparisonA").emit("change");
  releaseRead(JSON.stringify([ui.row("one",null,null)]));
  await inProgress;
  assert.match(ui.el("comparisonStatus").textContent,/không thể so sánh/i);
  assert.match(ui.el("comparisonSummary").textContent,/Chưa có kết quả/);
  assert.equal(ui.fetchCount(),0);
});

test("Tệp JSON quá lớn bị chặn trước khi đọc",async()=>{
  const ui=fixture();
  let read=false;
  ui.el("comparisonA").files=[{name:"too-big.json",size:1024*1024+1,text:async()=>{read=true;return "[]";}}];
  ui.el("comparisonB").files=[ui.file("b.json",[ui.row("one",null,null)])];
  await ui.el("runComparison").emit("click");
  assert.equal(read,false);
  assert.match(ui.el("comparisonStatus").textContent,/1 MB/);
});
