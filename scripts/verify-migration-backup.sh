#!/usr/bin/env bash
# Rehearse a clean-machine restore using ONLY the migration backup.
# Run this BEFORE wiping a host: a backup nobody has restored is a hypothesis.
#   bash scripts/verify-migration-backup.sh [/path/to/backup]
# Touches nothing live: temp dir, throwaway volume, throwaway container, spare port.
set -uo pipefail

BK="${1:-/mnt/f/finance management}"
T="/tmp/restore-rehearsal"
APP="$T/Finance-management"
VOL="rehearsal_pgdata"
CT="rehearsal-postgres"
PORT=55432
fails=0
ok()   { echo "  PASS  $*"; }
bad()  { echo "  FAIL  $*"; fails=$((fails+1)); }

cleanup() {
    docker rm -f "$CT"    >/dev/null 2>&1 || true
    docker volume rm "$VOL" >/dev/null 2>&1 || true
    rm -rf "$T"
}
trap cleanup EXIT
cleanup

echo "############ 0. integrity"
( cd "$BK" && sha256sum -c SHA256SUMS.txt >/dev/null 2>&1 ) && ok "all checksums" || bad "checksums"

echo
echo "############ 1. restore code from the BUNDLE (no GitHub)"
mkdir -p "$T"
git clone -q "$BK/repo/finance-management.bundle" "$APP" 2>/dev/null
CAPTURED=$(grep '| Commit |' "$BK/CAPTURED.md" | tr -d '`' | awk '{print $4}')
HEAD=$(git -C "$APP" rev-parse HEAD)
[ "$HEAD" = "$CAPTURED" ] && ok "HEAD matches CAPTURED.md ($(echo "$HEAD" | cut -c1-7))" \
                          || bad "HEAD $HEAD != captured $CAPTURED"

echo
echo "############ 2. what the clone is MISSING (the gitignored set)"
for f in CLAUDE.md .env docker-compose.override.yml backend/firebase-service-account.json; do
    [ -e "$APP/$f" ] && bad "$f unexpectedly present in clone" || echo "  (expected missing) $f"
done

echo
echo "############ 3. restore secrets + gitignored files from the backup"
tar xzf "$BK/secrets/secrets.tar.gz" -C "$APP"
tar xzf "$BK/repo/worktree.tar.gz" -C "$APP" ./CLAUDE.md 2>/dev/null
for f in CLAUDE.md .env docker-compose.override.yml backend/firebase-service-account.json; do
    [ -e "$APP/$f" ] && ok "restored $f" || bad "MISSING after restore: $f"
done
MODE=$(stat -c %a "$APP/backend/firebase-service-account.json")
[ "$MODE" = "600" ] && ok "firebase key mode 0600" || bad "firebase key mode $MODE"
python3 -c "import json;json.load(open('$APP/backend/firebase-service-account.json'))" 2>/dev/null \
    && ok "firebase key is valid JSON" || bad "firebase key not valid JSON"

echo
echo "############ 4. compose renders with the restored config"
cd "$APP"
sed -i "s|^HOST_PROJECT_DIR=.*|HOST_PROJECT_DIR=$APP|" .env
if docker compose config >/dev/null 2>&1; then
    ok "docker compose config valid"
else
    bad "compose config failed:"; docker compose config 2>&1 | head -3 | sed 's/^/        /'
fi
SRC=$(docker compose config | grep -A1 'firebase-service-account' | grep source: | awk '{print $2}')
[ "$SRC" = "$APP/backend/firebase-service-account.json" ] \
    && ok "firebase bind source resolves into the restored tree" || bad "bind source = $SRC"
[ -f "$SRC" ] && ok "bind source is a FILE (not the empty-dir trap)" || bad "bind source is not a file"

echo
echo "############ 5. restore the database into a throwaway volume"
docker volume create "$VOL" >/dev/null
docker run --rm -v "$VOL":/data -v "$BK/database":/backup:ro \
    alpine tar xzf /backup/postgres-data.tar.gz -C /data >/dev/null 2>&1
PW=$(grep '^POSTGRES_PASSWORD=' .env | cut -d= -f2-)
docker run -d --name "$CT" -v "$VOL":/var/lib/postgresql/data \
    -e POSTGRES_PASSWORD="$PW" -p 127.0.0.1:$PORT:5432 postgres:16-alpine >/dev/null 2>&1
for i in $(seq 1 45); do docker exec "$CT" pg_isready -U postgres >/dev/null 2>&1 && break; sleep 2; done
docker exec "$CT" pg_isready -U postgres >/dev/null 2>&1 && ok "restored DB starts and accepts connections" \
    || { bad "restored DB never became ready"; docker logs --tail 8 "$CT" 2>&1 | sed 's/^/        /'; }

echo
echo "############ 6. row counts vs what was captured"
docker exec "$CT" psql -U postgres -d financemanagement -A -F$'\t' -t -c "
SELECT 'Transactions', count(*) FROM \"Transactions\"
UNION ALL SELECT 'Categories', count(*) FROM \"Categories\"
UNION ALL SELECT 'Budgets', count(*) FROM \"Budgets\"
UNION ALL SELECT 'CategoryBudgets', count(*) FROM \"CategoryBudgets\"
UNION ALL SELECT 'BlockedIps', count(*) FROM \"BlockedIps\"
UNION ALL SELECT 'LoginAttempts', count(*) FROM \"LoginAttempts\"
UNION ALL SELECT 'EmailNotifications', count(*) FROM \"EmailNotifications\"
UNION ALL SELECT '__EFMigrationsHistory', count(*) FROM \"__EFMigrationsHistory\"
ORDER BY 1;" > /tmp/rehearsal-rows.tsv 2>/dev/null
if diff -q /tmp/rehearsal-rows.tsv "$BK/database/row-counts.tsv" >/dev/null 2>&1; then
    ok "row counts identical to capture"
    sed 's/^/        /' /tmp/rehearsal-rows.tsv
else
    bad "row counts DIFFER"; diff "$BK/database/row-counts.tsv" /tmp/rehearsal-rows.tsv | sed 's/^/        /'
fi
rm -f /tmp/rehearsal-rows.tsv

echo
echo "############ 7. the docs a fresh session must find"
for d in CLAUDE.md README-FIRST.md CAPTURED.md docs/ARCHITECTURE-ESSENTIALS.md \
         tasks/hosting/current.md tasks/hosting/decisions/claude-code-setup.md \
         tasks/hosting/decisions/linux-install.md tasks/hosting/decisions/restore-procedure.md \
         tasks/hosting/decisions/windows-era-gotchas.md; do
    [ -f "$BK/$d" ] && ok "$d" || bad "MISSING DOC: $d"
done

echo
echo "############ RESULT"
[ "$fails" -eq 0 ] && echo "  ALL CHECKS PASSED — the backup restores cleanly on its own" \
                   || echo "  $fails CHECK(S) FAILED"
exit "$fails"
