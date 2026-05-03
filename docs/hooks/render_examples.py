"""MkDocs hook: renders .cpd examples with the Calcpad CLI and injects
virtual category pages into the docs build.

Which examples are built and in what order is controlled by docs/examples.yml.
Each top-level key becomes an examples/<slug>.md page showing source code
alongside the rendered HTML output in a two-column grid.
"""

import logging
import os
import re
import shutil
import subprocess
import tempfile
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

import mkdocs.structure.files as mkdocs_files

log = logging.getLogger("mkdocs")

_temp_dir: str | None = None


# ---------------------------------------------------------------------------
# Hook entry points
# ---------------------------------------------------------------------------


def on_config(config, **kwargs):
    """Inject the Examples nav section from docs/examples.yml."""
    repo_root = Path(config["config_file_path"]).parent
    examples = _load_examples(repo_root)

    examples_nav = [{cat: f"examples/{_slugify(cat)}.md"} for cat in examples]
    config["nav"].append({"Examples": examples_nav})
    return config


def on_files(files, config, **kwargs):
    """Generate category pages and inject them as virtual MkDocs files."""
    global _temp_dir

    repo_root = Path(config["config_file_path"]).parent
    examples_root = repo_root / "Examples"
    examples = _load_examples(repo_root)

    cli_path = _find_cli(repo_root)

    # Resolve every listed entry to a concrete .cpd path (fail fast on missing files)
    all_cpd: list[tuple[str, Path]] = []
    for category, stems in examples.items():
        for stem in stems:
            cpd_file = examples_root / category / f"{stem}.cpd"
            if not cpd_file.exists():
                raise SystemExit(
                    f"render_examples: Listed example not found on disk:\n"
                    f"  {cpd_file}\n"
                    f"Either add the file or remove the entry from docs/examples.yml."
                )
            all_cpd.append((category, cpd_file))

    # Render all fragments in parallel
    fragments: dict[Path, str] = {}
    workers = min(os.cpu_count() or 1, len(all_cpd))
    with ThreadPoolExecutor(max_workers=workers) as pool:
        future_to_cpd = {
            pool.submit(_render_fragment, cpd_file, cli_path, f"{cat}/{cpd_file.stem}"): cpd_file
            for cat, cpd_file in all_cpd
        }
        for future in as_completed(future_to_cpd):
            cpd_file = future_to_cpd[future]
            fragments[cpd_file] = future.result()

    # Write generated .md files to a temp directory so MkDocs can read them
    _temp_dir = tempfile.mkdtemp(prefix="calcpad-examples-")
    examples_temp = Path(_temp_dir) / "examples"
    examples_temp.mkdir()

    all_files = list(files)

    for category, stems in examples.items():
        cpd_files = [examples_root / category / f"{stem}.cpd" for stem in stems]
        content = _render_category_page(category, cpd_files, fragments)
        slug = _slugify(category)
        dest = examples_temp / f"{slug}.md"
        dest.write_text(content, encoding="utf-8")

        virtual = mkdocs_files.File(
            path=f"examples/{slug}.md",
            src_dir=_temp_dir,
            dest_dir=config["site_dir"],
            use_directory_urls=config["use_directory_urls"],
        )
        all_files.append(virtual)

    # Inject asset files (e.g. .js, .css, images) referenced by examples
    # They sit next to .cpd files and are referenced via relative URLs like ./toc.js,
    # which resolves to examples/<file> when the page is served under examples/.
    _ASSET_SUFFIXES = {".js", ".css", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp"}
    # src_dir must contain the full path subtree: src_dir / path = actual file on disk
    # so mirror the examples/ subdirectory inside the temp tree.
    assets_temp = Path(_temp_dir) / "assets"
    assets_examples = assets_temp / "examples"
    assets_examples.mkdir(parents=True)
    seen_asset_names: set[str] = set()
    for asset in examples_root.rglob("*"):
        if asset.suffix.lower() in _ASSET_SUFFIXES and asset.name not in seen_asset_names:
            shutil.copy2(asset, assets_examples / asset.name)
            seen_asset_names.add(asset.name)
            log.info(f"Copying asset {asset.relative_to(examples_root)}")
            virtual_asset = mkdocs_files.File(
                path=f"examples/{asset.name}",
                src_dir=str(assets_temp),
                dest_dir=config["site_dir"],
                use_directory_urls=False,
            )
            all_files.append(virtual_asset)

    return mkdocs_files.Files(all_files)


def on_post_build(config, **kwargs):
    _cleanup_temp()


def on_build_error(error, **kwargs):
    _cleanup_temp()


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------


def _cleanup_temp():
    global _temp_dir
    if _temp_dir and os.path.exists(_temp_dir):
        shutil.rmtree(_temp_dir, ignore_errors=True)
        _temp_dir = None


def _slugify(text: str) -> str:
    """Convert a category name to a URL-safe slug."""
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def _load_examples(repo_root: Path) -> dict[str, list[str]]:
    """Read docs/examples.yml and return {category: [relative_stem, ...]}."""
    yaml_path = repo_root / "docs" / "examples.yml"
    if not yaml_path.exists():
        raise SystemExit(
            f"render_examples: docs/examples.yml not found.\n"
            f"Generate it with:  python docs/hooks/generate_examples.py"
        )

    # Minimal hand-rolled YAML parser — avoids a hard PyYAML import.
    # Format is strictly: top-level keys followed by list items (  - value).
    examples: dict[str, list[str]] = {}
    current_category: str | None = None
    for lineno, raw in enumerate(yaml_path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.rstrip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("  - "):
            if current_category is None:
                raise SystemExit(f"render_examples: examples.yml line {lineno}: list item before any category key")
            examples[current_category].append(line[4:])
        elif line.endswith(":"):
            current_category = line[:-1]
            examples[current_category] = []
        else:
            raise SystemExit(f"render_examples: examples.yml line {lineno}: unexpected format: {line!r}")

    return examples
    """Return the path to an existing Cli executable.

    Searches (in order):
      1. cli-build/     — output of the explicit CI publish step
      2. Release publish — dotnet publish -c Release
      3. Debug publish   — dotnet publish -c Debug
      4. Debug build     — dotnet build -c Debug (default local build)
      5. Release build   — dotnet build -c Release

    Raises SystemExit with a clear message when the binary is not found.
    """
    is_win = os.name == "nt"
    exe_name = "Cli.exe" if is_win else "Cli"
    rid = "win-x64" if is_win else "linux-x64"
    tfm = "net10.0"

    candidates = [
        repo_root / "cli-build" / exe_name,
        repo_root / "Calcpad.Cli" / "bin" / "Release" / tfm / rid / "publish" / exe_name,
        repo_root / "Calcpad.Cli" / "bin" / "Debug" / tfm / rid / "publish" / exe_name,
        repo_root / "Calcpad.Cli" / "bin" / "Debug" / tfm / exe_name,
        repo_root / "Calcpad.Cli" / "bin" / "Release" / tfm / exe_name,
    ]

    for candidate in candidates:
        if candidate.exists():
            log.info(f"Using CLI at {candidate}")
            return candidate

    searched = "\n  ".join(str(c) for c in candidates)
    raise SystemExit(
        f"render_examples: Cli executable not found.\n"
        f"Searched:\n  {searched}\n"
        f"Build it first with:\n"
        f"  dotnet build Calcpad.Cli/"
    )


def _render_fragment(cpd_file: Path, cli_path: Path, label: str = "") -> str:
    """Run the CLI in body-only silent mode and return the HTML fragment.

    Raises SystemExit on any CLI error, timeout, or empty output.
    """
    name = label or cpd_file.name
    log.info(f"Rendering {name} ...")
    tmp_fd, out_path = tempfile.mkstemp(suffix=".html")
    os.close(tmp_fd)
    try:
        result = subprocess.run(
            [str(cli_path), str(cpd_file), out_path, "-b", "-s"],
            capture_output=True,
            timeout=30,
        )
        if result.returncode != 0:
            stderr = result.stderr.decode(errors="replace").strip()
            stdout = result.stdout.decode(errors="replace").strip()
            raise SystemExit(
                f"render_examples: CLI failed (exit {result.returncode}) for {cpd_file.name}"
                + (f"\n  stderr: {stderr}" if stderr else "")
                + (f"\n  stdout: {stdout}" if stdout else "")
            )
        if not (os.path.exists(out_path) and os.path.getsize(out_path) > 0):
            raise SystemExit(
                f"render_examples: CLI produced no output for {cpd_file.name}"
            )
        log.info(f"Rendered  {name}")
        return Path(out_path).read_text(encoding="utf-8")
    except subprocess.TimeoutExpired:
        raise SystemExit(f"render_examples: CLI timed out rendering {cpd_file.name}")
    except OSError as exc:
        raise SystemExit(f"render_examples: Failed to run CLI for {cpd_file.name}: {exc}")
    finally:
        if os.path.exists(out_path):
            os.unlink(out_path)


def _strip_headings(fragment: str) -> str:
    """Replace <h1>–<h6> tags with divs so they don't appear in the MkDocs TOC."""
    fragment = re.sub(r"<(h[1-6])([ >])", r'<div class="calcpad-\1"\2', fragment)
    fragment = re.sub(r"</(h[1-6])>", r"</div>", fragment)
    return fragment


def _render_category_page(category: str, cpd_files: list[Path], fragments: dict[Path, str]) -> str:
    """Build the Markdown content for one category page from pre-rendered fragments."""
    lines = [f"# {category}\n"]

    for cpd_file in cpd_files:
        name = cpd_file.stem
        lines.append(f"\n## {name}\n")

        source = cpd_file.read_text(encoding="utf-8", errors="replace")
        fragment = _strip_headings(fragments[cpd_file])

        lines.append('<div class="example-grid" markdown="block">')
        lines.append('<div class="example-source" markdown="block">\n')
        lines.append("```")
        lines.append(source.rstrip())
        lines.append("```\n")
        lines.append("</div>")
        lines.append('<div class="example-output">\n')
        lines.append(fragment)
        lines.append("</div>")
        lines.append("</div>\n")

    return "\n".join(lines)
