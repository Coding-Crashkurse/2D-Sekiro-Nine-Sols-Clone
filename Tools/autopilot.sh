#!/bin/bash
# Run the built game in autopilot mode, capture screenshots + log. Usage: autopilot.sh [quitAfterSeconds] [extra args]
Q=${1:-240}; shift
S="C:/Users/User/Desktop/nine_sols/Screenshots"
EXE="C:/Users/User/Desktop/nine_sols/Builds/Windows/AshenSol.exe"
PLOG="C:/Users/User/AppData/LocalLow/AshenSol/Ashen Sol/Player.log"
rm -f "$S"/*.png "$S/autopilot_log.txt"
mkdir -p "$S"
"$EXE" -autopilot -screenshotDir "$S" -quitAfter "$Q" -screen-width 1600 -screen-height 900 -screen-fullscreen 0 "$@"
echo "GAME EXIT=$?"
echo "--- autopilot log (tail) ---"
tail -n 40 "$S/autopilot_log.txt" 2>/dev/null
echo "--- player log exceptions ---"
grep -n -A3 "Exception\|NullReference" "$PLOG" 2>/dev/null | head -40
ls "$S" | head -50
