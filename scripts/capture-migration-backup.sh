#!/usr/bin/env bash
# Refresh the migration backup on the external drive.
#
# Run this AGAIN right before wiping the machine. The code is on GitHub, but
# these are not, and they go stale the moment you keep using the app:
#   - the database
#   - .env, docker-compose.override.yml, the Firebase key
#   - CLAUDE.md and anything else .gitignore deliberately keeps off GitHub
#
# Safe to run any time: it only reads the live system, and it never touches the
# hand-written docs in the destination (README-FIRST.md, CLAUDE.md, docs/,
# tasks/). Only the payload and CAPTURED.md are rewritten.
#
#   bash scripts/capture-migration-backup.sh                       # default dest
#   bash scripts/capture-migration-backup.sh "/mnt/f/finance management"
set -euo pipefail

DEST="${1:-/mnt/f/finance management}"
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="${CONTAINER:-finance-postgres}"
DB_USER="${DB_USER:-postgres}"
DB_NAME="${DB_NAME:-financemanagement}"
VOLUME="${VOLUME:-finance-management_postgres-data}"

say() { echo "==> $*"; }
die() { echo "ERROR: $*" >&2; exit 1; }

[ -d "$SRC/.git" ]  || die "$SRC is not a git repo"
docker version >/dev/null 2>&1 || die "docker daemon not reachable"
docker inspect "$CONTAINER" >/dev/null 2>&1 || die "$CONTAINER is not running — start the stack first"

mkdir -p "$DEST"/{repo,secrets,database} || die "cannot write to $DEST (is the drive mounted?)"
cd "$SRC"

say "git bundle (full history)"
git bundle create "$DEST/repo/finance-management.bundle" --all >/dev/null
git bundle verify "$DEST/repo/finance-management.bundle" >/dev/null || die "bundle failed verification"

say "working tree tarball (INCLUDES gitignored files — that is the point)"
tar czf "$DEST/repo/worktree.tar.gz" \
    --exclude='./.git' --exclude='./startup.log' \
    --exclude='./frontend/node_modules' \
    --exclude='./backend/bin' --exclude='./backend/obj' \
    --exclude='./backend.tests/bin' --exclude='./backend.tests/obj' \
    -C "$SRC" .

# Plain copy too, so the project instructions can be read without extracting.
[ -f "$SRC/CLAUDE.md" ] && cp "$SRC/CLAUDE.md" "$DEST/repo/CLAUDE.md"

say "secrets (tarred so 0600 survives exFAT, which stores no POSIX modes)"
SECRET_FILES=()
for f in .env docker-compose.override.yml backend/firebase-service-account.json; do
    [ -f "$SRC/$f" ] && SECRET_FILES+=("$f")
done
[ "${#SECRET_FILES[@]}" -gt 0 ] || die "no secrets found to back up"
tar czf "$DEST/secrets/secrets.tar.gz" -C "$SRC" "${SECRET_FILES[@]}"

say "database — logical dump"
docker exec "$CONTAINER" pg_dumpall -U "$DB_USER" > "$DEST/database/pgdumpall.sql"
grep -q '^COPY ' "$DEST/database/pgdumpall.sql" || die "pg_dumpall produced no COPY blocks — refusing to keep it"

say "database — volume tarball (byte-exact PGDATA; needs the same major version)"
docker run --rm -v "$VOLUME":/data:ro -v "$DEST/database":/backup \
    alpine tar czf /backup/postgres-data.tar.gz -C /data .

say "row counts for post-restore verification"
docker exec "$CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -A -F$'\t' -t -c "
SELECT 'Transactions', count(*) FROM \"Transactions\"
UNION ALL SELECT 'Categories', count(*) FROM \"Categories\"
UNION ALL SELECT 'Budgets', count(*) FROM \"Budgets\"
UNION ALL SELECT 'CategoryBudgets', count(*) FROM \"CategoryBudgets\"
UNION ALL SELECT 'BlockedIps', count(*) FROM \"BlockedIps\"
UNION ALL SELECT 'LoginAttempts', count(*) FROM \"LoginAttempts\"
UNION ALL SELECT 'EmailNotifications', count(*) FROM \"EmailNotifications\"
UNION ALL SELECT '__EFMigrationsHistory', count(*) FROM \"__EFMigrationsHistory\"
ORDER BY 1;" > "$DEST/database/row-counts.tsv"

say "CAPTURED.md"
{
    echo "# Capture manifest"
    echo
    echo "Regenerate with \`bash scripts/capture-migration-backup.sh\`."
    echo "**Everything below is rewritten on each run — never edit by hand.**"
    echo
    echo "| | |"
    echo "|---|---|"
    echo "| Captured (UTC) | $(date -u +%Y-%m-%dT%H:%M:%SZ) |"
    echo "| Commit | \`$(git rev-parse HEAD)\` |"
    echo "| Subject | $(git log -1 --format=%s) |"
    echo "| Branch | $(git rev-parse --abbrev-ref HEAD) |"
    echo "| Pushed to origin | $(git rev-parse HEAD) == $(git rev-parse '@{u}' 2>/dev/null || echo 'NO UPSTREAM') |"
    echo "| Uncommitted changes | $(git status --porcelain | wc -l) file(s) |"
    echo "| Postgres | $(docker exec "$CONTAINER" postgres --version | awk '{print $3}') |"
    echo "| Source host | $(hostname) |"
    echo
    echo "## Row counts at capture"
    echo
    echo "| Table | Rows |"
    echo "|-------|-----:|"
    while IFS=$'\t' read -r t n; do echo "| $t | $n |"; done < "$DEST/database/row-counts.tsv"
    echo
    if [ "$(git status --porcelain | wc -l)" -ne 0 ]; then
        echo "> ⚠️ The working tree had uncommitted changes at capture time. They are"
        echo "> inside \`repo/worktree.tar.gz\` but NOT in the bundle's history."
        echo
    fi
    echo "## Files"
    echo
    echo '```'
    ( cd "$DEST" && find repo secrets database -type f -printf '%-38p %10s bytes\n' | sort )
    echo '```'
} > "$DEST/CAPTURED.md"

say "checksums"
( cd "$DEST" && find repo secrets database -type f ! -name SHA256SUMS.txt -print0 \
    | sort -z | xargs -0 sha256sum > SHA256SUMS.txt )

say "verifying what was just written"
( cd "$DEST" && sha256sum -c SHA256SUMS.txt )

echo
say "done — $(du -sh "$DEST" | cut -f1) at $DEST"
echo "    commit $(git rev-parse --short HEAD)  |  $(wc -l < "$DEST/database/row-counts.tsv") tables recorded"
