# CalcPad complementary workspace sessions

Handoff for the **calcpad-mode** (calcpad-web fork) agent. Detail Library mints a **long-lived capability session** (opaque token URL). The editor owns rich UI (versions, save, future canonical); the library owns GitHub + identity.

Sessions support the “buried tab for a week with local edits, then save” workflow via a **90-day sliding TTL** and explicit renew.

## Goal

1. Boot from `librarySession` + `libraryApi` (and existing `server` for convert).
2. Load tip `.cpd` into the editor; allow edits when `save.allowed`.
3. Versions panel via history + content-by-ref.
4. Renew on tab focus / periodically / immediately before save.
5. Save with commit message + tip conflict detection.

Calcpad-mode UI is built in the editor repo; this document is the API contract.

## Phased delivery

| Phase | Editor ships | Library must support |
| --- | --- | --- |
| **A (current)** | Boot tip; Versions panel (`history` + `content?ref=`); non-tip views are read-only | `GET` session, history, content-by-ref |
| **B (later)** | Renew on focus / before save; Save-to-library + `baseTipCommitSha` / **409** UI; sessionStorage capability keys | renew + PUT save as below |

Phase A acceptance (calcpad): history lists commits (ISO-8601 `date`, display locally); selecting an older `ref` changes the buffer; selecting tip/`isTip` restores tip content and editability when `save.allowed`.

Prefer concrete `versions.historyPath` / `versions.contentPath` from the session document when present; otherwise fall back to `/calc-sessions/{sessionId}/history` and `/calc-sessions/{sessionId}/content`.

## Query parameters (editor page)

| Param | Required (library open) | Meaning |
| --- | --- | --- |
| `librarySession` | yes | Opaque session token (`sessionId`) |
| `libraryApi` | yes | Detail-library API base, **no trailing slash** |
| `server` | existing | Calcpad convert API base (unchanged) |

Example URL minted by Detail Library:

```text
https://<calcpad-web-host>/?server=https://calcpad-server-….run.app&librarySession=TOKEN&libraryApi=https://detail-library-backend-….run.app
```

## Session longevity

| Mechanism | Behavior |
| --- | --- |
| Initial TTL | **90 days** from mint (`CALCPAD_SESSION_TTL_DAYS`, default 90) |
| Sliding renew | Successful workspace calls (GET session, renew, history, content, PUT save) set `expires_at = now() + TTL` |
| Explicit renew | `POST /calc-sessions/:token/renew` → `{ sessionId, expiresAt }` (**404** if purged) |
| Purge | Lazy delete when `expires_at < now()` |
| Buried-tab save | Keep buffer in memory/sessionStorage; call renew before PUT; if **404**, show “Re-open from Detail Library” (session row gone). Local buffer can still be downloaded. |

The browser buffer is the source of truth for unsaved edits. The session row is the **capability** to read history and write tip.

## Auth

- **Mint** `POST /items/:itemKey/calc/sessions` — Google / internal API key (same as rest of library).
- **Workspace** `GET|POST|PUT /calc-sessions/:token…` — **no Google auth**; the token is the secret. Include the Calcpad **web** origin in library `CORS_ORIGIN`.

## Library APIs

### Mint (authenticated)

```http
POST /items/:itemKey/calc/sessions
Authorization: Bearer <Google ID token>   # or X-Internal-Api-Key
```

Response:

```json
{
  "sessionId": "<token>",
  "expiresAt": "<ISO-8601>",
  "editorUrl": "https://…"
}
```

Requires GitHub calc tip + `CALCPAD_EDITOR_BASE_URL`, `CALCPAD_API_BASE_URL`, `API_PUBLIC_BASE_URL`.

Mint sets `save.allowed` from `CALCPAD_SESSION_SAVE_ALLOWED` (default **true**). Set env to `false` for read-only mint.

### Session document

```http
GET /calc-sessions/:token
```

- **404** if missing or expired.
- `Cache-Control: no-store`.
- Slides TTL.

```json
{
  "sessionId": "hex…",
  "expiresAt": "2026-10-19T22:00:00.000Z",
  "mode": "readwrite",
  "itemKey": "AbCdEf1234",
  "title": "Beam Seat",
  "filename": "beam-seat--a1b2c3d4.cpd",
  "repoPath": "calcs/beam-seat--a1b2c3d4.cpd",
  "tipCommitSha": "abc123…",
  "baseTipCommitSha": "abc123…",
  "content": "' full .cpd source\n…",
  "libraryApi": "https://detail-library-backend-….run.app",
  "libraryDetailHint": { "itemKey": "AbCdEf1234" },
  "versions": {
    "historyPath": "/calc-sessions/{sessionId}/history",
    "contentPath": "/calc-sessions/{sessionId}/content"
  },
  "workspace": {
    "renewPath": "/calc-sessions/{sessionId}/renew",
    "renewMethod": "POST"
  },
  "save": {
    "allowed": true,
    "requiresCommitMessage": true,
    "uploadMethod": "PUT",
    "uploadPath": "/calc-sessions/{sessionId}",
    "requiresBaseTipCommitSha": true
  }
}
```

