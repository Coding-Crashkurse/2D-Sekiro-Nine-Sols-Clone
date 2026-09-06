#!/bin/bash
# Build the Windows player. Output: Builds/Windows/AshenSol.exe
P="C:/Users/User/Desktop/nine_sols/AshenSol"
U="C:/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor/Unity.exe"
LOG="C:/Users/User/Desktop/nine_sols/Tools/build.log"
if tasklist 2>/dev/null | grep -qi "^Unity.exe"; then echo "Unity.exe already running — abort"; exit 2; fi
"$U" -batchmode -quit -disable-assembly-updater -projectPath "$P" -executeMethod AshenSol.EditorTools.BuildScript.BuildWindows -logFile "$LOG" "$@"
code=$?
echo "UNITY EXIT=$code"
grep -E "error CS[0-9]+" "$LOG" | sed 's/^.*Assets/Assets/' | sort -u | head -40
grep -E "\[Build\]|\[Bootstrap\]|Build Finished|BuildFailedException|Error building|Shader error|Exception:" "$LOG" | head -20 | cut -c1-240
ls -la "C:/Users/User/Desktop/nine_sols/Builds/Windows/AshenSol.exe" 2>/dev/null
exit $code
