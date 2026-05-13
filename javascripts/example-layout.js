/**
 * For each example grid, cap the source code block height to the height of
 * the rendered output column and enable vertical scrolling on the <code>
 * element. The cap is applied to <code> (not <pre>) so that:
 *   - Material's sticky copy button (positioned inside <pre>) remains fixed
 *     at the top-right of the visible viewport instead of scrolling away.
 *   - The code-block background (painted on <pre>) continues to extend
 *     across horizontally-scrolled content.
 *   - The horizontal scrollbar (also on <code>) stays pinned to the bottom
 *     edge of the visible viewport rather than the bottom of the content.
 *
 * This runs after load (so images in the output are sized) and again on each
 * MkDocs Material SPA navigation.
 */
function syncExampleHeights() {
    document.querySelectorAll('.example-grid').forEach(function (grid) {
        var source = grid.querySelector('.highlight');
        var output = grid.querySelector('.example-output');
        if (!source || !output) return;
        var code = source.querySelector('pre > code');
        if (!code) return;

        // Reset so re-runs always start from a clean state.
        code.style.maxHeight = '';
        code.style.overflowY = '';

        var outputHeight = output.offsetHeight;
        if (outputHeight > 0) {
            code.style.maxHeight = outputHeight + 'px';
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
