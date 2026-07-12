import type { PlotPayload } from '../types/api';

/**
 * Rendered plot decoded from the `/api/calcpad/plots` endpoint. `bytes` is
 * the raw image payload (PNG bytes for raster, UTF-8 SVG text for vector).
 * `dataUri` is what preview thumbnails reference.
 */
export interface ExtractedPlot {
    index: number;
    ext: 'png' | 'svg';
    mime: 'image/png' | 'image/svg+xml';
    bytes: Uint8Array;
    dataUri: string;
}

export function decodePlotPayload(payloads: PlotPayload[]): ExtractedPlot[] {
    return payloads.map((p, index) => {
        const bytes = base64ToBytes(p.base64);
        if (p.format === 'svg') {
            const svgText = new TextDecoder().decode(bytes);
            return {
                index,
                ext: 'svg',
                mime: 'image/svg+xml',
                bytes,
                dataUri: 'data:image/svg+xml;utf8,' + encodeURIComponent(svgText),
            };
        }
        return {
            index,
            ext: 'png',
            mime: 'image/png',
            bytes,
            dataUri: 'data:image/png;base64,' + p.base64,
        };
    });
}

function base64ToBytes(b64: string): Uint8Array {
    const bin = atob(b64);
    const out = new Uint8Array(bin.length);
    for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
    return out;
}
