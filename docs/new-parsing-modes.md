# Parsing Modes and Display Directives

> Calcpad.Web only (web editor and VS Code extension). Not available in the WPF desktop application.

Calcpad supports explicit mode switching for how a document section is rendered, plus a set of visibility directives for controlling what appears in preview vs. calculation output.

## `#cpd` — Calcpad mode (default)

Switches the parser into standard Calcpad mode, where lines are calculations by default and any line prefixed with `'` is treated as HTML/text. This is the initial mode; use `#cpd` to return to default after a `#html` or `#markdown` section:

```text
#html
<h1>Report</h1>
<p>This entire section is passed through verbatim.</p>

#cpd
A = 5m
B = 3m
'Area = A * B
```

## `#html` — HTML mode

Every line in the block is treated as raw HTML and emitted directly, with no leading `'` required. Useful for inlining large HTML sections without the `'` prefix boilerplate:

```text
#html
<section class="intro">
  <h2>Introduction</h2>
  <p>Pure HTML here.</p>
</section>

#cpd
```

`#HTML` mode emits content verbatim — opening `<style>` / `<script>` tags and the closing tag pass through unchanged, and so does every line in between. Previously, every line that didn't start with `<` was wrapped in `<p>...</p>`, which silently broke `<style>` blocks (CSS rules became invalid `<p>` elements) and `<script>` blocks (each statement got wrapped). The new behavior makes these usable:

```text
#HTML
<style>
    .foo.bar { background-color: #aaa; }
</style>
<script>
    var hi = 5;
    console.log(hi);
</script>
#CPD
```

Combined with `string$(value)` (numeric → string) and the `+` string-concatenation operator, you can build dynamic JS payloads from Calcpad values:

```text
t = 123
#string script$ = '<script>var hi = ' + string$(t) + '; console.log(hi);</script>'
#HTML
script$
#CPD
```

This renders as `<script>var hi = 123; console.log(hi);</script>` in the output document.

## `#markdown` — Markdown mode

Every line is treated as Markdown and converted to HTML:

```text
#markdown
# Report Title
## Section 1
- Item 1
- Item 2

#cpd
```

## `#md on` / `#md off`

A lighter-weight toggle: inside Calcpad mode, `#md on` (or just `#md`) enables Markdown processing on subsequent `'`-prefixed comment lines; `#md off` disables it:

```text
#md on
'# This renders as a heading
'**bold text**

#md off
'# This stays literal
```

## Visibility directives

Several directives control what appears in each render pass. All are blocked inside `#html` and `#markdown` mode.

| Directive | Effect |
|-----------|--------|
| `#hide`   | Hide all subsequent output from rendering |
| `#show`   | Show all subsequent output (cancels `#hide`) |
| `#val`    | Show **values only**, not equations |
| `#equ`    | Show **equations** (default, cancels `#val`) |
| `#noc`    | Suppress all output (no calculation, no rendering) |

> **Not supported in Calcpad.Web:** the legacy `#pre`, `#post`, and `#input` directives. These were used in the WPF editor to gate sections by render pass. In Calcpad.Web, **NoPrint regions** (paired `'<!--{"NoPrintStart": true}-->` / `'<!--{"NoPrintEnd": true}-->` HTML comment markers) replace this behavior — the live preview always renders the full document, and the PDF export flow strips NoPrint regions before conversion. See [new-pdf-export.md](new-pdf-export.md#noprint-regions).

Example — typical usage in a report template:

```text
#hide
helper = some_intermediate_value

#show
#val
final_result = helper * 2     ' renders as "final_result = 42" only
```

## Text vs HTML token classification

Within Calcpad mode, a `'`-prefixed line is auto-classified:

- Line starts with `<` → tokenized as **Html** (emitted without `<p>` wrapping)
- Otherwise → tokenized as **Text** (wrapped in `<p>…</p>`)

This means `'<div>…</div>` is inlined as HTML, while `'My comment` becomes `<p>My comment</p>`.