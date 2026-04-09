
#!/usr/bin/env bash
# Convert SVG text to outlines using Inkscape CLI
# Usage: ./outline_svg.sh path/to/input.svg
set -e
IN="$1"
OUT="${IN%.svg}-outlined.svg"
if ! command -v inkscape >/dev/null 2>&1; then
  echo "Inkscape not found. Please install Inkscape 1.0+" >&2
  exit 1
fi
# Export to PDF and back to SVG tends to outline text; newer inkscape has --export-plain-svg with --actions
# Use actions to select all, object-to-path, and save
inkscape --actions="select-all:all;object-to-path;export-do;FileSave;FileClose" --export-type=svg --export-filename="$OUT" "$IN"
echo "Outlined SVG written to $OUT"
