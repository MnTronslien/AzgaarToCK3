#!/bin/bash
# Launch CK3, leave it running, and detect crash-vs-main-menu without blocking on exit.
# Main-menu heuristic: process alive >= 5 min AND error.log quiescent >= 90s (load burst settled).
CK3DOCS=~/Documents/Paradox\ Interactive/Crusader\ Kings\ III
CRASHES="$CK3DOCS/crashes"
ERRLOG="$CK3DOCS/logs/error.log"
SYSLOG="$CK3DOCS/logs/system.log"
CK3EXE="/c/Program Files (x86)/Steam/steamapps/common/Crusader Kings III/binaries/ck3.exe"

CRASH_BEFORE=$(ls -td "$CRASHES"/ck3_* 2>/dev/null | head -1)
"$CK3EXE" -debug_mode -develop &
CK3_PID=$!
echo "LAUNCHED pid=$CK3_PID"

START=$(date +%s)
DEADLINE=$((START + 900))   # 15 min hard cap
LASTSIZE=-1
LASTCHANGE=$START
result="UNKNOWN"
NOW=$START

while true; do
    sleep 20
    NOW=$(date +%s)
    CA=$(ls -td "$CRASHES"/ck3_* 2>/dev/null | head -1)
    if [ -n "$CA" ] && [ "$CA" != "$CRASH_BEFORE" ]; then result="CRASH"; break; fi
    if ! kill -0 "$CK3_PID" 2>/dev/null; then result="EXITED_NO_DUMP"; break; fi
    SZ=$(stat -c%s "$ERRLOG" 2>/dev/null || echo 0)
    if [ "$SZ" != "$LASTSIZE" ]; then LASTSIZE=$SZ; LASTCHANGE=$NOW; fi
    ELAPSED=$((NOW - START))
    QUIET=$((NOW - LASTCHANGE))
    if [ $ELAPSED -ge 300 ] && [ $QUIET -ge 90 ]; then result="MAINMENU"; break; fi
    if [ $NOW -ge $DEADLINE ]; then result="TIMEOUT_ALIVE"; break; fi
done

echo "RESULT=$result elapsed=$((NOW - START))s pid_alive=$(kill -0 "$CK3_PID" 2>/dev/null && echo yes || echo no)"
echo "=== crash dump (if any) ==="
CA=$(ls -td "$CRASHES"/ck3_* 2>/dev/null | head -1)
[ -n "$CA" ] && [ "$CA" != "$CRASH_BEFORE" ] && echo "$CA" || echo "(none new)"
echo "=== error.log size + tail ==="
stat -c%s "$ERRLOG" 2>/dev/null; tail -25 "$ERRLOG" 2>/dev/null
echo "=== system.log tail ==="
tail -15 "$SYSLOG" 2>/dev/null
