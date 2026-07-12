# To-Do

> Hosted/Docker/multi-user/auth/S3 deployment work has moved to the `calcpad-experimental` branch. This branch is localhost-only; items below are scoped to the local-mode runtime.

## General

-   Audit Calcpad.Core for shared mutable state that races under concurrent local requests. Known offender: `MacroParser.Macros` is a `static` dictionary cleared and repopulated per `Parse(..., includeLine == 0)` call, so two simultaneous requests stomp each other. Less acute in single-user local mode but still a correctness bug if two editor panes hit the server concurrently.
-   Related: `Unit.IsUs` is another `static` that rebuilds the shared `Units` FrozenDictionary in place whenever it flips. `Unit.Get(name)` reads that same dictionary from every parser/calculator call site. `ExpressionParser.Parse` now writes `Unit.IsUs = Settings.IsUs` at the top of every parse, so concurrent requests with different values will race the same way `MacroParser.Macros` does. Proper fix: pre-build both `UnitsUK` and `UnitsUS` FrozenDictionaries once and change `Unit.Get(name)` to a Settings-aware lookup so nothing is mutated at request time.

### To Test/Review

-   Add HTML table styling when a table is called in the code
-   Update documentation for new features

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
-   Add intermediate step that changes the display name of a variable by using find and replace on the HTML content. This will need some processing of complex characters, for example \_ turns into a subscript in the HTML. Read how Calcpad.Core handles this and reverse-engineer it. This can use a displayName property in an HTML comment above the variable definition line. Alternatively, have a variable mapping object as part of the settings and have it change the variable name at the HTML output step in Calcpad.Core.

### Bugs

### Testing

-   Fix base64 syntax highlighting by sending the last characters that close the img tag.
-   Make sure long API calls are awaited and do not freeze the UI
-   Check if the cache needs cleared on intervals
-   Have variables use the tokenizer for recognition when you highlight text when clicking, commas are not handled properly currently.
-   UI state persists across code changes where possible.

## Calcpad.Web

-   Write/Append: prompt a download (ZIP for multiple files) when running under the Linux build instead of writing to the local filepath. Add a setting in Calcpad.Core to control whether write/append output is cached for download or applied directly to disk.
-   Add support for other languages, especially Chinese as there is a large Chinese community.
-   Add #UI features to main branch
-   Make update do var name is not default #UI id, but {varName:varRepeatNumber} is the id to handle repetitions of the same variable name.
-   An external browser dropdown could be helpful that allows switching the puppeteer and help button browser from the settings. I think this is doable if we store both the browser name and path in an object (then only show browsers with a valid path and allow adding a browser via file select to the exe or pasted path).
-   Submit removed jquery refactor as a PR to main.
-   Submit IsUS settings refactor as a PR to main.
-   Submit plot export Core refactor as a PR to main.
-   Add app version to settings Vue tab.

### Bugs

### Testing

-   Test snippet updates

## Calcpad.Web Desktop App

-   Make Prettify document put brackets at same line level, add spaces before and after operators, and add one space after line delimiters (and 0 spaces before ; delimiters):
    x2,seg = [38.667; 48; 66.667; 80.667; 90.667; 108.667; 118.667; _
            33.333; 40.333; 51.667; 75; 96.667; 108.333; 118.667; _
            23.333; 23.333; 23.333; _
            35; 35; 35; 35]*1ft
-   Make .cpd the default save extension
-   Associate .cpd filetype with CalcpadCE Web on install (is this possible with appimage? If not, wait until full deployment)

### Bugs


### Testing
-   error buttons don't jump to error line correctly when the error occurs in a loop. I think errors should get their own id that the buttons can get mapped to for scroll anchoring
-   create backup files of the current file state before a Tauri app crash in the project root directory. have this able to be opened via the files tab
-   add font selector to switch between JuliaMono and system default font (or any other fonts stored in the fonts folder). add button to open fonts folder.
-   add a max log length before it gets overwritten to help with performance
-   Fix Ctrl without click navigating to definition when the macro comes from an included file.

## Calcpad.Web Browser
-   Have `calcpad-web` (pure browser build) mirror the desktop settings scheme in localStorage — same JSON shape, active-config pointer, named configs — with export/import via browser file up/download. Not urgent; pure web build isn't the active target.
> Hosted-mode items (DDoS hardening, file-size/rate limits, CalcpadAuth SSO, S3 backend, Docker config, OAuth, Cloudflare tunnel, `<service:endpoint>` routing, token management) live on `calcpad-experimental`.

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
-   This code produces a bogus error:
    https://imartincei.github.io/CalcpadCE/examples/multiline-functions.html#fibonacci-numbers

## Calcpad.Core

-   Add Unit safety for angles by having arc functions return the unit based on the default setting (instead of a number with no unit)
-   Allows undefined in inline loops. This is because undefined is the best way to work with jagged matrices. Have the graph either show a vertical asymtote or ignore undefined values.
-   See if Kelvin unit conversion can be made safer.
-   Add more imperial units for engineering (ksf, plf, etc.)
-   Add #hideRegion {cond} and #endHideRegion {cond} to replace hideC/unhideC
-   Add a way to throw errors that Calcpad.Core uses with a built-in function.
-   Add inline unit convsersion function on a variable (instead of per-line)
-   Colors in unwrapped code don't match syntax highlighter

### Bugs

-   Fix #varsub to work when using v.i = x.i + y.i, this currently treats as #nosub
-   Calcpad's parser doesn't handle .iζ indexing on inline-substituted expressions like first(row(...)) (it parses .iζ as a unit suffix).


### Testing

-   Test Excel remote URL reading.
-   Route write for the .cpd directory, not the pwd.
-   Underlined line number line links for errors in unwrapped code do not route to source lines

## Calcpad Modules

-   DXF: Is there a way to get a CDN like import for JS libraries using #include in calcpad? This version has a text fix that is not in the CDN version
-   DXF: Add lineweight by using an offset line in the rendered version and rounding the ends by adding the length equal to the offset.
-   Geometry: Map RFEM nodes to polylines, map RFEM polylines to surfaces. Use order of lines to determine axis and match RFEM. Use this to get pressures.
-   Geometry needs to be tabled beyond simple use cases until this can be written in C# and have results sent to Calcpad via API.

```
f(x) = $Repeat{1/i @ i = x : 10}
f(0)
```
