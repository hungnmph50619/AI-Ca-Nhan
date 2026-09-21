"use strict";
const assert = require("node:assert/strict");
const test = require("node:test");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");
const review = require("../src/PersonalAI.Web/wwwroot/vision-review-core.js");
const compare = require("../src/PersonalAI.Web/wwwroot/vision-compare-core.js");
const compareCsv = require("../src/PersonalAI.Web/wwwroot/vision-compare-csv.js");
const source = fs.readFileSync(path.join(__dirname,
  "../src/PersonalAI.Web/wwwroot/vision-compare.js"), "utf8");

function fixture() {
  const elements = new Map();
  function el(id) {
    if (!elements.has(id)) {
      const listeners = new Map();
      elements.set(id, {
        value:"", files:[], checked:false, disabled:false, textContent:"", children:[],
        addEventListener(type, handler){listeners.set(type,handler);},
        emit(type){return listeners.get(type)?.({});},
        replaceChildren(){this.children=[];this.value="";},
        appendChild(child){this.children.push(child);if(this.children.length===1)this.value=child.value;}
      });
    }
    return elements.get(id);
  }
  el("comparisonThreshold").value="0.5";
  el("overlayVisibility").disabled=true;
  for (const id of ["overlayShowTruth","overlayShowA","overlayShowB"]) el(id).checked=true;
  const strokes=[];
  const canvas=el("comparisonOverlay");
  canvas.width=500;
  canvas.height=500;
  canvas.hidden=true;
  canvas.getContext=()=>({
    clearRect(){},fillRect(){},save(){},restore(){},setLineDash(){},
    strokeRect(x,y,width,height){strokes.push([x,y,width,height]);},
    fillStyle:"",strokeStyle:"",lineWidth:1
  });
  let fetchCount=0;
  const downloads=[];
  const blobs=[];
  const revoked=[];
  class FakeBlob {
    constructor(parts, options) { this.parts=parts; this.type=options.type; blobs.push(this); }
  }
  const context={
    MinimapReviewCore:review, MinimapCompareCore:compare, MinimapCompareCsv:compareCsv,
    document:{
      getElementById:el,
      createElement(tag){
        if (tag === "a") return {
          href:"", download:"", click(){downloads.push(this.download);}, remove(){}
        };
        if (tag === "tr") return {
          children:[],appendChild(child){this.children.push(child);}
        };
        return {value:"",textContent:""};
      },
      body:{appendChild(){}}
    },
    URL:{createObjectURL:()=>("blob:report-"+blobs.length),revokeObjectURL:url=>revoked.push(url)},
    Blob:FakeBlob,
    setTimeout(callback){callback();},
    fetch(){fetchCount++;throw Error("Trang A/B không được gọi mạng");}
  };
  context.globalThis=context;
  vm.runInNewContext(source,context,{filename:"vision-compare.js"});
  const file=(name,data)=>({name,size:128,text:async()=>JSON.stringify(data)});
  const row=(id,truth,predicted)=>({id,reviewed:true,truth_box:truth,predicted_box:predicted});
  return {el,file,row,fetchCount:()=>fetchCount,downloads,blobs,revoked,strokes};
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


test("Báo cáo chỉ xuất sau khi so sánh thành công, không chứa tên file hoặc ảnh",async()=>{
  const ui=fixture();
  assert.equal(ui.el("exportComparison").disabled,true);
  assert.equal(ui.el("exportComparisonCsv").disabled,true);
  ui.el("exportComparison").emit("click");
  assert.equal(ui.downloads.length,0);
  ui.el("comparisonA").files=[ui.file("private-input-a.json",[ui.row("mau-1",[0,0,200,200],null)])];
  ui.el("comparisonB").files=[ui.file("private-input-b.json",[ui.row("mau-1",[0,0,200,200],[0,0,200,200])])];
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("exportComparison").disabled,false);
  ui.el("exportComparison").emit("click");
  assert.deepEqual(ui.downloads,["minimap-a-b-comparison.json"]);
  assert.equal(ui.blobs.length,1);
  const exported=ui.blobs[0].parts.join("");
  const report=JSON.parse(exported);
  assert.equal(report.schema,"minimap-paired-comparison-v1");
  assert.equal(report.samples,1);
  assert.deepEqual(report.transitions,{corrected:1,regressed:0,both_match:0,both_mismatch:0});
  assert.equal(report.per_sample[0].id,"mau-1");
  assert.deepEqual(Object.keys(report.per_sample[0]).sort(),[
    "a_iou","a_outcome","b_iou","b_outcome","id",
    "prediction_changed","transition"
  ]);
  assert.doesNotMatch(exported,/truth_box|a_box|b_box|0,0,200,200/);
  assert.doesNotMatch(exported,/private-input|image|api.key|gemini/i);
  assert.equal(ui.revoked.length,1);
  assert.equal(ui.fetchCount(),0);
  ui.el("comparisonThreshold").value="0.75";
  ui.el("comparisonThreshold").emit("change");
  assert.equal(ui.el("exportComparison").disabled,true);
  ui.el("exportComparison").emit("click");
  assert.equal(ui.blobs.length,1);
});

