#!/bin/bash
# Launch CK3 and detect main-menu via RAM PLATEAU (reliable signal; error.log quiescence is not).
# RAM climbs through data+map+object load, then flattens at the menu. Leaves CK3 running.
CK3DOCS=~/Documents/Paradox\ Interactive/Crusader\ Kings\ III
CRASHES="$CK3DOCS/crashes"
ERRLOG="$CK3DOCS/logs/error.log"
CK3EXE="/c/Program Files (x86)/Steam/steamapps/common/Crusader Kings III/binaries/ck3.exe"

CRASH_BEFORE=$(ls -td "$CRASHES"/ck3_* 2>/dev/null | head -1)
"$CK3EXE" -debug_mode -develop &
echo "LAUNCHED pid=$!"

START=$(date +%s); DEADLINE=$((START + 1080))   # 18 min cap
ANCHOR=-1; STABLE_SINCE=$START; result="UNKNOWN"; NOW=$START; MB=0; EMPTY=0
while true; do
    sleep 20
    NOW=$(date +%s)
    MEM=$(powershell -NoProfile -Command "(Get-Process ck3 -ErrorAction SilentlyContinue | Select-Object -First 1).WorkingSet64" 2>/dev/null | tr -d '\r ')
    CA=$(ls -td "$CRASHES"/ck3_* 2>/dev/null | head -1)
    if [ -n "$CA" ] && [ "$CA" != "$CRASH_BEFORE" ]; then result="CRASH:$CA"; break; fi
    if [ -z "$MEM" ]; then
        # Require 3 consecutive empty reads (~60s) before declaring exit — guards against a
        # transient Get-Process hiccup falsely reporting the process gone.
        EMPTY=$((EMPTY + 1))
        if [ "$ANCHOR" -ge 0 ] && [ $EMPTY -ge 3 ]; then result="EXITED_NO_DUMP"; break; fi
        continue
    fi
    EMPTY=0
    MB=$((MEM / 1048576))
    if [ "$ANCHOR" -lt 0 ]; then ANCHOR=$MB; STABLE_SINCE=$NOW; fi
    DIFF=$((MB - ANCHOR)); [ $DIFF -lt 0 ] && DIFF=$((-DIFF))
    if [ $DIFF -gt 50 ]; then ANCHOR=$MB; STABLE_SINCE=$NOW; fi
    STABLE=$((NOW - STABLE_SINCE))
    echo "t=$((NOW - START))s ram=${MB}MB stable=${STABLE}s"
    if [ $MB -ge 4000 ] && [ $STABLE -ge 150 ]; then result="LOADED_PLATEAU ram=${MB}MB"; break; fi
    if [ $NOW -ge $DEADLINE ]; then result="TIMEOUT ram=${MB}MB"; break; fi
done
echo "RESULT=$result t=$((NOW - START))s"
echo "=== new crash dump? ==="
CA=$(ls -td "$CRASHES"/ck3_* 2>/dev/null | head -1)
{ [ -n "$CA" ] && [ "$CA" != "$CRASH_BEFORE" ] && echo "$CA"; } || echo "(none new)"
echo "=== error.log size + tail ==="; stat -c%s "$ERRLOG" 2>/dev/null; tail -15 "$ERRLOG" 2>/dev/null
