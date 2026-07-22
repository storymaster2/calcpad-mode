# CalcPad complementary workspace sessions

Handoff for the **calcpad-mode** (calcpad-web fork) agent. Detail Library mints a **long-lived capability session** (opaque token URL). The editor owns rich UI (versions, save, future canonical); the library owns GitHub + identity.

Sessions support the “buried tab for a week with local edits, then save” workflow via a **90-day sliding TTL** and explicit renew.

## Goal

1. Boot from `librarySession` + `libraryApi` (and existing `server` for convert).
2. Load tip `.cpd` into the editor; allow edits when `save.allowed`.
3. Optionally show the detail thumbnail from `thumbnailUrl`.
4. Versions panel via history + content-by-ref; mark the **canonical** commit.
5. Renew on tab focus / periodically / immediately before save.
6. Save with commit message, tip conflict detection, and optional provenance flags (`basedOnCommitSha`, `updateKind`).

Calcpad-mode UI is built in the editor repo; this document is the API contract.

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
| Sliding renew | Successful workspace calls (GET session, renew, history, content, thumbnail, PUT save) set `expires_at = now() + TTL` |
| Explicit renew | `POST /calc-sessions/:token/renew` → `{ sessionId, expiresAt }` (**404** if purged) |
| Purge | Lazy delete when `expires_at < now()` |
| Buried-tab save | Keep buffer in memory/sessionStorage; call renew before PUT; if **404**, show “Re-open from Detail Library” (session row gone). Local buffer can still be downloaded. |

The browser buffer is the source of truth for unsaved edits. The session row is the **capability** to read history and write tip.

## Auth

- **Mint** `POST /items/:itemKey/calc/sessions` — Google / internal API key (same as rest of library).
- **Workspace** `GET|POST|PUT /calc-sessions/:token…` — **no Google auth**; the token is the secret. Include the Calcpad **web** origin in library `CORS_ORIGIN`.

Do **not** use library `/thumbnails/…` from the editor when auth is on; use the session capability thumbnail URL below.

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
  "canonicalCommitSha": "abc123…",
  "thumbnailUrl": "/calc-sessions/hex…/thumbnail",
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
- `canonicalCommitSha` is the commit the library considers **canonical** for this calc. **Today it equals tip**; later it may diverge. Use it (and history `isCanonical`) to badge the canonical version in the UI.
- `thumbnailUrl` is a capability path (or `null` if the detail has no thumbnail). Fetch as `{libraryApi}{thumbnailUrl}` for optional chrome/header display. Ignore null or failed loads.
- Path fields may already include the concrete `sessionId` (prefer response values over templates).

### Thumbnail

```http
GET /calc-sessions/:token/thumbnail
```