test("So sánh lỗi không để lại báo cáo JSON lỗi thời",async()=>{
  const ui=fixture();
  const first=[ui.row("mau-1",null,null)];
  ui.el("comparisonA").files=[ui.file("a.json",first)];
  ui.el("comparisonB").files=[ui.file("b.json",first)];
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("exportComparison").disabled,false);
  ui.el("comparisonB").files=[ui.file("b.json",[ui.row("another",null,null)])];
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("exportComparison").disabled,true);
  ui.el("exportComparison").emit("click");
  assert.equal(ui.downloads.length,0);
  assert.equal(ui.fetchCount(),0);
});


test("Sơ đồ không có ảnh hiển thị ba khung khác nhau và chữ mô tả",async()=>{
  const ui=fixture();
  const truth=[100,200,500,600], a=[110,205,505,605], b=[300,400,800,900];
  ui.el("comparisonA").files=[ui.file("a.json",[ui.row("map-1",truth,a)])];
  ui.el("comparisonB").files=[ui.file("b.json",[ui.row("map-1",truth,b)])];
  assert.equal(ui.el("comparisonOverlay").hidden,true);
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("comparisonOverlay").hidden,false);
  assert.deepEqual(ui.strokes.slice(-3),[
    [66,112,184,184],
    [70.6,114.3,181.7,184],
    [158,204,230,230]
  ]);
  assert.match(ui.el("overlayText").textContent,/Nhãn chuẩn: 100, 200, 500, 600/);
  assert.match(ui.el("overlayText").textContent,/Dự đoán A: 110, 205, 505, 605/);
  assert.match(ui.el("overlayText").textContent,/Dự đoán B: 300, 400, 800, 900/);
  assert.equal(ui.fetchCount(),0);
  // A khớp/B lệch => nhóm "regressed" có chính mẫu này.
  // Nhóm "corrected" trống nên sơ đồ phải được ẩn.
  ui.el("comparisonFilter").value="corrected";
  ui.el("comparisonFilter").emit("change");
  assert.equal(ui.el("comparisonOverlay").hidden,true);
  ui.el("comparisonA").emit("change");
  assert.equal(ui.el("comparisonOverlay").hidden,true);
  assert.match(ui.el("overlayText").textContent,/Chưa có sơ đồ/);
});

test("Sơ đồ xử lý nhãn âm không vẽ khung không tồn tại",async()=>{
  const ui=fixture();
  ui.el("comparisonA").files=[ui.file("a.json",[ui.row("empty",null,null)])];
  ui.el("comparisonB").files=[ui.file("b.json",[ui.row("empty",null,[100,100,400,400])])];
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("comparisonOverlay").hidden,false);
  assert.equal(ui.strokes.length,2); // đường biên và khung của B
  assert.match(ui.el("overlayText").textContent,/Nhãn chuẩn: không có khung/);
  assert.match(ui.el("overlayText").textContent,/Dự đoán A: không có khung/);
  assert.equal(ui.fetchCount(),0);
});


