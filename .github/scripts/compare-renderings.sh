#!/bin/bash
# Builds the Calcpad CLI, then runs compare_renderings.py in a throwaway
# .venv. Any arguments (e.g. --write) are forwarded to compare_renderings.py.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
VENV_DIR="$SCRIPT_DIR/.venv"

cleanup() {
    rm -rf "$VENV_DIR"
}
trap cleanup EXIT

echo "Building Calcpad CLI..."
dotnet build "$REPO_ROOT/Calcpad.Cli/Calcpad.Cli.csproj" -c Release

echo "Setting up Python venv..."
python3 -m venv "$VENV_DIR"
"$VENV_DIR/bin/pip" install --quiet beautifulsoup4==4.14.3

echo "Comparing renderings..."
"$VENV_DIR/bin/python" "$SCRIPT_DIR/compare_renderings.py" "$@"
