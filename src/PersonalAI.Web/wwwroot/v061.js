(() => {
  // v0.6.1 could enter a MutationObserver feedback loop because it watched
  // the same subtree where it inserted suggestion cards. Keep this file as
  // a compatibility loader so cached index.html pages receive the fixed code.
  const script = document.createElement("script");
  script.src = "v0611.js?v=0611";
  script.defer = true;
  document.head.appendChild(script);
})();