test("Bật tắt riêng nhãn chuẩn, A, B chỉ vẽ lớp được chọn và không làm đổi báo cáo",async()=>{
  const ui=fixture();
  const truth=[100,200,500,600], a=[110,205,505,605], b=[300,400,800,900];
  ui.el("comparisonA").files=[ui.file("a.json",[ui.row("map-1",truth,a)])];
  ui.el("comparisonB").files=[ui.file("b.json",[ui.row("map-1",truth,b)])];
  assert.equal(ui.el("overlayVisibility").disabled,true);
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("overlayVisibility").disabled,false);
  assert.equal(ui.strokes.length,4); // biên sơ đồ + ba khung
  ui.el("overlayShowA").checked=false;
  ui.el("overlayShowA").emit("change");
  assert.equal(ui.strokes.length,7); // biên mới + hai khung còn hiện
  assert.match(ui.el("overlayText").textContent,/Dự đoán A: 110, 205, 505, 605 \(đang ẩn\)/);
  ui.el("overlayShowTruth").checked=false;
  ui.el("overlayShowTruth").emit("change");
  assert.equal(ui.strokes.length,9); // biên mới + chỉ B
  ui.el("overlayShowB").checked=false;
  ui.el("overlayShowB").emit("change");
  assert.equal(ui.strokes.length,10); // chỉ còn viền giới hạn sơ đồ
  assert.equal(ui.el("comparisonOverlay").hidden,false);
  assert.match(ui.el("overlayText").textContent,/Nhãn chuẩn: 100, 200, 500, 600 \(đang ẩn\)/);
  ui.el("exportComparison").emit("click");
  const report=JSON.parse(ui.blobs[0].parts.join(""));
  assert.equal(report.samples,1);
  assert.deepEqual(Object.keys(report.per_sample[0]).sort(),[
    "a_iou","a_outcome","b_iou","b_outcome","id","prediction_changed","transition"
  ]);
  assert.equal(ui.fetchCount(),0);
  ui.el("comparisonThreshold").emit("change");
  assert.equal(ui.el("overlayVisibility").disabled,true);
  assert.equal(ui.el("comparisonOverlay").hidden,true);
  ui.el("overlayShowA").checked=true;
  ui.el("overlayShowA").emit("change");
  assert.equal(ui.strokes.length,10); // không vẽ lại khi báo cáo hết hiệu lực
});


test("Nút trước/sau đi trong đúng nhóm đang lọc và cập nhật vị trí mẫu",async()=>{
  const ui=fixture();
  const truth=[0,0,200,200];
  const a=[
    ui.row("mau-1",truth,null),
    ui.row("mau-2",truth,truth),
    ui.row("mau-3",truth,truth)
  ];
  const b=[
    ui.row("mau-1",truth,truth),
    ui.row("mau-2",truth,null),
    ui.row("mau-3",truth,truth)
  ];
  ui.el("comparisonA").files=[ui.file("a.json",a)];
  ui.el("comparisonB").files=[ui.file("b.json",b)];
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("comparisonSample").value,"mau-1");
  assert.match(ui.el("samplePosition").textContent,/1 \/ 3/);
  assert.equal(ui.el("previousSample").disabled,true);
  assert.equal(ui.el("nextSample").disabled,false);

  ui.el("nextSample").emit("click");
  assert.equal(ui.el("comparisonSample").value,"mau-2");
  assert.match(ui.el("samplePosition").textContent,/2 \/ 3/);
  assert.match(ui.el("comparisonDetails").textContent,/Mẫu mau-2/);
  assert.equal(ui.el("previousSample").disabled,false);
  assert.equal(ui.el("nextSample").disabled,false);

  ui.el("nextSample").emit("click");
  assert.equal(ui.el("comparisonSample").value,"mau-3");
  assert.match(ui.el("samplePosition").textContent,/3 \/ 3/);
  assert.equal(ui.el("nextSample").disabled,true);
  ui.el("nextSample").emit("click");
  assert.equal(ui.el("comparisonSample").value,"mau-3");

  ui.el("comparisonFilter").value="corrected";
  ui.el("comparisonFilter").emit("change");
  assert.equal(ui.el("comparisonSample").value,"mau-1");
  assert.match(ui.el("samplePosition").textContent,/1 \/ 1/);
  assert.equal(ui.el("previousSample").disabled,true);
  assert.equal(ui.el("nextSample").disabled,true);

  ui.el("comparisonFilter").value="both_mismatch";
  ui.el("comparisonFilter").emit("change");
  assert.equal(ui.el("comparisonSample").disabled,true);
  assert.equal(ui.el("previousSample").disabled,true);
  assert.equal(ui.el("nextSample").disabled,true);
  assert.match(ui.el("samplePosition").textContent,/Không có mẫu/);
  assert.equal(ui.fetchCount(),0);
});

