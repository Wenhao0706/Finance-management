#!/usr/bin/env bash
# Nightly Postgres backup — runs inside MSYS bash via Windows Task Scheduler.
# Schedule: see scripts/install-backup-task.ps1
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-D:/backups/finance}"
RETAIN_DAYS="${RETAIN_DAYS:-30}"
CONTAINER="${CONTAINER:-finance-postgres}"
DB_USER="${DB_USER:-postgres}"
DB_NAME="${DB_NAME:-financemanagement}"

mkdir -p "$BACKUP_DIR"

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
OUT="$BACKUP_DIR/finance_${TIMESTAMP}.sql.gz"

# Plain SQL dump piped straight into gzip — keeps backups portable + small.
# pg_dump's exit code propagates via PIPESTATUS so we fail loudly if it errors.
set +u  # PIPESTATUS array indexing trips nounset
docker exec -i "$CONTAINER" pg_dump -U "$DB_USER" "$DB_NAME" 2>"$BACKUP_DIR/last-error.log" | gzip > "$OUT"
DUMP_RC="${PIPESTATUS[0]:-0}"
GZIP_RC="${PIPESTATUS[1]:-0}"
set -u

if [ "${DUMP_RC:-0}" -ne 0 ] || [ "${GZIP_RC:-0}" -ne 0 ]; then
    echo "[$(date)] BACKUP FAILED (dump=$DUMP_RC gzip=$GZIP_RC)" >> "$BACKUP_DIR/backup.log"
    rm -f "$OUT"
    exit 1
fi

SIZE=$(stat -c %s "$OUT" 2>/dev/null || stat -f %z "$OUT")
echo "[$(date)] OK  $OUT  ${SIZE} bytes" >> "$BACKUP_DIR/backup.log"

# Retention: delete dumps older than RETAIN_DAYS days.
find "$BACKUP_DIR" -type f -name "finance_*.sql.gz" -mtime +${RETAIN_DAYS} -delete 2>/dev/null || true

# Sanity: keep only the last 90 dumps regardless, in case of clock drift / many runs.
ls -1t "$BACKUP_DIR"/finance_*.sql.gz 2>/dev/null | tail -n +91 | xargs -r rm -f
