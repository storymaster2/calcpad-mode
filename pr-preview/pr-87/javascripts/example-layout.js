/**
 * For each example grid, cap the source code viewport height to the height of
 * the rendered output column and enable vertical scrolling on the <code>
 * element. The measured height is also applied to the outer .highlight
 * wrapper and the inner <code> scroll container so the visible grey code
 * panel fills the full output-column height and both scrollbars stay pinned
 * to its bottom edge. The scroll stays on <code> (not <pre>) so
 * that:
 *   - Material's sticky copy button (positioned inside <pre>) remains fixed
 *     at the top-right of the visible viewport instead of scrolling away.
 *   - The code panel background can live on the outer .highlight wrapper,
 *     while the code content still scrolls inside it.
 *   - The horizontal scrollbar (also on <code>) stays pinned to the bottom
 *     edge of the visible viewport rather than the bottom of the content.
 *
 * This runs after load (so images in the output are sized) and again on each
 * MkDocs Material SPA navigation.
 */
function syncExampleHeights() {
    document.querySelectorAll('.example-grid').forEach(function (grid) {
        var source = grid.querySelector('.highlight');
        var sourceCaption = grid.querySelector('figure figcaption');
        var output = grid.querySelector('.example-output');
        if (!source || !output || !sourceCaption) return;
        var code = source.querySelector('pre > code');
        if (!code) return;

        // Reset so re-runs always start from a clean state.
        source.style.height = '';
        source.style.overflow = '';
        code.style.height = '';
        code.style.maxHeight = '';
        code.style.overflowY = '';

        var panelHeight = output.offsetHeight - sourceCaption.offsetHeight;
        if (panelHeight > 0) {
            source.style.height = panelHeight + 'px';
            source.style.overflow = 'hidden';
            code.style.height = panelHeight + 'px';
            code.style.maxHeight = panelHeight + 'px';
            code.style.overflowY = 'auto';
        }
    });
}

// After all images/resources in the output column are loaded.
window.addEventListener('load', syncExampleHeights);

// MkDocs Material SPA navigation — document$ fires on every page switch.
if (typeof document$ !== 'undefined') {
    document$.subscribe(function () {
        requestAnimationFrame(syncExampleHeights);
    });
}