test("Đổi dữ liệu hoặc ngưỡng xóa trạng thái điều hướng cũ",async()=>{
  const ui=fixture();
  const rows=[ui.row("one",null,null),ui.row("two",null,null)];
  ui.el("comparisonA").files=[ui.file("a.json",rows)];
  ui.el("comparisonB").files=[ui.file("b.json",rows)];
  await ui.el("runComparison").emit("click");
  assert.match(ui.el("samplePosition").textContent,/1 \/ 2/);
  assert.equal(ui.el("nextSample").disabled,false);
  ui.el("comparisonThreshold").value="0.75";
  ui.el("comparisonThreshold").emit("change");
  assert.equal(ui.el("comparisonSample").disabled,true);
  assert.equal(ui.el("previousSample").disabled,true);
  assert.equal(ui.el("nextSample").disabled,true);
  assert.match(ui.el("samplePosition").textContent,/Chưa có mẫu/);
  ui.el("nextSample").emit("click");
  assert.equal(ui.el("comparisonSample").value,"");
});


test("Tìm mã mẫu kết hợp bộ lọc, điều hướng trong kết quả khớp và không gọi mạng",async()=>{
  const ui=fixture();
  const truth=[0,0,200,200];
  const a=[
    ui.row("Alpha-01",truth,null),
    ui.row("Alpha-02",truth,truth),
    ui.row("Beta-03",truth,null),
    ui.row("Gamma-04",truth,truth)
  ];
  const b=[
    ui.row("Alpha-01",truth,truth),
    ui.row("Alpha-02",truth,null),
    ui.row("Beta-03",truth,truth),
    ui.row("Gamma-04",truth,truth)
  ];
  ui.el("comparisonA").files=[ui.file("a.json",a)];
  ui.el("comparisonB").files=[ui.file("b.json",b)];
  assert.equal(ui.el("comparisonSearch").disabled,true);
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("comparisonSearch").disabled,false);
  assert.match(ui.el("samplePosition").textContent,/1 \/ 4/);

  ui.el("comparisonSearch").value="aLpHa";
  ui.el("comparisonSearch").emit("input");
  assert.match(ui.el("samplePosition").textContent,/1 \/ 2/);
  assert.equal(ui.el("comparisonSample").value,"Alpha-01");
  ui.el("nextSample").emit("click");
  assert.equal(ui.el("comparisonSample").value,"Alpha-02");
  assert.match(ui.el("samplePosition").textContent,/2 \/ 2/);

  ui.el("comparisonFilter").value="corrected";
  ui.el("comparisonFilter").emit("change");
  assert.equal(ui.el("comparisonSample").value,"Alpha-01");
  assert.match(ui.el("samplePosition").textContent,/1 \/ 1/);
  assert.equal(ui.el("nextSample").disabled,true);
  ui.el("comparisonSearch").value=" beta ";
  ui.el("comparisonSearch").emit("input");
  assert.equal(ui.el("comparisonSample").value,"Beta-03");
  assert.match(ui.el("samplePosition").textContent,/1 \/ 1/);
  assert.equal(ui.el("previousSample").disabled,true);

  ui.el("comparisonSearch").value="khong-co";
  ui.el("comparisonSearch").emit("input");
  assert.equal(ui.el("comparisonSample").disabled,true);
  assert.equal(ui.el("previousSample").disabled,true);
  assert.equal(ui.el("nextSample").disabled,true);
  assert.equal(ui.el("comparisonOverlay").hidden,true);
  assert.match(ui.el("comparisonDetails").textContent,/Không có mẫu/);

  ui.el("comparisonSearch").value="";
  ui.el("comparisonSearch").emit("input");
  assert.match(ui.el("samplePosition").textContent,/1 \/ 2/);
  ui.el("exportComparison").emit("click");
  const report=JSON.parse(ui.blobs[0].parts.join(""));
  assert.equal(report.samples,4); // Lọc chỉ tác động cách xem, không thay báo cáo toàn bộ
  assert.equal(report.per_sample.length,4);
  assert.equal(ui.fetchCount(),0);
});

