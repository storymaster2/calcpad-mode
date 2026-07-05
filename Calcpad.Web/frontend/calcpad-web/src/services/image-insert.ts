/**
 * Browser-only image helpers (Clipboard API + FileReader). Pure/cross-platform
 * helpers live in `calcpad-frontend/services/image-utils`.
 */

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
