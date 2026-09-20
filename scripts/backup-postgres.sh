#!/usr/bin/env bash
# Nightly Postgres backup — runs inside MSYS bash via Windows Task Scheduler.
# Schedule: see scripts/install-backup-task.ps1
set -euo pipefail

# Overridden by the scheduler; this default is only for a manual run.
BACKUP_DIR="${BACKUP_DIR:-/var/backups/finance}"
RETAIN_DAYS="${RETAIN_DAYS:-30}"
CONTAINER="${CONTAINER:-finance-postgres}"
DB_USER="${DB_USER:-postgres}"
DB_NAME="${DB_NAME:-financemanagement}"

mkdir -p "$BACKUP_DIR"

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
OUT="$BACKUP_DIR/finance_${TIMESTAMP}.sql.gz"

fail() {
    echo "[$(date)] BACKUP FAILED — $1" >> "$BACKUP_DIR/backup.log"
    rm -f "$OUT"
    exit 1
}

# Plain SQL dump piped straight into gzip — keeps backups portable + small.
#
# The pipeline is wrapped in `if !` on purpose. Under `set -e` + `pipefail` a
# bare pipeline ABORTS THE SCRIPT the moment pg_dump fails, so every line below
# — including the failure branch that was supposed to log and delete — is
# unreachable. What that produced on the Windows host: `docker` was absent at
# 03:30, printed "The command 'docker' could not be found" TO STDOUT, that text
# was gzipped into $OUT, and the script died before logging anything. Seven
# consecutive nights left a 178-byte file with a plausible name and timestamp,
# an empty backup.log, and no indication anything was wrong.
#
# Capture the whole PIPESTATUS array in ONE assignment: any assignment resets
# it, so reading [0] and then [1] on separate lines silently yields 0 for [1].
RC=0
if ! docker exec -i "$CONTAINER" pg_dump -U "$DB_USER" "$DB_NAME" \
        2>"$BACKUP_DIR/last-error.log" | gzip > "$OUT"; then
    RC=1
fi
STATUS=("${PIPESTATUS[@]}")
DUMP_RC="${STATUS[0]:-$RC}"
GZIP_RC="${STATUS[1]:-0}"

if [ "$DUMP_RC" -ne 0 ] || [ "$GZIP_RC" -ne 0 ]; then
    fail "dump=$DUMP_RC gzip=$GZIP_RC — see last-error.log"
fi

SIZE=$(stat -c %s "$OUT" 2>/dev/null || stat -f %z "$OUT")

# Exit status alone is not enough: anything that writes to stdout instead of
# stderr gets gzipped into a well-formed file. Verify the archive decompresses
# and actually contains table data before calling it a backup.
COPY_COUNT=$(gzip -dc "$OUT" 2>/dev/null | grep -c '^COPY ' || true)
if [ "${COPY_COUNT:-0}" -lt 1 ]; then
    fail "dump has no COPY blocks (${SIZE} bytes) — not a real dump"
fi

echo "[$(date)] OK  $OUT  ${SIZE} bytes  ${COPY_COUNT} tables" >> "$BACKUP_DIR/backup.log"

# Retention: delete dumps older than RETAIN_DAYS days.
find "$BACKUP_DIR" -type f -name "finance_*.sql.gz" -mtime +${RETAIN_DAYS} -delete 2>/dev/null || true

# Sanity: keep only the last 90 dumps regardless, in case of clock drift / many runs.
ls -1t "$BACKUP_DIR"/finance_*.sql.gz 2>/dev/null | tail -n +91 | xargs -r rm -f
