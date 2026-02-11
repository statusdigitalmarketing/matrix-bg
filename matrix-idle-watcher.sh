#!/bin/bash
# matrix-idle-watcher — Triggers matrix-bg --fullscreen after idle timeout
# Runs as a launchd agent, checks system-wide idle time (keyboard + mouse)

IDLE_THRESHOLD=60  # seconds before triggering
CHECK_INTERVAL=5   # how often to check (seconds)
PID_FILE="/tmp/.matrix-idle-pid"
MATRIX_BIN="$HOME/.local/bin/matrix-bg"

while true; do
    # Get system idle time in nanoseconds, convert to seconds
    IDLE_NS=$(ioreg -c IOHIDSystem | awk '/HIDIdleTime/ {print $NF; exit}')
    IDLE_SECS=$(( IDLE_NS / 1000000000 ))

    MATRIX_RUNNING=false
    if [[ -f "$PID_FILE" ]] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
        MATRIX_RUNNING=true
    fi

    if [[ $IDLE_SECS -ge $IDLE_THRESHOLD ]] && [[ "$MATRIX_RUNNING" == false ]]; then
        # User is idle — start matrix
        "$MATRIX_BIN" --fullscreen &
        echo $! > "$PID_FILE"
    elif [[ $IDLE_SECS -lt 5 ]] && [[ "$MATRIX_RUNNING" == true ]]; then
        # User came back — kill matrix
        kill "$(cat "$PID_FILE")" 2>/dev/null
        rm -f "$PID_FILE"
    fi

    sleep "$CHECK_INTERVAL"
done
