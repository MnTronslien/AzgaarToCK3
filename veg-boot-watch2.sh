#!/bin/bash
# Watch the already-running ck3.exe for main-menu via RAM plateau (RAM climbs through load,
# flattens at the menu). Far more reliable than error.log quiescence (which false-positives mid-load).
CK3DOCS=~/Documents/Paradox\ Interactive/Crusader\ Kings\ III
CRASHES="$CK3DOCS/crashes"
ERRLOG="$CK3DOCS/logs/error.log"
CRASH_BEFORE=$(ls -td "$CRASHES"/ck3_* 2>/dev/null | head -1)

START=$(date +%s); DEADLINE=$((START + 1080))   # 18 min cap
ANCHOR=-1; STABLE_SINCE=$START; result="UNKNOWN"; NOW=$START; MB=0
while true; do
    sleep 20
    NOW=$(date +%s)
    MEM=$(powershell -NoProfile -Command "(Get-Process ck3 -ErrorAction SilentlyContinue | Select-Object -First 1).WorkingSet64" 2>/dev/null | tr -d '\r ')
    CA=$(ls -td "$CRASHES"/ck3_* 2>/dev/null | head -1)
    if [ -n "$CA" ] && [ "$CA" != "$CRASH_BEFORE" ]; then result="CRASH:$CA"; break; fi
    if [ -z "$MEM" ]; then result="EXITED_NO_DUMP"; break; fi
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
echo "--- error.log tail ---"; tail -12 "$ERRLOG" 2>/dev/null
