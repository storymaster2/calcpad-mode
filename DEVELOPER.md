# Developer Documentation for CalcpadCE

## Generating the Documentation

To build the documentation locally, .NET SDK 10, Python and NPM are prerequisites.

You need to install mathjax and MkDocs, and build Calcpad.Cli:

```pwsh
pip install mkdocs
npm install --no-save --package-lock=false --prefix . mathjax@4 @mathjax/mathjax-newcm-font@4
cp -r node_modules/mathjax/* docs/javascripts/mathjax/
New-Item -ItemType Directory -Force -Path docs/javascripts/mathjax/output/fonts/mathjax-newcm | Out-Null
cp -r node_modules/@mathjax/mathjax-newcm-font/* docs/javascripts/mathjax/output/fonts/mathjax-newcm/
dotnet build Calcpad.Cli
```

The examples are rendered via a hook when the documentation is built.
The hook calls CalcpadCE CLI for each example and spits out an HTML stub (without headers/body).

Generation can be started by invoking this command.
A local webserver will spawn to serve the rendered documentation:

`mkdocs serve`
