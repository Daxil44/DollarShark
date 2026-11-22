#!/bin/sh
# Backup scheduler: one backup when the container starts, then one every day at BACKUP_TIME_UTC (HH:MM, UTC).
set -u

TIME="${BACKUP_TIME_UTC:-08:00}"

run_backup() {
    /usr/local/bin/backup.sh || echo "!! Backup FAILED (see the messages above). Next try at $TIME UTC."
}

if [ "${BACKUP_ON_START:-true}" = "true" ]; then
    run_backup
fi

while true; do
    now=$(date -u +%s)
    next=$(date -u -d "$(date -u +%Y-%m-%d) $TIME" +%s) || { echo "BACKUP_TIME_UTC must look like 08:00"; exit 1; }
    if [ "$next" -le "$now" ]; then
        next=$((next + 86400))
    fi
    echo "Next backup: $(date -u -d "@$next" '+%Y-%m-%d %H:%M') UTC"
    sleep $((next - now))
    run_backup
done
