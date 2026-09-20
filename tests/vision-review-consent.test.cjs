"use strict";
// Kiểm thử hành vi nút đề xuất bằng DOM giả; không có ảnh thật hay mạng.
const assert = require("node:assert/strict");
const test = require("node:test");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");
const core = require("../src/PersonalAI.Web/wwwroot/vision-review-core.js");
const script = fs.readFileSync(path.join(__dirname, "../src/PersonalAI.Web/wwwroot/vision-review.js"), "utf8");

function setup(fetchImplementation) {
  const elements = new Map();
  function element(id) {
    if (!elements.has(id)) {
      const listeners = new Map();
      elements.set(id, {
        id, value: "", checked: false, disabled: false, files: [], hidden: true,
        textContent: "", style: {}, listeners,
        addEventListener(type, handler) { listeners.set(type, handler); },
        emit(type) { return listeners.get(type)?.({}); },
        removeAttribute() {}, focus() {}, click() {},
        getBoundingClientRect() { return {left: 0, top: 0, width: 960, height: 540}; }
      });
    }
    return elements.get(id);
  }
  const canvas = element("imageCanvas");
  canvas.getContext = () => ({
    clearRect() {}, drawImage() {}, save() {}, restore() {}, setLineDash() {},
    strokeRect() {}, strokeStyle: "", lineWidth: 2
  });
  element("qualityThreshold").value = "0.5";
  const document = {
    getElementById: element,
    createElement: () => ({click(){}, remove(){}}),
    body: {appendChild(){}}
  };
  class FakeImage {
    naturalWidth = 960;
    naturalHeight = 540;
    set src(value) { if (value) this.onload?.(); }
  }
  class FakeFormData {
    fields = [];
    append(name, value, filename) { this.fields.push({name, value, filename}); }
  }
  const calls = [];
  const context = {
    MinimapReviewCore: core,
    document,
    Image: FakeImage,
    FormData: FakeFormData,
    URL: {createObjectURL: () => "blob:test", revokeObjectURL() {}},
    window: {addEventListener() {}, confirm: () => true},
    fetch: async (url, options) => {
      calls.push({url, options});
      return fetchImplementation(url, options);
    },
    console,
    setTimeout() {},
    Blob: class {},
  };
  context.globalThis = context;
  vm.runInNewContext(script, context, {filename: "vision-review.js"});
  function selectImage(id) {
    const input = element("imageFile");
    input.files = [{ name: id + ".png", size: 4096, type: "image/png" }];
    input.emit("change");
  }
  async function clickLocate() { await element("locateButton").emit("click"); }
  function consent() {
    element("locateConsent").checked = true;
    element("locateConsent").emit("change");
  }
  return {element, calls, selectImage, clickLocate, consent};
}

function response(data) { return {ok: true, json: async () => data}; }
const ready = {available: true};
const proposed = {
  provider: "Gemini", verified: false, needsReview: true,
  width: 960, height: 540, found: true,
  normalizedBox: [600, 100, 900, 400]
};

test("Chọn ảnh hoặc chỉ tích xác nhận KHÔNG gửi ảnh; một lần bấm mới gửi", async () => {
  const app = setup(url => response(url.endsWith("/status") ? ready : proposed));
  app.selectImage("a");
  assert.equal(app.calls.length, 0);
  assert.equal(app.element("locateButton").disabled, true);
  app.consent();
  assert.equal(app.calls.length, 0);
  assert.equal(app.element("locateButton").disabled, false);
  await app.clickLocate();
  assert.deepEqual(app.calls.map(x => x.url), [
    "/api/vision/minimap/locate/status", "/api/vision/minimap/locate"
  ]);
  assert.equal(app.calls[1].options.method, "POST");
  assert.equal(app.calls[1].options.headers["X-Xerath-Vision"], "1");
  assert.equal(app.calls[1].options.body.fields.find(x => x.name === "confirmed").value, "true");
  assert.equal(app.element("predictionMode").value, "found");
  assert.equal(app.element("predictionBox").value, "[600,100,900,400]");
  assert.equal(app.element("reviewed").checked, false);
  assert.equal(app.element("locateConsent").checked, false);
  assert.equal(app.element("locateButton").disabled, true);
  await app.clickLocate();
  assert.equal(app.calls.length, 2, "Không gửi lại khi chưa đồng ý lần nữa");
});

test("Rút lại đồng ý khi đang kiểm tra trạng thái: không gửi ảnh", async () => {
  let finishStatus;
  const waiting = new Promise(resolve => { finishStatus = resolve; });
  const app = setup(url => url.endsWith("/status") ? waiting : response(proposed));
  app.selectImage("a");
  app.consent();
  const inflight = app.clickLocate();
  app.element("locateConsent").checked = false;
  app.element("locateConsent").emit("change");
  finishStatus(response(ready));
  await inflight;
  assert.equal(app.calls.filter(x => x.url === "/api/vision/minimap/locate").length, 0);
});

test("Đổi ảnh khi đang kiểm tra trạng thái: không gửi ảnh cũ", async () => {
  let finishStatus;
  const waiting = new Promise(resolve => { finishStatus = resolve; });
  const app = setup(url => url.endsWith("/status") ? waiting : response(proposed));
  app.selectImage("a");
  app.consent();
  const inflight = app.clickLocate();
  app.selectImage("b");
  finishStatus(response(ready));
  await inflight;
  assert.equal(app.calls.filter(x => x.url === "/api/vision/minimap/locate").length, 0);
  assert.equal(app.element("locateConsent").checked, false);
  assert.equal(app.element("predictionBox").value, "");
});

test("Đổi ảnh khi Gemini đang trả kết quả: bỏ qua đề xuất cũ", async () => {
  let finishProposal;
  const waiting = new Promise(resolve => { finishProposal = resolve; });
  const app = setup(url => url.endsWith("/status") ? response(ready) : waiting);
  app.selectImage("a");
  app.consent();
  const inflight = app.clickLocate();
  // Chờ tới khi fetch /locate thực sự bắt đầu, không dùng timeout.
  for (let i = 0; i < 12 && app.calls.length < 2; i++) await Promise.resolve();
  assert.equal(app.calls.length, 2);
  app.selectImage("b");
  finishProposal(response(proposed));
  await inflight;
  assert.equal(app.element("predictionMode").value, "");
  assert.equal(app.element("predictionBox").value, "");
  assert.equal(app.element("reviewed").checked, false);
});

test("Kết quả sai kích thước không ghi vào nhãn chuẩn", async () => {
  const app = setup(url => response(url.endsWith("/status") ? ready : {...proposed, width: 1920}));
  app.selectImage("a");
  app.consent();
  await app.clickLocate();
  assert.equal(app.element("predictionMode").value, "");
  assert.equal(app.element("predictionBox").value, "");
  assert.match(app.element("locateStatus").textContent, /không phù hợp/);
});
