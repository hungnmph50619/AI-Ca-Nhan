(() => {
  // Compatibility loader: always apply the v0.6.1.1 MutationObserver hotfix
  // before layering the v0.6.2 memory-management UI on top of v0.6.0.
  const hotfix = document.createElement("script");
  hotfix.src = "v0611.js?v=0611";
  hotfix.onload = () => {
    const v062 = document.createElement("script");
    v062.src = "v062.js?v=0621";
    document.head.appendChild(v062);
  };
  document.head.appendChild(hotfix);
})();