- `mode` is `"readwrite"` when `save.allowed`; otherwise `"readonly"`.
- Path fields may already include the concrete `sessionId` (prefer response values over templates).

### Renew

```http
POST /calc-sessions/:token/renew
```

```json
{ "sessionId": "…", "expiresAt": "…" }
```

### History

```http
GET /calc-sessions/:token/history?limit=50
```

```json
{
  "expiresAt": "…",
  "commits": [
    { "sha": "…", "message": "…", "date": "…", "isTip": true }
  ]
}
```

`date` is **ISO-8601**; the editor formats it in the local timezone for the Versions list.

### Content by ref

```http
GET /calc-sessions/:token/content?ref=<commitSha>
```

Omit `ref` for tip. Response:

```json
{ "expiresAt": "…", "sha": "…", "filename": "….cpd", "content": "…" }
```

### Save

```http
PUT /calc-sessions/:token
Content-Type: application/json

{
  "content": "<current editor buffer>",
  "commitMessage": "<non-empty trim>",
  "baseTipCommitSha": "<sha editor believes is tip>",
  "force": false
}
```

Optional: `?force=true` or `"force": true` — skip tip conflict check and commit anyway.

| Status | Meaning |
| --- | --- |
| **200** | `{ tipCommitSha, repoPath, expiresAt }` — update local `baseTipCommitSha`; refresh history |
| **400** | blank message / missing fields |
| **403** | `save.allowed` false |
| **404** | session gone |
| **409** | tip moved: `{ message, tipCommitSha, tipContent? }` — offer reload vs overwrite (`force`) |

## Editor responsibilities

1. Boot from `librarySession` + `libraryApi` as above.
2. Persist in `sessionStorage` (or memory): `{ sessionId, libraryApi, itemKey, baseTipCommitSha }` so renew/save work after soft reloads.
3. Versions panel: `GET …/history`, jump via `GET …/content?ref=`.
4. On `visibilitychange` → visible / interval (e.g. daily) / **immediately before save**: `POST …/renew`.
5. Save: require commit message; `PUT` with `baseTipCommitSha`; on **409** show conflict UI; on **404** → “Re-open this calc from Detail Library”.
6. After save: update `baseTipCommitSha` from response; refresh history.
7. If `!save.allowed`: Monaco read-only; hide Save-to-library.

### Future extension points (not built)

Reserved in contract only — do not invent APIs yet:

- `canonicalPath` / choose canonical commit
- Cross-detail search / list other details from the editor (`workspace` links later)

## CORS / origins

Document exact Calcpad web origins for library `CORS_ORIGIN` (origin only; path is not part of CORS):

- **Dev:** `http://localhost:5173` (Vite default; adjust if the port differs)
- **Prod:** `https://detail-library.modearchitecture.com` (SPA hosted at `/calcpad/` on that host)

## Out of scope for calcpad-mode agent

- GitHub access / PAT
- Minting sessions
- Persisting unsaved buffers on the library server
- Changing convert API authentication

## Acceptance tests (calcpad)

1. Open a library-minted `editorUrl` → tip file loads; preview via `server`.
2. When `save.allowed`, buffer is editable; Save requires commit message.
3. Versions: history lists commits; opening an older `ref` loads different content when history has 2+ commits.
4. Renew before save / on focus extends `expiresAt`.
5. Wrong `baseTipCommitSha` → conflict UI; correct → new tip; update stored base SHA.
6. Expired/missing token → error (“Re-open from Detail Library”); do not treat as blank success.
7. Opening the editor **without** `librarySession` still works (blank / file picker).

## Acceptance tests (detail-library)

1. Mint → history returns ≥1 commit for a known tip.
2. `content?ref=` older sha returns different body than tip when history has 2+ commits.
3. Renew extends `expiresAt`.
4. PUT with wrong `baseTipCommitSha` → **409**; with correct → new tip.
5. Unauthenticated workspace routes work; mint still auth-gated.
6. **Open** on a card with `hasCalc` opens a tab whose URL contains `librarySession` and `libraryApi`.
