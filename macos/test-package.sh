#!/bin/bash
set -euo pipefail
work="$(mktemp -d)"
pid=
cleanup() {
  if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; fi
  rm -rf "$work"
}
trap cleanup EXIT
ditto -x -k "$1" "$work"
app="$work/WheekKeys.app"
exe="$app/Contents/MacOS/WheekKeys"
test -x "$exe"
lipo "$exe" -verify_arch arm64 x86_64
codesign --verify --deep --strict --verbose=2 "$app"
"$exe" --smoke-test macos-frame.png
"$exe" >"$work/startup.log" 2>&1 &
pid=$!
sleep 5
if ! kill -0 "$pid" 2>/dev/null; then
  cat "$work/startup.log"
  exit 1
fi
