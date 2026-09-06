#!/bin/bash
# Unity 6000.4.6f1 quirk: several ShaderGraph package files use the type `GUID` (UnityEngine.GUID)
# without importing it, so the ShaderGraph editor assembly fails to compile. Unity's script updater
# then rewrites them into broken forms. Restore the originals from the editor installation and add a
# `using GUID = UnityEngine.GUID;` alias where the type is used but not imported.
# Idempotent; run before every batch-mode invocation.
P="C:/Users/User/Desktop/nine_sols/AshenSol"
BUILTIN="C:/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor/Data/Resources/PackageManager/BuiltInPackages"
CACHE="$P/Library/PackageCache"
ALIAS="using GUID = UnityEngine.GUID;"
[ -d "$CACHE" ] || exit 0

# 1. restore any package file the updater rewrote
restored=0
for f in $(grep -rlE "UnityEngine\.GUID|UnityEditor\.GUID" "$CACHE" 2>/dev/null | grep -v "using GUID"); do
  rel="${f#$CACHE/}"; pkg="${rel%%/*}"; sub="${rel#*/}"
  src="$BUILTIN/${pkg%@*}/$sub"
  [ -f "$src" ] && cp -f "$src" "$f" && restored=$((restored+1))
done

# 2. add the alias wherever `GUID` is used as a type without `using UnityEngine;`
patched=0
for f in $(grep -rlE '(new GUID\(|GUID\.TryParse|\bGUID [a-zA-Z_]+ *[,)=;]|<GUID[,>]|\(GUID )' \
           "$CACHE"/com.unity.shadergraph@* "$CACHE"/com.unity.render-pipelines.* 2>/dev/null); do
  grep -q "^using UnityEngine;" "$f" && continue
  grep -q "^$ALIAS" "$f" && continue
  sed -i "0,/^namespace /s//$ALIAS\n\nnamespace /" "$f"
  patched=$((patched+1))
done
echo "[fix_packages] restored=$restored patched=$patched"
exit 0
