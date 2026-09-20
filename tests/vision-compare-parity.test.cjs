"use strict";
// Kiểm tra hai mã triển khai JavaScript và Python dùng cùng quy tắc trên
// các mẫu tọa độ giả lập cố định. Không dùng ảnh thật, API hay Gemini.
const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const {execFileSync} = require("node:child_process");
const compare = require("../src/PersonalAI.Web/wwwroot/vision-compare-core.js");

function generatePair(seed, count = 128) {
  let state = seed >>> 0;
  const rand = max => {
    state = (Math.imul(1664525, state) + 1013904223) >>> 0;
    return state % max;
  };
  const box = () => {
    const x = 25 + rand(600), y = 25 + rand(600);
    return [x, y, x + 120 + rand(140), y + 120 + rand(140)];
  };
  const chosen = (truth, variant) => {
    if (variant === 0) return null;
    if (variant === 1 && truth) return [...truth];
    if (variant === 2 && truth) return [
      truth[0] + 20, truth[1] + 15, truth[2] + 20, truth[3] + 15
    ];
    if (variant === 3 && truth) return [
      truth[0] + 60, truth[1] + 60, truth[2] + 60, truth[3] + 60
    ];
    return box();
  };
  const a = [], b = [];
  for (let i = 0; i < count; i++) {
    const truth = i % 5 === 0 ? null : box();
    const id = "mau-" + String(i + 1).padStart(4, "0");
    a.push({id, reviewed:true, truth_box:truth, predicted_box:chosen(truth, i % 5)});
    b.push({id, reviewed:true, truth_box:truth ? [...truth] : null,
      predicted_box:chosen(truth, (i + 2) % 5)});
  }
  return [a,b.reverse()];
}

function pythonReport(dir, a, b, threshold) {
  const aPath = path.join(dir, "a.json");
  const bPath = path.join(dir, "b.json");
  fs.writeFileSync(aPath, JSON.stringify(a), "utf8");
  fs.writeFileSync(bPath, JSON.stringify(b), "utf8");
  const script = path.join(__dirname, "../scripts/compare_minimap_evaluations.py");
  const command = process.env.PYTHON || "python";
  const result = execFileSync(command,
    [script, aPath, bPath, "--min-iou", String(threshold)],
    {encoding:"utf8", timeout:15000, maxBuffer:2*1024*1024});
  return JSON.parse(result);
}

function approx(a, b, label) {
  if (a === null || b === null) {
    assert.equal(a,b,label);
  } else {
    assert.equal(typeof a,"number",label);
    assert.equal(typeof b,"number",label);
    assert.ok(Math.abs(a-b) <= 0.0000011,
      label + ": JS " + a + ", Python " + b);
  }
}

test("Đối chiếu JS/Python trên cùng 128 mẫu, hai bộ seed và ba ngưỡng IoU", () => {
  const folder = fs.mkdtempSync(path.join(os.tmpdir(),"minimap-parity-"));
  try {
    for (const seed of [2026, 45061]) {
      const [a,b] = generatePair(seed);
      for (const threshold of [0.5,0.75,0.9]) {
        const js = compare.comparePaired(a,b,threshold);
        const py = pythonReport(folder,a,b,threshold);
        const prefix = seed + " @ " + threshold;
        for (const name of ["samples","minimum_iou","changed_predictions"]) {
          assert.equal(js[name],py[name],prefix + " " + name);
        }
        assert.deepEqual(js.transitions,py.transitions,prefix + " transitions");
        for (const side of ["a","b"]) {
          for (const key of ["samples","tp","fp","fn","tn"]) {
            assert.equal(js[side][key],py[side][key],prefix + " " + side + "." + key);
          }
          for (const key of ["precision","recall","f1"]) {
            approx(js[side][key],py[side][key],prefix + " " + side + "." + key);
          }
        }
        assert.equal(js.per_sample.length,py.per_sample.length,prefix + " rows");
        for (let i=0; i<js.per_sample.length; i++) {
          const j = js.per_sample[i], p = py.per_sample[i];
          for (const field of ["id","transition","prediction_changed","a_outcome","b_outcome"]) {
            assert.equal(j[field],p[field],prefix + " " + i + "." + field);
          }
          approx(j.a_iou,p.a_iou,prefix + " " + i + ".a_iou");
          approx(j.b_iou,p.b_iou,prefix + " " + i + ".b_iou");
        }
      }
    }
  } finally {
    fs.rmSync(folder,{recursive:true,force:true});
  }
});

test("JS/Python cùng từ chối ID ngoài định dạng ASCII quy định", () => {
  const folder = fs.mkdtempSync(path.join(os.tmpdir(),"minimap-parity-bad-"));
  try {
    const sample = id => ({id,reviewed:true,truth_box:null,predicted_box:null});
    for (const id of ["có-dấu","space id","../bad",""]) {
      assert.throws(() => compare.comparePaired([sample(id)],[sample(id)]));
      const data=[sample(id)];
      assert.throws(() => pythonReport(folder,data,data,0.5));
    }
  } finally {
    fs.rmSync(folder,{recursive:true,force:true});
  }
});
