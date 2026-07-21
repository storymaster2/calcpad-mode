/**
 * Detail Library session handoff (calcpad-web only).
 *
 * Library CORS_ORIGIN must include this app's origin, e.g.:
 *   - Dev:  http://localhost:5173
 *   - Prod: https://detail-library.modearchitecture.com
 *     (SPA is served at /calcpad/ on that host; CORS uses the origin only)
 *
 * Spec: docs/calcpad-editor-session-contract.md
 */

export interface LibrarySessionSave {
    allowed: boolean;
    requiresCommitMessage: boolean;
    uploadMethod: string;
    uploadPath: string;
}

export interface LibrarySessionDocument {
    sessionId: string;
    expiresAt: string;
    mode: 'readonly' | 'readwrite' | string;
    itemKey: string;
    title: string;
    filename: string;
    repoPath: string;
    tipCommitSha: string;
    content: string;
    libraryApi: string;
    save: LibrarySessionSave;
}

export interface LibraryQueryParams {
    librarySession: string;
    libraryApi: string;
}

export class LibrarySessionError extends Error {
    constructor(
        message: string,
        public readonly kind: 'not_found' | 'network' | 'invalid',
        public readonly status?: number,
    ) {
        super(message);
        this.name = 'LibrarySessionError';
    }
}

/** Require both librarySession and libraryApi; trim trailing slash on API base. */
export function parseLibraryQuery(search: string = window.location.search): LibraryQueryParams | null {
    const params = new URLSearchParams(search);
    const librarySession = params.get('librarySession')?.trim() ?? '';
    let libraryApi = params.get('libraryApi')?.trim() ?? '';
    if (!librarySession || !libraryApi) return null;
    libraryApi = libraryApi.replace(/\/+$/, '');
    return { librarySession, libraryApi };
}

export async function fetchLibrarySession(
    libraryApi: string,
    token: string,
): Promise<LibrarySessionDocument> {
    const base = libraryApi.replace(/\/+$/, '');
    const url = `${base}/calc-sessions/${encodeURIComponent(token)}`;

    let response: Response;
    try {
        response = await fetch(url, {
            method: 'GET',
            cache: 'no-store',
            headers: { Accept: 'application/json' },
        });
    } catch (err) {
        const detail = err instanceof Error ? err.message : String(err);
        throw new LibrarySessionError(
            `Could not reach Detail Library (${detail}).`,
            'network',
        );
    }

    if (response.status === 404) {
        throw new LibrarySessionError(
            'This library session is missing or has expired. Return to Detail Library and open the calculation again.',
            'not_found',
            404,
        );
    }

    if (!response.ok) {
        throw new LibrarySessionError(
            `Detail Library returned HTTP ${response.status}.`,
            'invalid',
            response.status,
        );
    }

    let data: unknown;
    try {
        data = await response.json();
    } catch {
        throw new LibrarySessionError('Session response was not valid JSON.', 'invalid');
    }

    if (!isSessionDocument(data)) {
        throw new LibrarySessionError('Session response was missing required fields.', 'invalid');
    }

    return data;
}

function isSessionDocument(value: unknown): value is LibrarySessionDocument {
    if (!value || typeof value !== 'object') return false;
    const o = value as Record<string, unknown>;
    return (
        typeof o.sessionId === 'string' &&
        typeof o.content === 'string' &&
        typeof o.filename === 'string' &&
        typeof o.title === 'string' &&
        o.save !== null &&
        typeof o.save === 'object' &&
        typeof (o.save as LibrarySessionSave).allowed === 'boolean'
    );
}

export function isSessionReadOnly(session: LibrarySessionDocument): boolean {
    return session.mode === 'readonly' || !session.save.allowed;
}
