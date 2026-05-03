"""One-shot generator: scans Examples/ and writes docs/examples.yml.

Run from the repo root:
    python docs/hooks/generate_examples.py

After verifying the output, delete this script.
"""

from pathlib import Path
import re


def _slugify(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def main() -> None:
    repo_root = Path(__file__).parent.parent.parent
    examples_root = repo_root / "Examples"
    out_path = repo_root / "docs" / "examples.yml"

    if not examples_root.exists():
        raise SystemExit(f"Examples/ directory not found at {examples_root}")

    # Group relative stems by top-level category
    categories: dict[str, list[str]] = {}
    for cpd_file in sorted(examples_root.rglob("*.cpd")):
        rel = cpd_file.relative_to(examples_root)
        category = rel.parts[0]
        # stem = path relative to category dir, without .cpd extension
        stem = str(rel.relative_to(category).with_suffix("")).replace("\\", "/")
        categories.setdefault(category, []).append(stem)

    # Write YAML manually to preserve list order and readable formatting
    lines: list[str] = []
    for category in sorted(categories):
        lines.append(f"{category}:")
        for stem in sorted(categories[category]):
            lines.append(f"  - {stem}")
        lines.append("")

    out_path.write_text("\n".join(lines), encoding="utf-8")

    total = sum(len(v) for v in categories.values())
    print(f"Written {out_path.relative_to(repo_root)}")
    print(f"  {len(categories)} categories, {total} examples")
    for cat, stems in sorted(categories.items()):
        print(f"  {cat}: {len(stems)}")


if __name__ == "__main__":
    main()
