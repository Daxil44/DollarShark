#!/bin/sh
# Restore tools. Run on the server from ~/DollarShark:
#   docker compose exec backup restore.sh list                    backups on the server and in S3
#   docker compose exec backup restore.sh test [NAME]             SAFE: restore into a throwaway database, compare row counts, delete it
#   docker compose stop api
#   docker compose exec backup restore.sh restore NAME --yes      REPLACE the live database + statement files with backup NAME
#   docker compose start api
# NAME is a file name from "list" (e.g. dollarshark-20261122-080000.tar.gpg) or "latest".
# Backups that are not on the server are downloaded from S3 automatically.
set -eu

S3_PREFIX="s3://${BACKUP_S3_BUCKET:-}/backups/"
LIVE_DB="${PGDATABASE:-dollarshark}"
TEST_DB=restore_check

usage() { sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'; exit 1; }

s3_enabled() { [ -n "${BACKUP_S3_BUCKET:-}" ]; }

list_local() { ls -1 /backups/dollarshark-*.tar.gpg 2>/dev/null | xargs -r -n1 basename | sort; }
list_safety() { ls -1 /backups/before-restore-*.tar.gpg 2>/dev/null | xargs -r -n1 basename | sort; }
list_s3() { aws s3 ls "$S3_PREFIX" | awk '{print $4}' | grep '^dollarshark-.*\.tar\.gpg$' | sort; }

cmd_list() {
    echo "On this server (/backups):"
    list_local | sed 's/^/  /'
    if [ -n "$(list_safety)" ]; then
        echo "Safety copies taken just before a restore (server only):"
        list_safety | sed 's/^/  /'
    fi
    if s3_enabled; then
        echo "In S3 ($S3_PREFIX):"
        list_s3 | sed 's/^/  /'
    else
        echo "S3: not configured (BACKUP_S3_BUCKET is empty)"
    fi
}

# fetch NAME -> decrypts the backup into $WORK (db.dump + statements.tar.gz)
fetch() {
    name="$1"
    if [ "$name" = "latest" ]; then
        name=$( (list_local; if s3_enabled; then list_s3; fi) | sort | tail -n 1)
        [ -n "$name" ] || { echo "No backups found."; exit 1; }
    fi
    case "$name" in */*|..*) echo "Use a file name from 'restore.sh list'."; exit 1 ;; esac

    if [ -f "/backups/$name" ]; then
        src="/backups/$name"
    elif s3_enabled; then
        echo "Downloading $name from S3..."
        aws s3 cp "$S3_PREFIX$name" "$WORK/$name" --only-show-errors --no-progress
        src="$WORK/$name"
    else
        echo "Backup $name not found in /backups (and S3 is not configured)."; exit 1
    fi

    echo "Using backup: $name"
    [ -n "${BACKUP_PASSPHRASE:-}" ] || { echo "BACKUP_PASSPHRASE is not set: it is needed to decrypt."; exit 1; }
    printf '%s' "$BACKUP_PASSPHRASE" > "$WORK/passphrase"
    gpg --batch --quiet --pinentry-mode loopback --passphrase-file "$WORK/passphrase" --no-symkey-cache \
        --decrypt "$src" | tar -C "$WORK" -xf - \
        || { echo "Could not decrypt $name: wrong BACKUP_PASSPHRASE or damaged file."; exit 1; }
}

table_counts() { # table_counts DB -> "table count" lines
    psql -d "$1" -XAtq -F ' ' -c "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY 1" |
    while read -r t; do
        echo "$t $(psql -d "$1" -XAtq -c "SELECT count(*) FROM public.\"$t\"")"
    done
}

cmd_test() {
    fetch "${1:-latest}"
    PGOPTIONS='-c client_min_messages=warning' psql -d postgres -Xq -c "DROP DATABASE IF EXISTS $TEST_DB" -c "CREATE DATABASE $TEST_DB"
    trap 'psql -d postgres -Xq -c "DROP DATABASE IF EXISTS $TEST_DB" >/dev/null 2>&1 || true; cleanup' EXIT
    pg_restore --no-owner --exit-on-error -d "$TEST_DB" "$WORK/db.dump"

    table_counts "$TEST_DB" > "$WORK/backup.counts"
    table_counts "$LIVE_DB" > "$WORK/live.counts"
    printf '\n%-28s %10s %10s\n' "Table" "Backup" "Live now"
    while read -r t n; do
        printf '%-28s %10s %10s\n' "$t" "$n" "$(awk -v t="$t" '$1 == t {print $2}' "$WORK/live.counts")"
    done < "$WORK/backup.counts"
    printf '%-28s %10s %10s\n\n' "statement files" \
        "$(tar -tzf "$WORK/statements.tar.gz" | grep -vc '/$' || true)" "$(find /statements -type f | wc -l)"

    echo "Restore test PASSED: the backup restored into a separate database (now deleted). Live data was not touched."
    echo "(Live numbers can be higher than the backup if people added data since it was taken.)"
}

cmd_restore() {
    [ $# -ge 1 ] || usage
    [ "${2:-}" = "--yes" ] || { echo "This REPLACES all live data with the backup. Re-run with --yes at the end to confirm."; exit 1; }

    others=$(psql -d postgres -XAtq -c "SELECT count(*) FROM pg_stat_activity WHERE datname = '$LIVE_DB' AND pid <> pg_backend_pid()")
    if [ "$others" != "0" ]; then
        echo "The app is still connected to the database. First run:  docker compose stop api"; exit 1
    fi

    fetch "$1"
    echo "Safety copy of the current data first (kept on the server as before-restore-*.tar.gpg):"
    BACKUP_NAME_PREFIX=before-restore BACKUP_S3_BUCKET="" /usr/local/bin/backup.sh

    echo "Restoring the database..."
    pg_restore --clean --if-exists --no-owner --single-transaction --exit-on-error -d "$LIVE_DB" "$WORK/db.dump"
    echo "Restoring statement files..."
    find /statements -mindepth 1 -maxdepth 1 -exec rm -rf {} +
    tar -C /statements -xzf "$WORK/statements.tar.gz"

    echo "Restore finished. Start the app again:  docker compose start api"
}

WORK=$(mktemp -d)
export GNUPGHOME="$WORK/gnupg"
mkdir -m 700 "$GNUPGHOME"
cleanup() { gpgconf --kill gpg-agent 2>/dev/null || true; rm -rf "$WORK"; }
trap cleanup EXIT

case "${1:-}" in
    list)    cmd_list ;;
    test)    shift; cmd_test "$@" ;;
    restore) shift; cmd_restore "$@" ;;
    *)       usage ;;
esac
