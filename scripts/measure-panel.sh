#!/bin/zsh
# Times `cpm list` against the Limits target in the design: 10 profiles and 5,000 transcripts,
# under 1 second. Uses generated data in a temporary HOME. This times reading the sessions,
# not a panel opening, because no panel exists until S3. Run ./make-cli.sh first.
set -eu
repo=${0:A:h:h}
cpm=$repo/build/cli/cpm
home=$(mktemp -d /private/tmp/cpm-measure-XXXXXX); home=${home:A}
trap 'rm -rf "$home"' EXIT
e=(env -i HOME=$home PATH=/usr/bin:/bin)

for i in {1..9}; do $e $cpm add "P$i" "p$i" '#000000' >/dev/null; done

/usr/bin/python3 -I - "$home" <<'PY'
import glob, json, os, sys, uuid
home = sys.argv[1]
dirs = [home + "/.claude"] + sorted(glob.glob(home + "/.claude-profiles/profiles/*"))
filler = json.dumps({"type": "assistant", "x": "y" * 2000}) + "\n"
small = filler * 10       # 20 KB
large = filler * 200      # 400 KB, past the 64 KB ends
for n in range(5000):
    d = dirs[n % len(dirs)] + f"/projects/p{n % 20}"
    os.makedirs(d, exist_ok=True)
    sid = str(uuid.uuid4())
    head = json.dumps({"type": "user", "cwd": "/w", "entrypoint": "cli", "sessionId": sid}) + "\n"
    title = json.dumps({"type": "ai-title", "aiTitle": f"Session {n}", "sessionId": sid}) + "\n"
    # One in ten is large. One in fifty has no title, which forces the wider read.
    body = large if n % 10 == 0 else small
    with open(f"{d}/{sid}.jsonl", "w") as f:
        f.write(head + body + ("" if n % 50 == 0 else title))
PY

for run in 1 2 3; do
  s=$(/usr/bin/python3 -I -c 'import time; print(time.time())')
  count=$($e $cpm list | wc -l | tr -d ' ')
  t=$(/usr/bin/python3 -I -c "import time; print(round(time.time()-$s, 2))")
  print -r -- "run $run: $count rows in $t s"
done
print -r -- "Target: under 1 s. Each run is a new process, so the in-memory cache does not help here;"
print -r -- "the app keeps it between panel opens."
