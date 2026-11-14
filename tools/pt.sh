#!/usr/bin/env bash
# Play-test helper for the open Unity Editor.
#   tools/pt.sh start "<game args>"     recompile, check maps, enter play mode with the given args
#   tools/pt.sh shot <name>             capture the Game view to the shots folder
#   tools/pt.sh eval '<C# code>'        run C# in the Editor
#   tools/pt.sh errors                  print console errors and warnings
#   tools/pt.sh stop                    leave play mode
P="C:\\Users\\omerj\\Downloads\\Remaster"
SHOTS="${REMASTER_SHOTS:-C:\\Users\\omerj\\AppData\\Local\\Temp\\claude\\a5e3e8bcb5e31c1d\\c5b96579-705a-4ed2-a09f-9c2a9f6dd6a2\\scratchpad\\shots}"
C="unity command --caller plugin --skill unity-cli --project-path $P"
cmd="$1"; shift
case "$cmd" in
  start)
    # the Editor can be busy (importing, leaving play mode) for a moment: retry a few times before giving up
    ok=0
    for attempt in 1 2 3 4; do
      unity status --until-ready --project-path "$P" --format json >/dev/null 2>&1
      unity recompile --project-path "$P" --format json 2>/dev/null | python -c "
import sys,json
env=json.load(sys.stdin)
d=env.get('data')
if d is None:
    print('recompile not ready:', [e.get('code') for e in env.get('errors',[])]); sys.exit(2)
print('compile errors:', d['summary']['errors'], 'warnings:', d['summary']['warnings'])
for e in d['errors'][:20]: print('  ', e)
sys.exit(1 if d['summary']['errors'] else 0)"
      rc=$?
      if [ $rc -eq 0 ]; then ok=1; break; fi
      if [ $rc -eq 1 ]; then exit 1; fi
      sleep 3
    done
    [ $ok -eq 1 ] || exit 1
    unity status --until-ready --project-path "$P" --format json >/dev/null 2>&1
    $C script eval "UnityEditor.EditorPrefs.SetString(\"RemasterArgs\", \"$1\"); return MapValidator.RunAll().Split('\\n')[0];" 2>&1 | tail -1 | cut -f3
    $C console clear >/dev/null 2>&1
    $C editor set_auto_tick >/dev/null 2>&1
    $C editor.playmode start 2>&1 | tail -1 | cut -c1-60
    unity status --until-ready --project-path "$P" --format json >/dev/null 2>&1
    $C job wait -- --condition '{"member":"UnityEngine.Time.frameCount","op":"greaterThan","value":30}' --timeout 30 2>&1 | tail -1 | cut -c1-90
    ;;
  shot)
    mkdir -p "$(cygpath -u "$SHOTS")"
    $C capture game_view -- --width 1920 --height 1080 --output-path "$SHOTS\\$1.png" 2>&1 | tail -1 | cut -c1-70
    ;;
  eval)
    $C script eval "$1" 2>&1 | tail -1 | cut -f2,3 | cut -c1-1800
    ;;
  errors)
    $C console get -- --level warning --tail 40 2>&1 | tail -45 | cut -c1-900
    ;;
  stop)
    $C editor.playmode stop 2>&1 | tail -1 | cut -c1-60
    ;;
esac
