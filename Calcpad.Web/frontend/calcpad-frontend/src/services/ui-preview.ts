/**
 * Shared UI preview injection logic for Calcpad frontends.
 * Provides CDN tags, event scripts, and helpers for entry inputs and datagrid controls.
 */

/**
 * Returns CDN script/link tags for Jspreadsheet CE (datagrid support).
 * Inject into <head> only when UI mode is active and datagrids are present.
 */
export function getDatagridCdnTags(): string {
    return [
        '<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/jspreadsheet-ce/dist/jspreadsheet.min.css" />',
        '<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/jsuites/dist/jsuites.min.css" />',
        '<script src="https://cdn.jsdelivr.net/npm/jsuites/dist/jsuites.min.js"></script>',
        '<script src="https://cdn.jsdelivr.net/npm/jspreadsheet-ce/dist/index.min.js"></script>',
    ].join('\n');
}

/**
 * Checks if the HTML contains any datagrid UI elements.
 */
export function htmlHasDatagrids(html: string): boolean {
    return html.includes('calcpad-ui-datagrid');
}

/**
 * Returns a <script> block that initializes all UI controls and wires up change events.
 * @param postMessageExpr - JS expression to call for sending messages.
 *   e.g. "vscode.postMessage" for VS Code, "window.parent.postMessage" for iframes.
 * @param iframeMode - If true, the second argument to postMessage is '*' (for cross-origin iframes).
 */
