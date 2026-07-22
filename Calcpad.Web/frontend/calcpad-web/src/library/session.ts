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
    requiresBaseTipCommitSha?: boolean;
}

export interface LibrarySessionVersions {
    historyPath: string;
    contentPath: string;
}

export interface LibrarySessionWorkspace {
    renewPath: string;
    renewMethod: string;
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
    baseTipCommitSha?: string;
    content: string;
    libraryApi: string;
    libraryDetailHint?: { itemKey: string };
    versions?: LibrarySessionVersions;
    workspace?: LibrarySessionWorkspace;
    save: LibrarySessionSave;
}

export interface LibraryQueryParams {
    librarySession: string;
    libraryApi: string;
}

export interface HistoryCommit {
    sha: string;
    message: string;
    date: string;
    isTip: boolean;
}

export interface HistoryResponse {
    expiresAt: string;
    commits: HistoryCommit[];
}

export interface ContentByRefResponse {
    expiresAt: string;
    sha: string;
    filename: string;
    content: string;
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

export function getActiveLibrarySession(): LibrarySessionDocument | null {
    return (window as unknown as { calcpadLibrarySession?: LibrarySessionDocument }).calcpadLibrarySession
        ?? null;
}

export function setActiveLibrarySession(session: LibrarySessionDocument | null): void {
    (window as unknown as { calcpadLibrarySession?: LibrarySessionDocument | null }).calcpadLibrarySession = session;
}

function resolvePathTemplate(path: string, sessionId: string): string {
    return path.replace(/\{sessionId\}/g, encodeURIComponent(sessionId));
}

function absoluteUrl(libraryApi: string, pathOrUrl: string): string {
    if (/^https?:\/\//i.test(pathOrUrl)) return pathOrUrl;
    const base = libraryApi.replace(/\/+$/, '');
    const path = pathOrUrl.startsWith('/') ? pathOrUrl : `/${pathOrUrl}`;
    return `${base}${path}`;
}

export function historyUrl(session: LibrarySessionDocument, limit = 50): string {
    const raw = session.versions?.historyPath
        ? resolvePathTemplate(session.versions.historyPath, session.sessionId)
        : `/calc-sessions/${encodeURIComponent(session.sessionId)}/history`;
    const url = new URL(absoluteUrl(session.libraryApi, raw));
    url.searchParams.set('limit', String(limit));
    return url.toString();
}

export function contentUrl(session: LibrarySessionDocument, ref?: string): string {
    const raw = session.versions?.contentPath
        ? resolvePathTemplate(session.versions.contentPath, session.sessionId)
        : `/calc-sessions/${encodeURIComponent(session.sessionId)}/content`;
    const url = new URL(absoluteUrl(session.libraryApi, raw));
    if (ref) url.searchParams.set('ref', ref);
    return url.toString();
}

async function libraryFetch(url: string): Promise<Response> {
    try {
        return await fetch(url, {
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
}

function throwForStatus(response: Response, context: string): void {
    if (response.status === 404) {
        throw new LibrarySessionError(
            'This library session is missing or has expired. Return to Detail Library and open the calculation again.',
            'not_found',
            404,
        );
    }
    if (!response.ok) {
        throw new LibrarySessionError(
            `${context}: Detail Library returned HTTP ${response.status}.`,
            'invalid',
            response.status,
        );
    }
}

export async function fetchLibrarySession(
    libraryApi: string,
    token: string,
): Promise<LibrarySessionDocument> {
    const base = libraryApi.replace(/\/+$/, '');
    const url = `${base}/calc-sessions/${encodeURIComponent(token)}`;
    const response = await libraryFetch(url);
    throwForStatus(response, 'Session');

    let data: unknown;
    try {
        data = await response.json();
    } catch {
        throw new LibrarySessionError('Session response was not valid JSON.', 'invalid');
    }

    if (!isSessionDocument(data)) {
        throw new LibrarySessionError('Session response was missing required fields.', 'invalid');
    }

    // Normalize libraryApi onto the document for later absolute URLs.
    if (!data.libraryApi) {
        data = { ...data, libraryApi: base };
    } else {
        data = { ...data, libraryApi: data.libraryApi.replace(/\/+$/, '') };
    }

    return data as LibrarySessionDocument;
}

export async function fetchHistory(
    session: LibrarySessionDocument,
    limit = 50,
): Promise<HistoryResponse> {
    const response = await libraryFetch(historyUrl(session, limit));
    throwForStatus(response, 'History');

    let data: unknown;
    try {
        data = await response.json();
    } catch {
        throw new LibrarySessionError('History response was not valid JSON.', 'invalid');
    }

    if (!data || typeof data !== 'object' || !Array.isArray((data as HistoryResponse).commits)) {
        throw new LibrarySessionError('History response was missing commits.', 'invalid');
    }

    return data as HistoryResponse;
}

export async function fetchContentByRef(
    session: LibrarySessionDocument,
    sha?: string,
): Promise<ContentByRefResponse> {
    const response = await libraryFetch(contentUrl(session, sha));
    throwForStatus(response, 'Content');

    let data: unknown;
    try {
        data = await response.json();
    } catch {
        throw new LibrarySessionError('Content response was not valid JSON.', 'invalid');
    }

    const o = data as ContentByRefResponse;
    if (!o || typeof o.content !== 'string' || typeof o.sha !== 'string') {
        throw new LibrarySessionError('Content response was missing required fields.', 'invalid');
    }

    return o;
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
