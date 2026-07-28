import * as monaco from 'monaco-editor';
import { computeHeadingFoldRanges } from 'calcpad-frontend/services/headings';

/**
 * Register heading-based folding for the calcpad language (VS Code–like
 * section collapse until the next same-or-higher heading).
 */
export function registerHeadingFoldingProvider(): void {
    monaco.languages.registerFoldingRangeProvider('calcpad', {
        provideFoldingRanges(model) {
            const ranges = computeHeadingFoldRanges(model.getValue());
            return ranges.map(r => ({
                start: r.startLine,
                end: r.endLine,
                kind: monaco.languages.FoldingRangeKind.Region,
            }));
        },
    });
}
