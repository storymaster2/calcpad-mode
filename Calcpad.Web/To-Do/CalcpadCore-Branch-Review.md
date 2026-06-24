f# Calcpad.Core review — `calcpad-web` vs `main`

Findings from auditing the Calcpad.Core diff between `calcpad-web` and `origin/main`.
The three highest-impact items (stale disk cache, `_includeStack` case sensitivity, and disk-cache write duplication) are tracked in the active session to-do list. The rest are documented here.

## Bugs / correctness

### ~~`globalAssignment` length mismatch in variable-substitution rendering~~ — accepted as-is
[MathParser.Output.cs:88-93](../../Calcpad.Core/Parsers/MathParser/MathParser.Output.cs#L88-L93) was left unchanged. Decision: every existing writer renders `=` and `←` at the same width, so the implicit-length coupling is acceptable in exchange for less code. Revisit if a new writer breaks the width parity.

### Static `Macros` dictionary in MacroParser races under server load
Moved up to the main [To-Do.md](To-Do.md) under "Remaining before deployment" — needs a broader audit of shared mutable state in Calcpad.Core, not a one-line fix.

### ~~`Exceptions.CircularReference` message reads "function" — now used for macros~~ — resolved
Added `Messages.Circular_reference_detected_for_macro_0` and `Exceptions.CircularMacroReference`; MacroParser throws the macro-specific exception. English-only for now — `Messages.bg.resx` / `Messages.zh.resx` not updated yet.

### ~~Hardcoded English "Circular #include detected" string~~ — resolved
Added `Messages.Circular_include_detected_0` and switched `ParseInclude` to use `string.Format` against the resource. English-only for now.

### ~~`ClientFileCache` parallel arrays not length-validated~~ — resolved
Replaced by `List<Entry>` (private) in [Settings.cs](../../Calcpad.Core/Settings.cs). Length mismatch is unrepresentable.

### ~~Silent swallow on refetch failure~~ — resolved
The `catch` in [Settings.cs `TryGetBytes`](../../Calcpad.Core/Settings.cs#L58-L92) now records `ex.Message` on the entry's `Error` field and clears the `DiskGuid`. Subsequent `TryGetError` calls surface the actual failure (timeout, auth, etc.) instead of "file not in cache," and the entry stops retrying.

## Code duplication

### ~~Two `save/restore SourceFilePath -> Parse -> return` blocks in `ParseInclude`~~ — resolved
Extracted local helper `ParseWithSourcePath(string content, string newSourcePath)` in [MacroParser.cs](../../Calcpad.Core/Parsers/MacroParser.cs). Both call sites are now one-liners.

### ~~`Units["V"]` dictionary lookup in hot path~~ — resolved
`ElectricalUnits` is now a `FrozenDictionary<Unit, Unit>` with `kV → V` baked in; `GetElectricalUnit` is a single lookup-and-return matching `GetForceUnit`'s style.

## Minor / cosmetic

### ~~Trailing whitespace introduced in Unit.cs~~ — resolved
Stripped the trailing spaces on the two affected lines.

### ~~UTF-8 BOM removed from Unit.cs~~ — resolved
BOM restored so the file is consistent with the rest of the Calcpad.Core tree.

### ~~`ClientFileCache.AddEntry` rebuilds all four arrays on every call~~ — resolved
`AddEntry` now appends to the private `List<Entry>` instead of rebuilding arrays. O(1) amortized.

### ~~"kV-in-ElectricalUnits" dance needs a comment~~ — resolved
Restructured (see "`Units["V"]` dictionary lookup in hot path" above): the kV → V mapping is now expressed directly in the dictionary, no post-lookup fix-up.

## Ranking

1. **High-impact (resolved):** stale disk cache, `_includeStack` case sensitivity, disk-cache write duplication between Core and Web, `ClientFileCache` parallel-arrays / `AddEntry` rebuilds.
2. **Medium (resolved):** refetch error swallow, circular-reference message text — added `Circular_reference_detected_for_macro_0` and `Circular_include_detected_0` to `Messages.resx` (English-only; `bg`/`zh` still pending translation).
   - Static `Macros` race tracked separately in main [To-Do.md](To-Do.md).
4. **Accepted as-is:** `globalAssignment` length — the implicit width coupling is acceptable for the readability gain.
5. **Cleanup (resolved):** `GetElectricalUnit` / kV-dance refactor, `ParseWithSourcePath` extraction, Unit.cs trailing whitespace, Unit.cs BOM.