export function getUiEventScript(postMessageExpr: string, iframeMode: boolean = false): string {
    const postCall = iframeMode
        ? `${postMessageExpr}(msg, '*')`
        : `${postMessageExpr}(msg)`;

    return `<script>
(function() {
    function sendMsg(type, varName, newValue, sourceLine) {
        var msg = { type: type, varName: varName, newValue: newValue, sourceLine: sourceLine };
        ${postCall};
    }

    // --- Entry inputs ---
    document.querySelectorAll('.calcpad-ui-input').forEach(function(input) {
        input.addEventListener('change', function() {
            sendMsg('uiValueChange',
                input.getAttribute('data-ui-var'),
                input.value,
                parseInt(input.getAttribute('data-ui-line') || '0'));
        });
        input.addEventListener('keydown', function(e) {
            if (e.key === 'Enter') { input.blur(); }
        });
    });

    // --- Dropdown controls ---
    document.querySelectorAll('.calcpad-ui-dropdown').forEach(function(select) {
        select.addEventListener('change', function() {
            sendMsg('uiValueChange',
                select.getAttribute('data-ui-var'),
                select.value,
                parseInt(select.getAttribute('data-ui-line') || '0'));
        });
    });

    // --- Radio button controls ---
    document.querySelectorAll('.calcpad-ui-radio').forEach(function(group) {
        var radios = group.querySelectorAll('input[type="radio"]');
        radios.forEach(function(radio) {
            radio.addEventListener('change', function() {
                if (radio.checked) {
                    sendMsg('uiValueChange',
                        group.getAttribute('data-ui-var'),
                        radio.value,
                        parseInt(group.getAttribute('data-ui-line') || '0'));
                }
            });
        });
    });

    // --- Checkbox controls ---
    document.querySelectorAll('.calcpad-ui-checkbox').forEach(function(cb) {
        cb.addEventListener('change', function() {
            sendMsg('uiValueChange',
                cb.getAttribute('data-ui-var'),
                cb.checked ? '1' : '0',
                parseInt(cb.getAttribute('data-ui-line') || '0'));
        });
    });

    // --- Datagrid controls ---
    // CDN scripts may still be loading when this runs in VS Code webviews.
    // Poll for jspreadsheet availability with a timeout.
    var datagridContainers = document.querySelectorAll('.calcpad-ui-datagrid');
    if (datagridContainers.length > 0) {
        console.log('[CalcPad UI] Found ' + datagridContainers.length + ' datagrid container(s)');
        var attempts = 0;
        var maxAttempts = 50; // 50 * 100ms = 5 seconds
        function tryInitDatagrids() {
            if (typeof jspreadsheet !== 'undefined') {
                initAllDatagrids();
            } else if (++attempts < maxAttempts) {
                setTimeout(tryInitDatagrids, 100);
            } else {
                console.error('[CalcPad UI] jspreadsheet library not loaded after 5s. Check CDN availability.');
                datagridContainers.forEach(function(container) {
                    container.innerHTML = '<p style="color:red;font-size:10pt;">Datagrid library failed to load</p>';
                });
            }
        }
        tryInitDatagrids();
    }

    function initAllDatagrids() {
        console.log('[CalcPad UI] Initializing ' + datagridContainers.length + ' datagrid(s)');
        datagridContainers.forEach(function(container) {
            var rows = parseInt(container.getAttribute('data-ui-rows') || '3');
            var cols = parseInt(container.getAttribute('data-ui-columns') || '1');
            var valuesStr = container.getAttribute('data-ui-values') || '';
            var varName = container.getAttribute('data-ui-var');
            var sourceLine = parseInt(container.getAttribute('data-ui-line') || '0');

            // Parse Calcpad format: | separates rows, ; separates elements within a row
            var data;
            if (valuesStr) {
                if (valuesStr.indexOf('|') >= 0) {
                    // Matrix: split by | for rows, then ; for columns
                    data = valuesStr.split('|').map(function(row) {
                        return row.split(';');
                    });
                } else {
                    // Vector: single row with ; separated columns
                    data = [valuesStr.split(';')];
                }
            } else {
                data = [];
                for (var r = 0; r < rows; r++) {
                    var row = [];
                    for (var c = 0; c < cols; c++) row.push('0');
                    data.push(row);
                }
            }

            // Read custom headers if provided
            var colHeadersStr = container.getAttribute('data-ui-col-headers') || '';
            var rowHeadersStr = container.getAttribute('data-ui-row-headers') || '';
            var colHeaders = colHeadersStr ? colHeadersStr.split(',') : null;
            var rowHeaders = rowHeadersStr ? rowHeadersStr.split(',') : null;

            var colDefs = [];
            for (var c = 0; c < cols; c++) {
                var def = { width: 80 };
                if (colHeaders && c < colHeaders.length) {
                    def.title = colHeaders[c];
                }
                colDefs.push(def);
            }

            var worksheetConfig = {
                data: data,
                minDimensions: [cols, rows],
                columns: colDefs,
                tableOverflow: true,
                tableWidth: Math.min(cols * 85 + 50, 600) + 'px',
                tableHeight: Math.min(rows * 28 + 30, 400) + 'px'
            };

            // Apply custom row headers if provided
            if (rowHeaders) {
                worksheetConfig.rows = {};
                for (var r = 0; r < rowHeaders.length; r++) {
                    worksheetConfig.rows[r] = { title: rowHeaders[r] };
                }
            }

            var grid = jspreadsheet(container, {
                worksheets: [worksheetConfig]
            });

            // In v5, jspreadsheet() returns an array of worksheet instances
            var worksheet = Array.isArray(grid) && grid.length > 0 ? grid[0] : grid;
            container._grid = worksheet;

            container.addEventListener('mouseup', function() {
                setTimeout(function() { emitGridChange(); }, 50);
            });

            // Use MutationObserver to detect cell edits
            var observer = new MutationObserver(function() {
                emitGridChange();
            });
            var tbody = container.querySelector('tbody');
            if (tbody) {
                observer.observe(tbody, { childList: true, subtree: true, characterData: true });
            }

            // Also listen for blur on td elements (cell edit complete)
            container.addEventListener('focusout', function(e) {
                if (e.target && e.target.tagName === 'TD') {
                    setTimeout(function() { emitGridChange(); }, 50);
                }
            });

            function emitGridChange() {
                var gridData = worksheet.getData ? worksheet.getData() : [];
                var calcpadValue;
                if (rows === 1) {
                    // Vector: [1; 2; 3] — single row, ; joins elements
                    calcpadValue = '[' + (gridData[0] || []).join('; ') + ']';
                } else {
                    // Matrix: [1;2;3 | 4;5;6] — ; joins elements in a row, | joins rows
                    calcpadValue = '[' + gridData.map(function(r) { return r.join(';'); }).join(' | ') + ']';
                }
                sendMsg('uiValueChange', varName, calcpadValue, sourceLine);
            }
        });
    }
})();
</script>`;
}
