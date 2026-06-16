/**
 * Read an image from the system clipboard via the standard Clipboard API and return
 * a `data:` URI suitable for an HTML `<img src>`. Returns null if the clipboard has
 * no image, the API isn't available, or permission was denied.
 */
export async function readImageFromClipboard(): Promise<string | null> {
    if (!navigator.clipboard?.read) return null;
    try {
        const items = await navigator.clipboard.read();
        for (const item of items) {
            for (const type of item.types) {
                if (!type.startsWith('image/')) continue;
                const blob = await item.getType(type);
                return await blobToDataUri(blob);
            }
        }
    } catch {
        // Permission denied or no image — fall through.
    }
    return null;
}

export function blobToDataUri(blob: Blob): Promise<string> {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result as string);
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(blob);
    });
}

export function bytesToBase64(bytes: ArrayBuffer | Uint8Array): string {
    const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    let binary = '';
    const chunkSize = 0x8000;
    for (let i = 0; i < view.length; i += chunkSize) {
        const chunk = view.subarray(i, Math.min(i + chunkSize, view.length));
        binary += String.fromCharCode.apply(null, Array.from(chunk));
    }
    return btoa(binary);
}

export function mimeFromExtension(filename: string): string {
    const ext = filename.toLowerCase().split('.').pop() ?? '';
    switch (ext) {
        case 'png':  return 'image/png';
        case 'jpg':
        case 'jpeg': return 'image/jpeg';
        case 'gif':  return 'image/gif';
        case 'webp': return 'image/webp';
        case 'svg':  return 'image/svg+xml';
        default:     return 'image/png';
    }
}

/** Build the CalcPad comment-line wrapping an image src — `'<img src="...">`. */
export function buildImageCommentLine(srcValue: string): string {
    return `'<img src="${srcValue}">`;
}