test("Đổi file hoặc ngưỡng sẽ khóa và xóa mã tìm kiếm cũ",async()=>{
  const ui=fixture();
  const rows=[ui.row("Alpha-01",null,null),ui.row("Beta-02",null,null)];
  ui.el("comparisonA").files=[ui.file("a.json",rows)];
  ui.el("comparisonB").files=[ui.file("b.json",rows)];
  await ui.el("runComparison").emit("click");
  ui.el("comparisonSearch").value="alpha";
  ui.el("comparisonSearch").emit("input");
  assert.match(ui.el("samplePosition").textContent,/1 \/ 1/);
  ui.el("comparisonB").emit("change");
  assert.equal(ui.el("comparisonSearch").disabled,true);
  assert.equal(ui.el("comparisonSearch").value,"");
  assert.equal(ui.el("comparisonSample").disabled,true);
  assert.equal(ui.el("comparisonOverlay").hidden,true);
  assert.equal(ui.el("exportComparison").disabled,true);
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("comparisonSearch").disabled,false);
  assert.match(ui.el("samplePosition").textContent,/1 \/ 2/);
  ui.el("comparisonThreshold").emit("change");
  assert.equal(ui.el("comparisonSearch").disabled,true);
  assert.equal(ui.el("comparisonSearch").value,"");
  assert.equal(ui.fetchCount(),0);
});


test("CSV tải đúng toàn bộ mẫu ngay cả khi tìm kiếm đang ẩn bớt; không xuất tọa độ",async()=>{
  const ui=fixture();
  const truth=[123,234,345,456];
  const a=[ui.row("Alpha-01",truth,null),ui.row("Beta-02",null,null)];
  const b=[ui.row("Beta-02",null,null),ui.row("Alpha-01",truth,truth)];
  ui.el("comparisonA").files=[ui.file("secret-a.json",a)];
  ui.el("comparisonB").files=[ui.file("secret-b.json",b)];
  assert.equal(ui.el("exportComparisonCsv").disabled,true);
  ui.el("exportComparisonCsv").emit("click");
  assert.equal(ui.downloads.length,0);
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("exportComparisonCsv").disabled,false);
  ui.el("comparisonSearch").value="alpha";
  ui.el("comparisonSearch").emit("input");
  assert.match(ui.el("samplePosition").textContent,/1 \/ 1/);
  ui.el("exportComparisonCsv").emit("click");
  assert.deepEqual(ui.downloads,["minimap-a-b-comparison.csv"]);
  assert.equal(ui.blobs.length,1);
  assert.equal(ui.blobs[0].type,"text/csv;charset=utf-8");
  const exported=ui.blobs[0].parts.join("");
  assert.equal(exported.slice(1).trim().split("\r\n").length,3); // header + toàn bộ 2 mẫu
  assert.match(exported,/Alpha-01/);
  assert.match(exported,/Beta-02/);
  assert.doesNotMatch(exported,/123|234|345|456|secret-a|truth_box|a_box|b_box/);
  assert.equal(ui.revoked.length,1);
  assert.equal(ui.fetchCount(),0);
  ui.el("comparisonThreshold").emit("change");
  assert.equal(ui.el("exportComparisonCsv").disabled,true);
  ui.el("exportComparisonCsv").emit("click");
  assert.equal(ui.downloads.length,1);
});

