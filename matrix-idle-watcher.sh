#!/bin/bash
# matrix-idle-watcher — Triggers matrix-bg --fullscreen after idle timeout
# Runs as a launchd agent, checks system-wide idle time (keyboard + mouse)
# Reads settings from ~/.config/matrix-bg/config

CONFIG_DIR="$HOME/.config/matrix-bg"
CONFIG_FILE="$CONFIG_DIR/config"
CHECK_INTERVAL=5   # how often to check (seconds)
PID_FILE="/tmp/.matrix-idle-pid"
MATRIX_BIN="$HOME/.local/bin/matrix-bg"

# Read a config value, return default if missing
config_get() {
    local key="$1" default="$2"
    if [[ -f "$CONFIG_FILE" ]]; then
        local val
        val=$(grep -E "^${key}=" "$CONFIG_FILE" 2>/dev/null | tail -1 | cut -d= -f2-)
        if [[ -n "$val" ]]; then
            echo "$val"
            return
        fi
    fi
    echo "$default"
}

# Check if media is playing
# Different apps use different assertions:
#   - Safari/VLC/QuickTime: PreventUserIdleDisplaySleep
#   - Chrome/Firefox: NoIdleSleepAssertion "Playing audio/video"
#   - coreaudiod: audio-out assertions for active playback
media_is_playing() {
    local assertions
    assertions=$(pmset -g assertions 2>/dev/null)
    echo "$assertions" | grep -q "PreventUserIdleDisplaySleep.*1" && return 0
    echo "$assertions" | grep -q "NoIdleSleepAssertion.*Playing" && return 0
    return 1
}

while true; do
    # Check if screensaver is enabled in config
    ENABLED=$(config_get "enabled" "true")
    if [[ "$ENABLED" != "true" ]]; then
        sleep "$CHECK_INTERVAL"
        continue
    fi

    # Check if media is playing — skip activation
    SKIP_MEDIA=$(config_get "skip_media" "true")
    if [[ "$SKIP_MEDIA" == "true" ]] && media_is_playing; then
        sleep "$CHECK_INTERVAL"
        continue
    fi

    # Read idle threshold from config (default 60s)
    IDLE_THRESHOLD=$(config_get "idle_seconds" "60")

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
