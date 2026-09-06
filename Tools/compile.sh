#!/bin/bash
# Compile check + project bootstrap in Unity batch mode. Prints compiler errors and the exit code.
# Retries the known EPERM package-extraction flake on this machine.
P="C:/Users/User/Desktop/nine_sols/AshenSol"
U="C:/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor/Unity.exe"
LOG="C:/Users/User/Desktop/nine_sols/Tools/compile.log"
if tasklist 2>/dev/null | grep -qi "^Unity.exe"; then echo "Unity.exe already running — abort"; exit 2; fi
for attempt in 1 2 3; do
  bash "$(dirname "$0")/fix_packages.sh"
  "$U" -batchmode -quit -nographics -disable-assembly-updater -projectPath "$P" \
       -executeMethod AshenSol.EditorTools.ProjectBootstrap.Run -logFile "$LOG"
  code=$?
  if grep -q "EPERM" "$LOG"; then
    echo "[compile] EPERM package flake on attempt $attempt — retrying"
    rm -rf "$P/Library/PackageCache/.tmp-"*
    continue
  fi
  break
done
echo "UNITY EXIT=$code"
echo "--- errors ---"
grep -E "error CS[0-9]+" "$LOG" | sed 's/^.*Assets/Assets/' | sort -u | head -80
echo "--- error count: $(grep -cE 'error CS[0-9]+' "$LOG") ---"
grep -E "\[Bootstrap\]|Scripts have compiler errors|API Updater\] Updated|EPERM" "$LOG" | head -12 | cut -c1-240
exit $code
