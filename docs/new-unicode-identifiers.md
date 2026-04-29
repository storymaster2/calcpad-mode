# Unicode Diacritic Identifiers and Symbol Palette

> Calcpad.Web only (web editor and VS Code extension). Not available in the WPF desktop application.

The parser now treats four combining marks as identifier-continuation characters, so any base letter can carry a diacritic and remain a single variable name. The symbol palette adds eight new collapsible sections covering every Latin letter under each diacritic.

## Combining marks accepted in identifiers

| Mark | Codepoint | Example |
|------|-----------|---------|
| Acute accent (´) | U+0301 | `á`, `ń` |
| Macron / bar (¯) | U+0304 | `x̄`, `Z̄` |
| Dot above (˙) | U+0307 | `ẋ`, `θ̇` |
| Diaeresis (¨) | U+0308 | `z̈`, `ϕ̈` |

Combining marks are continuation-only — an identifier still has to begin with a base letter — so `̄x` is invalid but `x̄` is a valid variable name. This unblocks notation like `x̄`/`ȳ`/`z̄` for sample means and `ẋ`/`ẍ` for first/second time derivatives without per-letter precomposed enumeration.

## Symbol palette diacritic sections

The symbol palette generates 4 × 26 × 2 = **208 diacritic snippets**, organized into eight new categories:

- `Symbols/Bar Lowercase`, `Symbols/Bar Uppercase`
- `Symbols/Dot Lowercase`, `Symbols/Dot Uppercase`
- `Symbols/Double Dot Lowercase`, `Symbols/Double Dot Uppercase`
- `Symbols/Acute Accent Lowercase`, `Symbols/Acute Accent Uppercase`

Every glyph is emitted as **decomposed (base letter + combining mark)** for a single canonical form across the palette. This avoids cases where a precomposed `ā` and a decomposed `a + ̄` would resolve as two different variables in the lookup dictionary even though they look identical.

## Per-group collapsible palette UI

The Insert tab renders each symbol group as its own collapsible section with a disclosure arrow header. All groups — Greek Lowercase, Greek Uppercase, Special, and the eight new diacritic sections — start **collapsed by default**, keeping the palette compact. Users open only the groups they need.