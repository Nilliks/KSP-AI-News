#!/usr/bin/env bash
# Linux/macOS checks with Mono: runs the core unit tests and compile-checks the KSP layer
# against stubs (no game DLLs needed). With KSP_DIR set it builds the real DLL instead.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p bin
mcs -nologo -debug -out:bin/tests.exe -r:System.Core.dll src/Core/*.cs src/Core/Web/*.cs tests/*.cs
mono --debug bin/tests.exe
if [ -n "${KSP_DIR:-}" ]; then
  M="$KSP_DIR/KSP_x64_Data/Managed"; [ -d "$M" ] || M="$KSP_DIR/KSP_Data/Managed"
  R=""; for d in mscorlib System System.Core netstandard Assembly-CSharp Assembly-CSharp-firstpass; do
    [ -f "$M/$d.dll" ] && R="$R -r:$M/$d.dll"; done
  for f in "$M"/UnityEngine*.dll; do R="$R -r:$f"; done
  mcs -nologo -target:library -optimize -nostdlib $R -out:GameData/KerbalNews/Plugins/KerbalNews.dll src/Core/*.cs src/Core/Web/*.cs src/KSP/*.cs
  echo "Built GameData/KerbalNews/Plugins/KerbalNews.dll"
else
  mcs -nologo -target:library -out:bin/stubs.dll -r:System.Core.dll stubs/KspStubs.cs
  mcs -nologo -target:library -out:bin/KerbalNews.stubcheck.dll -r:System.Core.dll -r:bin/stubs.dll src/Core/*.cs src/Core/Web/*.cs src/KSP/*.cs
  echo "KSP layer compiles against stubs (set KSP_DIR for a real build)"
fi
