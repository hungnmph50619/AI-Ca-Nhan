(() => {
  const VERSION = "0.6.3.1";

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }

  function initialize() {
    updateVersionLabels();
    document.addEventListener("keydown", handleEscape);
  }

  function updateVersionLabels() {
    const brandVersion = document.querySelector(".brand > div:last-child > span");
    if (brandVersion) brandVersion.textContent = `Phiên bản ${VERSION}`;

    const memoryIntro = document.querySelector(".memory-intro strong");
    if (memoryIntro) memoryIntro.textContent = `Trí nhớ v${VERSION}`;

    const knowledgeIntro = document.querySelector(".knowledge-intro strong");
    if (knowledgeIntro) knowledgeIntro.textContent = `Hỏi đáp có nguồn v${VERSION}`;
  }

  function handleEscape(event) {
    if (event.key !== "Escape") return;
    const openDialog = [...document.querySelectorAll("dialog[open]")].at(-1);
    if (openDialog) openDialog.close();
  }
})();