- Image bytes (`Content-Type` image/* or svg).
- `Cache-Control: private, max-age=300`.
- Slides TTL.
- **404** if session expired or thumbnail missing.

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
  "canonicalCommitSha": "abc123…",
  "commits": [
    {
      "sha": "…",
      "message": "…",
      "date": "…",
      "isTip": true,
      "isCanonical": true
    }
  ]
}
```

- Badge `isCanonical` in the versions panel (today same as `isTip`; keep both so the UI stays correct when they diverge).
- Prefer at most one `isCanonical: true` when a canonical SHA exists.

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
  "force": false,
  "basedOnCommitSha": "<optional — commit this edit was based on>",
  "updateKind": "canonical"
}
```

| Field | Required | Meaning |
| --- | --- | --- |
| `baseTipCommitSha` | yes | Optimistic concurrency vs **current tip**. **409** if tip moved unless `force`. |
| `basedOnCommitSha` | no | Provenance: which commit the buffer was edited from (**tip by default**; set to an older SHA when the user started from history). Library validates when present; **not persisted yet**. |
| `updateKind` | no | `"canonical"` or `"branch"`. Intent for future canonical-pointer updates. Library accepts; **no server effect yet**. When UI exists, send explicitly; contract default if omitted is **canonical**. |
| `force` | no | Also `?force=true` — skip tip conflict check and commit anyway. |

| Status | Meaning |
| --- | --- |
| **200** | `{ tipCommitSha, canonicalCommitSha, repoPath, expiresAt }` — update local `baseTipCommitSha`; refresh history. Today `canonicalCommitSha === tipCommitSha`. |
| **400** | blank message / missing fields / invalid optional flags |
| **403** | `save.allowed` false |
| **404** | session gone |
| **409** | tip moved: `{ message, tipCommitSha, tipContent? }` — offer reload vs overwrite (`force`) |

## Editor responsibilities

1. Boot from `librarySession` + `libraryApi` as above.
2. Persist in `sessionStorage` (or memory): `{ sessionId, libraryApi, itemKey, baseTipCommitSha, basedOnCommitSha? }` so renew/save work after soft reloads.
3. Optional UI: load `{libraryApi}{thumbnailUrl}` when non-null.
4. Versions panel: `GET …/history`; mark `isCanonical` (and `isTip` if useful); jump via `GET …/content?ref=`.
5. When loading an older version into the buffer, remember that SHA as `basedOnCommitSha` for the next save.
6. On `visibilitychange` → visible / interval (e.g. daily) / **immediately before save**: `POST …/renew`.
7. Save: require commit message; `PUT` with `baseTipCommitSha`; include `basedOnCommitSha` (tip or historical) and `updateKind` when the UI can set them; on **409** show conflict UI; on **404** → “Re-open this calc from Detail Library”.
8. After save: update `baseTipCommitSha` (and treat new tip as based-on/canonical until history says otherwise); refresh history.
9. If `!save.allowed`: Monaco read-only; hide Save-to-library.

### Future behavior (reserved)

- `updateKind: "canonical"` will move the library’s canonical pointer to the new tip; `"branch"` will leave canonical unchanged while still advancing tip (or a branch model TBD).
- `basedOnCommitSha` may be recorded for audit / history UI.
- Until then, editors may send these fields; library ignores them for persistence.

## CORS / origins

Document exact Calcpad web origins for library `CORS_ORIGIN`:

- **Dev** (e.g. `http://localhost:5174`)
- **Prod** (e.g. `https://detail-library.modearchitecture.com` when hosted under `/calcpad/`)

## Out of scope for calcpad-mode agent

- GitHub access / PAT
- Minting sessions
- Persisting unsaved buffers on the library server
- Changing convert API authentication
- Implementing canonical-pointer persistence (library will do that in a later pass)

## Acceptance tests (calcpad)

1. Open a library-minted `editorUrl` → tip file loads; preview via `server`.
2. When `save.allowed`, buffer is editable; Save requires commit message.
3. When `thumbnailUrl` is set, optional image loads from the capability URL.
4. Versions: history lists commits; exactly one `isCanonical` when tip exists; opening an older `ref` loads different content when history has 2+ commits.
5. Renew before save / on focus extends `expiresAt`.
6. Wrong `baseTipCommitSha` → conflict UI; correct → new tip; update stored base SHA. Optional `basedOnCommitSha` / `updateKind` do not break save.
7. Expired/missing token → error (“Re-open from Detail Library”); do not treat as blank success.
8. Opening the editor **without** `librarySession` still works (blank / file picker).

## Acceptance tests (detail-library)

1. Mint → history returns ≥1 commit; `canonicalCommitSha` present; tip row has `isCanonical: true`.
2. `content?ref=` older sha returns different body than tip when history has 2+ commits.
3. `GET …/thumbnail` works without Google auth with a valid token; **404** when missing.
4. Renew extends `expiresAt`.
5. PUT with wrong `baseTipCommitSha` → **409**; with correct → new tip + `canonicalCommitSha` in response.
6. PUT with `basedOnCommitSha` + `updateKind` still succeeds (flags validated, not applied yet).
7. Unauthenticated workspace routes work; mint still auth-gated.
8. **Open** on a card with `hasCalc` opens a tab whose URL contains `librarySession` and `libraryApi`.
