# Table of Contents

> Available in **Calcpad.Web** (web editor and VS Code extension). The features in all `new-*.md` documents apply to Calcpad.Web only — not to the WPF desktop application.

Calcpad.Web documents can generate a navigable table of contents from the document headings, with automatic ID assignment and nested rendering.

## Heading comment syntax

Markdown-style heading comments use a leading `'` plus one to six `#` characters:

```text
'# Section 1
'Some content
'## Subsection 1.1
'More content
'### Deeper subsection
'# Section 2
```

These render as `<h1>`–`<h6>` HTML headings in the output.

## Automatic ID generation

`toc.js` walks all `<h1>`–`<h6>` elements on page load. For each heading without an `id`:

1. Snake-cases the heading text (lowercase, spaces → underscores)
2. Detects collisions and appends `_<n>` when necessary
3. Assigns the generated ID to the element

## Nested list rendering

Call `makeList({ target, parent })` from a `<script>` block to build a nested `<ul>` tree matching the heading levels:

```js
window.addEventListener('load', () => {
  makeList({ target: '#toc', parent: 'article' });
});
```

- Deeper heading levels become nested `<ul>` elements
- Same or shallower levels pop the stack to the correct parent
- Each entry becomes `<li><a href="#heading_id">Heading Text</a></li>`

## Example

[Calcpad.Cli/Examples/Demos/toc.cpd](../Calcpad.Cli/Examples/Demos/toc.cpd) demonstrates the full pattern including `#md on`, custom CSS, and the JS invocation.