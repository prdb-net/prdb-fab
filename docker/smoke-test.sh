#!/usr/bin/env bash
#
# Starts a built image and checks the claims ADR 0034 makes about it, in the only
# place they can be checked: a running container. ADR 0042 handed "that the image
# runs" to ADR 0044's CI rather than to the test suite, and this is it.
#
#   1. It comes up, migrates its database and answers.
#   2. What it writes into its data volume belongs to PUID:PGID.
#   3. A variable with a dot in its name survives the shell the entrypoint runs
#      under, which is the whole reason that shell is bash.
#   4. ADR 0043's rolling log file exists on the data volume, which is what makes
#      "send me your log" a file copy.
#   5. A Backup written by one container restores into a second one, and the
#      password that comes back in the file is the one that signs in. That is
#      ADR 0009's whole promise, exercised where it is actually made: two
#      containers, two data volumes, and no service outside them.
#   6. `docker stop` stops it, rather than the daemon killing it once the timeout
#      runs out.
#
# Usage: docker/smoke-test.sh <image> [host-port]

set -euo pipefail

image="${1:?Usage: docker/smoke-test.sh <image> [host-port]}"
port="${2:-18080}"

readonly test_uid=1234
readonly test_gid=5678
readonly dotted_variable="Logging__LogLevel__Prdb.Fab"
readonly startup_timeout_seconds=180
readonly stop_timeout_seconds=10
readonly password="a password from the smoke test"

container=""
second=""
workspace="$(mktemp --directory)"

cleanup() {
    if [ -n "$container" ]; then
        docker rm --force "$container" >/dev/null 2>&1 || true
    fi

    if [ -n "$second" ]; then
        docker rm --force "$second" >/dev/null 2>&1 || true
    fi

    # The container wrote into these as another user, so removing them from here
    # would need root. Borrowing the image's own root is cheaper than sudo and
    # works the same way on a laptop and on a runner.
    docker run --rm --volume "$workspace:/workspace" --entrypoint /bin/sh "$image" \
        -c 'rm -rf /workspace/data /workspace/restored' >/dev/null 2>&1 || true
    rmdir "$workspace" 2>/dev/null || true
}
trap cleanup EXIT

fail() {
    echo "FAIL: $*" >&2

    if [ -n "$container" ]; then
        echo "The container said:" >&2
        docker logs "$container" 2>&1 | sed 's/^/    /' >&2
    fi

    exit 1
}

pass() { echo "ok: $*"; }

# Waits for a container to answer, or fails saying which one did not.
wait_for_health() {
    local at="$1"
    local what="$2"

    for _ in $(seq "$startup_timeout_seconds"); do
        if curl --silent --fail "http://localhost:$at/api/health" >/dev/null 2>&1; then
            return 0
        fi

        sleep 1
    done

    fail "no answer from $what on port $at within ${startup_timeout_seconds}s"
}

mkdir -p "$workspace/data" "$workspace/restored"

echo "Starting $image"
container="$(docker run --detach \
    --publish "$port:8080" \
    --volume "$workspace/data:/data" \
    --env "PUID=$test_uid" \
    --env "PGID=$test_gid" \
    --env "$dotted_variable=Debug" \
    "$image")"

# 1. It comes up and answers.
answered=false
for _ in $(seq "$startup_timeout_seconds"); do
    if curl --silent --fail "http://localhost:$port/api/health" >/dev/null 2>&1; then
        answered=true
        break
    fi

    if [ -z "$(docker ps --quiet --filter "id=$container")" ]; then
        fail "the container exited before it answered"
    fi

    sleep 1
done

[ "$answered" = true ] || fail "no answer from /api/health within ${startup_timeout_seconds}s"
pass "it comes up and answers"

# 2. What it wrote into the data volume belongs to the identity it was given.
database_owner="$(stat --format '%u:%g' "$workspace/data/prdb-fab.db")"
[ "$database_owner" = "$test_uid:$test_gid" ] \
    || fail "the database belongs to $database_owner rather than to $test_uid:$test_gid"
pass "the database belongs to PUID:PGID"

