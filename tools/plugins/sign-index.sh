#!/usr/bin/env bash
# Writes a plugins folder's index.txt -- one "id version file sha512" line for each plugin's plugin.json and the stage
# it names -- and signs it with ssh-keygen under the cx-plugins namespace. Run it on the maintainer's machine, after
# reviewing every stage the index will vouch for; the key's public half belongs in packaging/plugin_signers.
#
# Usage: tools/plugins/sign-index.sh <plugins-folder> <private-key>
set -euo pipefail
dir="${1:?usage: sign-index.sh <plugins-folder> <private-key>}"
key="${2:?usage: sign-index.sh <plugins-folder> <private-key>}"
index="$dir/index.txt"
: > "$index"
for p in "$dir"/*/; do
  [ -f "$p/plugin.json" ] || continue
  id=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$p/plugin.json")
  ver=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$p/plugin.json")
  stage=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["stage"])' "$p/plugin.json")
  for f in plugin.json "$stage"; do
    printf '%s %s %s %s\n' "$id" "$ver" "$f" "$(sha512sum "$p/$f" | cut -d' ' -f1)" >> "$index"
  done
done
rm -f "$index.sig"
ssh-keygen -Y sign -f "$key" -n cx-plugins "$index"
echo ">> $index and $index.sig written"
