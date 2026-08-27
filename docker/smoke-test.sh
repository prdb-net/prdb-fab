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
#   5. `docker stop` stops it, rather than the daemon killing it once the timeout
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

container=""
workspace="$(mktemp --directory)"

cleanup() {
    if [ -n "$container" ]; then
        docker rm --force "$container" >/dev/null 2>&1 || true
    fi

    # The container wrote into these as another user, so removing them from here
    # would need root. Borrowing the image's own root is cheaper than sudo and
    # works the same way on a laptop and on a runner.
    docker run --rm --volume "$workspace:/workspace" --entrypoint /bin/sh "$image" \
        -c 'rm -rf /workspace/data' >/dev/null 2>&1 || true
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

mkdir -p "$workspace/data"

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

# 5. It stops when it is asked to, rather than being killed on the timeout.
started_stopping="$(date +%s)"
docker stop --timeout "$stop_timeout_seconds" "$container" >/dev/null
took=$(($(date +%s) - started_stopping))

[ "$took" -lt "$stop_timeout_seconds" ] \
    || fail "it took ${took}s to stop, which means the signal did not reach PID 1"
pass "docker stop reaches the application (${took}s)"

echo "All checks passed for $image."