# 3. The dotted variable arrived, with its value. Asked of the application's own
#    process rather than of the container: `docker exec env` reports the
#    environment the container was configured with, which is not the question —
#    what matters is what survived the entrypoint's shell, and only PID 1 knows
#    that. Read as the identity PID 1 runs as, because reading another user's
#    environ needs a capability this container deliberately does not have.
application_environment="$(docker exec --user "$test_uid" "$container" \
    sh -c 'tr "\0" "\n" < /proc/1/environ' 2>/dev/null)" \
    || fail "could not read the environment of the application process"

# Fixed strings and whole lines: the name being matched contains the dot this
# check is about, and as a pattern it would match anything in that position.
echo "$application_environment" \
    | grep --quiet --fixed-strings --line-regexp "$dotted_variable=Debug" \
    || fail "$dotted_variable did not reach the application — the entrypoint's shell dropped it"
pass "a logging category with a dot in it reaches the application intact"

# 4. ADR 0043's rolling file is on the volume the user mounts.
compgen -G "$workspace/data/logs/prdb-fab-*.log" >/dev/null \
    || fail "no rolling log file under /data/logs"
pass "the log is a file on the data volume"

# 5. ADR 0009's loop, end to end, across two containers. Nothing outside this
#    script is involved: the tool needs no prdb, no indexer and no SABnzbd to
#    write a Backup or to take one.
cookies="$workspace/cookies.txt"

curl --silent --fail --cookie-jar "$cookies" \
    --header 'Content-Type: application/json' \
    --data "$(printf '{"password":"%s"}' "$password")" \
    "http://localhost:$port/api/access/password" >/dev/null \
    || fail "the first container would not take a password"

curl --silent --fail --cookie "$cookies" --request POST \
    --output "$workspace/backup.json" \
    "http://localhost:$port/api/backup/export" \
    || fail "the export did not produce a file"

# ADR 0057: readable throughout, which is what makes this checkable from a shell
# at all — and what the check is here to keep true.
grep --quiet '"formatVersion"' "$workspace/backup.json" \
    || fail "the exported file is not a Backup document"
pass "a Backup is written and is a readable document"

restored_port=$((port + 1))
second="$(docker run --detach \
    --publish "$restored_port:8080" \
    --volume "$workspace/restored:/data" \
    --env "PUID=$test_uid" \
    --env "PGID=$test_gid" \
    "$image")"

wait_for_health "$restored_port" "the second container"

# Anonymous, because ADR 0010's window is open: this container has no password
# yet, and the one in the file is what closes it.
restore_body="$workspace/restore.json"
python3 - "$workspace/backup.json" "$restore_body" <<'PYTHON'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as backup:
    document = backup.read()

# Neither root is answered: a container that has only just been started has
# filed nothing, so the document has no path under either of them.
with open(sys.argv[2], "w", encoding="utf-8") as body:
    json.dump({"document": document, "roots": {"library": None, "downloads": None}}, body)
PYTHON

outcome="$(curl --silent --fail \
    --header 'Content-Type: application/json' \
    --data "@$restore_body" \
    "http://localhost:$restored_port/api/backup/restore" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["outcome"])')" \
    || fail "the restore did not answer"

[ "$outcome" = "Restored" ] || fail "the restore answered $outcome rather than Restored"

signed_in="$(curl --silent --fail \
    --header 'Content-Type: application/json' \
    --data "$(printf '{"password":"%s"}' "$password")" \
    "http://localhost:$restored_port/api/access/sign-in" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["outcome"])')" \
    || fail "the restored container did not answer the sign-in"

[ "$signed_in" = "SignedIn" ] \
    || fail "the password from the Backup did not sign in: $signed_in"
pass "a Backup restores into a second container and its password signs in"

docker rm --force "$second" >/dev/null
second=""

# 6. It stops when it is asked to, rather than being killed on the timeout.
started_stopping="$(date +%s)"
docker stop --timeout "$stop_timeout_seconds" "$container" >/dev/null
took=$(($(date +%s) - started_stopping))

[ "$took" -lt "$stop_timeout_seconds" ] \
    || fail "it took ${took}s to stop, which means the signal did not reach PID 1"
pass "docker stop reaches the application (${took}s)"

echo "All checks passed for $image."
