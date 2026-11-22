#!/bin/sh
# One backup: database dump + uploaded statement files -> one encrypted archive
#   -> kept in the "backups" volume (newest BACKUP_KEEP_LOCAL) -> uploaded to S3 when BACKUP_S3_BUCKET is set.
# Run by hand:  docker compose exec backup backup.sh
set -eu

if [ -z "${BACKUP_PASSPHRASE:-}" ]; then
    echo "BACKUP_PASSPHRASE is not set in .env. Backups are encrypted, so add a long passphrase (and save it in your password manager)."
    exit 1
fi

KEEP="${BACKUP_KEEP_LOCAL:-7}"
PREFIX="${BACKUP_NAME_PREFIX:-dollarshark}"   # restore.sh uses "before-restore" for its safety copy
STAMP=$(date -u +%Y%m%d-%H%M%S)
NAME="$PREFIX-$STAMP.tar.gpg"
WORK=$(mktemp -d)
export GNUPGHOME="$WORK/gnupg"
mkdir -m 700 "$GNUPGHOME"
printf '%s' "$BACKUP_PASSPHRASE" > "$WORK/passphrase"
cleanup() { gpgconf --kill gpg-agent 2>/dev/null || true; rm -rf "$WORK"; }
trap cleanup EXIT

echo "[$STAMP] Backup started"

# 1. Database (custom format = compressed, restorable with pg_restore). Connection comes from PGHOST/PGUSER/PGPASSWORD/PGDATABASE.
pg_dump --format=custom --no-owner --file="$WORK/db.dump"

# 2. Uploaded statement files
tar -C /statements -czf "$WORK/statements.tar.gz" .

# 3. One archive, encrypted with AES-256 using the passphrase
tar -C "$WORK" -cf "$WORK/backup.tar" db.dump statements.tar.gz
mkdir -p /backups
gpg --batch --quiet --pinentry-mode loopback --passphrase-file "$WORK/passphrase" --no-symkey-cache \
    --symmetric --cipher-algo AES256 --output "/backups/$NAME.part" "$WORK/backup.tar"

# 4. Check the archive can be decrypted and read before calling it a backup
gpg --batch --quiet --pinentry-mode loopback --passphrase-file "$WORK/passphrase" --no-symkey-cache \
    --decrypt "/backups/$NAME.part" | tar -tf - > /dev/null
mv "/backups/$NAME.part" "/backups/$NAME"
echo "Saved /backups/$NAME ($(du -h "/backups/$NAME" | cut -f1))"

# 5. Keep only the newest $KEEP archives on the server
ls -1 /backups/"$PREFIX"-*.tar.gpg | sort -r | tail -n +"$((KEEP + 1))" | xargs -r rm -f

# 6. Off-server copy in S3
if [ -n "${BACKUP_S3_BUCKET:-}" ]; then
    aws s3 cp "/backups/$NAME" "s3://$BACKUP_S3_BUCKET/backups/$NAME" --only-show-errors --no-progress
    echo "Uploaded to s3://$BACKUP_S3_BUCKET/backups/$NAME"
else
    echo "BACKUP_S3_BUCKET is not set: this copy stays on the server only."
fi

if [ "$PREFIX" = "dollarshark" ]; then
    date -u +%s > /backups/.last-success
fi
echo "Backup finished"
