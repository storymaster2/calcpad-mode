# CalcPad editor session contract

Handoff for the **calcpad-mode** (calcpad-web fork) agent. Detail Library mints short-lived sessions; the editor opens via deep link, fetches the session, and loads the `.cpd` **read-only** for v1. Save-back is specified for Phase 2.

## Goal

Host the **calcpad-web fork** so Detail Library can `window.open` an editor URL. On load:

1. Fetch a library session document (includes `.cpd` source).
2. Open that file in the editor **read-only**.
3. Keep convert/lint via existing `?server=` Calcpad API.
4. Save UI may exist but must stay disabled / no-op while `save.allowed === false`.

## Query parameters (editor page)

| Param | Required (library open) | Meaning |
| --- | --- | --- |
| `librarySession` | yes | Opaque session token |
| `libraryApi` | yes | Detail-library API base, **no trailing slash** |
| `server` | existing | Calcpad convert API base (unchanged) |

Example URL minted by Detail Library:

```text
https://<calcpad-web-host>/?server=https://calcpad-server-….run.app&librarySession=TOKEN&libraryApi=https://detail-library-backend-….run.app
```

## Library APIs (already implemented)

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

TTL: **30 minutes**. Requires GitHub calc tip + `CALCPAD_EDITOR_BASE_URL`, `CALCPAD_API_BASE_URL`, `API_PUBLIC_BASE_URL` on the library backend.

### Load session (capability URL — no Google auth)

```http
GET /calc-sessions/:token
```

- **404** if missing or expired.
- `Cache-Control: no-store`.
- CORS: library `CORS_ORIGIN` must include the Calcpad **web** origin.

### Session document schema

```json
{
  "sessionId": "hex…",
  "expiresAt": "2026-07-21T22:00:00.000Z",
  "mode": "readonly",
  "itemKey": "AbCdEf1234",
  "title": "Beam Seat",
  "filename": "beam-seat--a1b2c3d4.cpd",
  "repoPath": "calcs/beam-seat--a1b2c3d4.cpd",
  "tipCommitSha": "abc123…",
  "content": "' full .cpd source\n…",
  "libraryApi": "https://detail-library-backend-….run.app",
  "save": {
    "allowed": false,
    "requiresCommitMessage": true,
    "uploadMethod": "PUT",
    "uploadPath": "/calc-sessions/{sessionId}"
  }
}
```

- `mode` is `"readonly"` when `save.allowed` is false; later `"readwrite"` when save is enabled.
- Replace `{sessionId}` in `uploadPath` with `session.sessionId` (or use the literal path `/calc-sessions/<sessionId>`).

## On boot (when `librarySession` is present)

1. `GET {libraryApi}/calc-sessions/{librarySession}`.
2. If non-OK: show a clear full-page error (expired/missing). Do **not** open an empty editor as success.
3. Parse the session JSON.
4. `tabs.openFile(session.filename, session.content)` (or equivalent).
5. If `session.mode === "readonly"` or `!session.save.allowed`: Monaco `readOnly: true`; disable/hide Save-to-library.
6. Keep convert preview working via existing `server` API.
7. Optional banner: `Opened from Detail Library: {title} (read-only)`.

## CORS / origins

Detail Library will allow the Calcpad web origin via `CORS_ORIGIN` (comma-separated). Document exact origins used in:

- **Dev** (e.g. `http://localhost:5174`)
- **Prod** (e.g. `https://calcpad.modearchitecture.com`)

so library env can be updated.

## Save (Phase 2 — not implemented on library yet)

When `session.save.allowed === true`:

```http
PUT {libraryApi}/calc-sessions/{sessionId}
Content-Type: application/json

{
  "content": "<current editor buffer>",
  "commitMessage": "<non-empty trim>"
}
```

- Capability auth via session token (same as GET). No Google GIS on the Calcpad origin.
- Library will reject expired sessions and sessions minted with `save.allowed: false` (**403**).
- Expected success shape (planned): `{ "tipCommitSha": "…", "repoPath": "…" }`.
- On success: clear dirty state; update local tip SHA if returned.

Calcpad may stub a Save control behind `save.allowed` now.

## Out of scope for calcpad-mode agent

- GitHub access / PAT
- Minting sessions
- Changing convert API authentication

## Acceptance tests (calcpad)

1. Open a library-minted `editorUrl` → file appears in editor; preview renders via `server`.
2. Buffer is not editable (read-only).
3. Expired/invalid token → error page (not a fake empty doc).
4. Opening the editor **without** `librarySession` still works (blank / file picker).

## Acceptance tests (detail-library — already required)

1. With GitHub + editor env set, **Open** on a card with `hasCalc` opens a tab whose URL contains `librarySession` and `libraryApi`.
2. `GET /calc-sessions/:token` returns content matching the downloaded `.cpd`.
3. Unknown/expired token → **404**.
4. Unauthenticated `GET` works; unauthenticated `POST …/sessions` → **401** when auth is enabled.
