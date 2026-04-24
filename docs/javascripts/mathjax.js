window.MathJax = {
  tex: {
    inlineMath: [["\\(", "\\)"]],
    displayMath: [["\\[", "\\]"]],
    processEscapes: true,
    processEnvironments: true,
  },
  options: {
    ignoreHtmlClass: ".*",
    processHtmlClass: "arithmatex|md-ellipsis",
  },
};

// Material for MkDocs uses instant loading, which swaps page content via XHR
// without a full browser reload. MathJax must re-typeset after each navigation.
document$.subscribe(function () {
  MathJax.startup.output.clearCache();
  MathJax.typesetClear();
  MathJax.texReset();
  MathJax.typesetPromise();
});
