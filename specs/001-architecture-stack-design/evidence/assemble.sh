#!/usr/bin/env bash
# Assemble docs/design.md from the draft fragments, in C-locale filename order (Tasks T002).
set -euo pipefail
FD="specs/001-architecture-stack-design"
OUT="docs/design.md"
mkdir -p docs
: > "$OUT"
first=1
while IFS= read -r f; do
  [ -s "$f" ] || continue
  if [ $first -eq 0 ]; then printf '\n' >> "$OUT"; fi
  cat "$f" >> "$OUT"
  # ensure each fragment ends with a newline
  [ "$(tail -c1 "$f" | od -An -c | tr -d ' ')" = '\n' ] || printf '\n' >> "$OUT"
  first=0
done < <(LC_ALL=C ls "$FD"/draft/*.md | LC_ALL=C sort)
echo "assembled $OUT: $(wc -l < "$OUT" | tr -d ' ') lines"
