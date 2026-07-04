# To-Do

> Hosted/Docker/multi-user/auth/S3 deployment work has moved to the `calcpad-experimental` branch. This branch is localhost-only; items below are scoped to the local-mode runtime.

## General

-   Add error logging that passes Calcpad errors to the frontend even if they aren't visible in the webview
-   Audit Calcpad.Core for shared mutable state that races under concurrent local requests. Known offender: `MacroParser.Macros` is a `static` dictionary cleared and repopulated per `Parse(..., includeLine == 0)` call, so two simultaneous requests stomp each other. Less acute in single-user local mode but still a correctness bug if two editor panes hit the server concurrently.

### To Test/Review

-   Add HTML table styling when a table is called in the code
-   Update documentation for new features
-   Get desktop version usable so new features can be tested and vs code can be used as a fallback.
-   Add better documentation for using vs code features

## calcpad-frontend

-   Finish enhanced PDF generation
    -   Custom header/footers
    -   Custom svg backgrounds
    -   Custom fields with
    -   Add batch plot with running or restarting page numbers

## vscode-calcpad

### Enhancements

-   Add desc metadata for variables
-   Make a Vue panel that activates when the cursor is inside a JSON HTML comment. Where you can edit properties and then the line gets updated based on what you put into the UI.
-   Add the ability to focus the preview to the line selected in the code. Add a toggle to automatically sync this in the Vue panel.
-   Switch Vue tabs from text to icons now that they are getting longer.
-   Add quick typing for macros (~1 macro 1, ~2 macro 2, etc.). Add macro mapping to vscode config using json object {macroMapping:{"1": "macroName$", ...}}. Have VS code set cursor position to within () and before first param.
-   Publish to Open VSX but not Visual Studio Marketplace unless I need a personal Azure account for other reasons
-   Fix line links to use source line mapping - this may be fixed? If I run into it again, give test case
-   Add intermediate step that changes the display name of a variable by using find and replace on the HTML content. This will need some processing of complex characters, for example \_ turns into a subscript in the HTML. Read how Calcpad.Core handles this and reverse-engineer it. This can use a displayName property in an HTML comment above the variable definition line. Alternatively, have a variable mapping object as part of the settings and have it change the variable name at the HTML output step in Calcpad.Core.

### Bugs

### Testing

-   Make refresh button check if the server is down and restart it if it crashes. See if it is possible to put the server in a try/catch loop and send the crash message to the client output. Have this on 3 retries before requiring a manual refresh and give the user a pop-up so they know it crashed due to something in their file.
-   Fix base64 syntax highlighting by sending the last characters that close the img tag.
-   Add button that auto-downloads the .NET runtime into the extension folder with slim build options if the user doesn't want to install the .NET runtimes locally
-   Make sure long API calls are awaited and do not freeze the UI
-   Check if the cache needs cleared on intervals
-   Have variables use the tokenizer for recognition when you highlight text when clicking, commas are not handled properly currently.
-   UI state persists across code changes where possible.
-   Add docstrings for built-in functions using the same hover option. Build this into the snippet provider
-   Add prettify command that does the same thing as eat space and auto-indenting. However, the automatic behavior is not preferred as it caused various bugs in Wpf.
-   Have variables use the tokenizer for recognition when you highlight text when clicking, commas are not handled properly currently.

## Calcpad.Web

-   Write/Append: prompt a download (ZIP for multiple files) when running under the Linux build instead of writing to the local filepath. Add a setting in Calcpad.Core to control whether write/append output is cached for download or applied directly to disk.
-   Add support for other languages, especially Chinese as there is a large Chinese community.
-   Add #UI features to main branch
-   Make update do var name is not default #UI id, but {varName:varRepeatNumber} is the id to handle repetitions of the same variable name.

> Hosted-mode items (DDoS hardening, file-size/rate limits, CalcpadAuth SSO, S3 backend, Docker config, OAuth, Cloudflare tunnel, `<service:endpoint>` routing, token management) live on `calcpad-experimental`.

### Bugs

### Testing

-   Test snippet updates

## Calcpad.Web Desktop App

-   Have `calcpad-web` (pure browser build) mirror the desktop settings scheme in localStorage — same JSON shape, active-config pointer, named configs — with export/import via browser file up/download. Not urgent; pure web build isn't the active target.
- switching settings dropdown in VS Code/calcpad-desktop doesn't change active settings


## Calcpad.Highlighter

-   Order greek symbols by greek alphabet instead of english
-   Add linting for HTML/markdown mode
-   Add information check for re-defining a variable as a different type
-   There is an issue with tokenization of global variables in macro #def statements. You need to get global scopes and properly tokenize them if it gets defined later in the file.
-   Improve HTML/JS/CSS/SVG tokenization to use JS library. Use the node instance in vscode (also this is only needed as a vs code feature).
-   Add HTML/JS/CSS/SVG linting using a JS library (also this is only needed as a vs code feature). This is done using the HTML preview, but I need to add a plugin config to make all of these work to the docs.
-   Add undefined macro linter check
-   Allow rename of global variables inside macro defs - this may be an undefined variable linter bug? But this should not run at stage 2 anyways.
-   Scan for #if/#else mismatches in macro definitions, and in general improve macro definition linting where possible.

### Testing

-   Make auto-indenting work inside macro definitions - this works in some cases but not others, prettifier should handle this.

### Bugs

-   Fix HTMLComment tokenization
-   Fix ' symbol tokenization in JS:  
    #def drawLine$(x1$; y1$; x2$; y2$)  
    '\<br/> 'if (window.dxfDrawing) { window.dxfDrawing.drawLine('x1$', 'y1$', 'x2$', 'y2$'); }\<br/> '  
    #end def
-   Fix tokenization of custom units

## Calcpad.Core

-   Add Unit safety for angles by having arc functions return the unit based on the default setting (instead of a number with no unit)
-   Add a way to throw custom errors in command block expressions
-   Allows undefined in inline loops. This is because undefined is the best way to work with jagged matrices. Have the graph either show a vertical asymtote or ignore undefined values.
-   See if Kelvin unit conversion can be made safer.
-   Add more imperial units for engineering (ksf, plf, etc.)
-   Add #hideRegion {cond} and #endHideRegion {cond} to replace hideC/unhideC
-   Add a way to throw errors that Calcpad.Core uses with a built-in function. Catch all errors including these in Calcpad.Core and pass them to the Vue panel with source code line references/external filepath.
-   Add inline unit convsersion function on a variable (instead of per-line)

### Bugs

-   Fix #varsub to work when using v.i = x.i + y.i, this currently treats as #nosub
-   Calcpad's parser doesn't handle .iζ indexing on inline-substituted expressions like first(row(...)) (it parses .iζ as a unit suffix).

### Testing

-   Test Excel remote URL reading.
-   Route write for the .cpd directory, not the pwd.

## Calcpad Modules

-   DXF: Is there a way to get a CDN like import for JS libraries using #include in calcpad? This version has a text fix that is not in the CDN version
-   DXF: Add lineweight by using an offset line in the rendered version and rounding the ends by adding the length equal to the offset.
-   Geometry: Map RFEM nodes to polylines, map RFEM polylines to surfaces. Use order of lines to determine axis and match RFEM. Use this to get pressures.
-   Geometry needs to be tabled beyond simple use cases until this can be written in C# and have results sent to Calcpad via API.

```
f(x) = $Repeat{1/i @ i = x : 10}
f(0)
```
