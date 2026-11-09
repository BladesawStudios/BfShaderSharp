#!/bin/sh
# Usage: build.sh <Fritchii src dir> <output dir>. Needs a C++20 g++ (WinLibs mingw64 works).
set -e
SRC="$1"; OUT="$2"
mkdir -p "$OUT"
g++ -std=c++20 -O2 -shared -static -o "$OUT/bfs_oracle.dll" -I"$SRC" "$(dirname "$0")/oracle.cpp"