test("So sánh JSON lỗi sẽ khóa xuất CSV của kết quả trước",async()=>{
  const ui=fixture();
  const first=[ui.row("same",null,null)];
  ui.el("comparisonA").files=[ui.file("a.json",first)];
  ui.el("comparisonB").files=[ui.file("b.json",first)];
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("exportComparisonCsv").disabled,false);
  ui.el("comparisonB").files=[ui.file("invalid.json",[ui.row("different",null,null)])];
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("exportComparisonCsv").disabled,true);
  ui.el("exportComparisonCsv").emit("click");
  assert.equal(ui.blobs.length,0);
  assert.equal(ui.fetchCount(),0);
});


test("Bảng ba ngưỡng được tính cục bộ, không thay đổi ngưỡng tải báo cáo",async()=>{
  const ui=fixture();
  const truth=[0,0,200,200], shifted=[20,0,220,200];
  ui.el("comparisonA").files=[ui.file("a.json",[ui.row("one",truth,null)])];
  ui.el("comparisonB").files=[ui.file("b.json",[ui.row("one",truth,shifted)])];
  assert.equal(ui.el("sensitivityRows").children.length,0);
  await ui.el("runComparison").emit("click");
  const rows=ui.el("sensitivityRows").children;
  assert.equal(rows.length,3);
  assert.deepEqual(rows.map(row=>row.children[0].textContent),["0.50","0.75","0.90"]);
  assert.deepEqual(rows.map(row=>row.children[1].textContent),["0 / 1","0 / 1","0 / 0"]);
  assert.deepEqual(rows.map(row=>row.children[6].textContent),["1","1","0"]);
  assert.match(ui.el("sensitivityStatus").textContent,/3 ngưỡng/);
  assert.equal(ui.fetchCount(),0);

  ui.el("exportComparison").emit("click");
  const report=JSON.parse(ui.blobs[0].parts.join(""));
  assert.equal(report.minimum_iou,0.5);
  assert.equal(report.per_sample.length,1);
  assert.equal(Object.hasOwn(report,"sensitivity"),false);
  ui.el("exportComparisonCsv").emit("click");
  assert.match(ui.blobs[1].parts.join(""),/"0.5"/);
  assert.doesNotMatch(ui.blobs[1].parts.join(""),/"0.9"/);

  ui.el("comparisonThreshold").value="0.9";
  ui.el("comparisonThreshold").emit("change");
  assert.equal(ui.el("sensitivityRows").children.length,0);
  assert.match(ui.el("sensitivityStatus").textContent,/Chưa có dữ liệu/);
  assert.equal(ui.el("exportComparison").disabled,true);
  assert.equal(ui.el("exportComparisonCsv").disabled,true);
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("sensitivityRows").children.length,3);
  ui.el("exportComparison").emit("click");
  const newReport=JSON.parse(ui.blobs[2].parts.join(""));
  assert.equal(newReport.minimum_iou,0.9);
  assert.equal(ui.fetchCount(),0);
});

test("So sánh thất bại không giữ lại bảng nhạy cảm theo ngưỡng của dữ liệu cũ",async()=>{
  const ui=fixture();
  const rows=[ui.row("one",null,null)];
  ui.el("comparisonA").files=[ui.file("a.json",rows)];
  ui.el("comparisonB").files=[ui.file("b.json",rows)];
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("sensitivityRows").children.length,3);
  ui.el("comparisonB").files=[ui.file("wrong.json",[ui.row("two",null,null)])];
  await ui.el("runComparison").emit("click");
  assert.equal(ui.el("sensitivityRows").children.length,0);
  assert.match(ui.el("sensitivityStatus").textContent,/Chưa có dữ liệu/);
  assert.equal(ui.el("exportComparison").disabled,true);
  assert.equal(ui.fetchCount(),0);
});
