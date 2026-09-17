(() => {
  const VERSION = "0.7.0";

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;

    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Kho dữ liệu đa định dạng v${VERSION}`;

    const fileInput = document.querySelector("#knowledgeFileInput");
    if (fileInput) {
      fileInput.setAttribute(
        "accept",
        ".pdf,.docx,.txt,.md,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document,text/plain,text/markdown");
    }
  }
})();
