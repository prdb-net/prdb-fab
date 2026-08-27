#!/bin/bash
#
# ADR 0034: the container starts as root and does not stay there. This works out
# which identity the user's files should belong to, hands the tool's own data
# directory over to it, and execs the application under it.
#
# exec, not a supervisor: the application ends up as PID 1, so `docker stop`
# reaches it and a file move in progress is interrupted properly rather than the
# container being killed once the timeout runs out.
#
# bash rather than sh, although nothing below is a bashism: /bin/sh here is dash,
# and dash drops environment variables whose names are not valid shell
# identifiers instead of passing them on. Every .NET logging category has a dot
# in it — `Logging__LogLevel__Prdb.Fab` above all, which ADR 0043 made the one
# knob anybody is asked to change — so under dash the setting never reaches the
# application and nothing says so. `docker exec env` still shows it, because that
# is the container's configured environment rather than the process's;
# /proc/1/environ is where it is missing. smoke-test.sh checks that it arrives.

set -eu

say() { printf 'entrypoint: %s\n' "$*"; }
refuse() { printf 'entrypoint: %s\n' "$*" >&2; exit 1; }

data_directory="${FAB_DATA_DIRECTORY:-/data}"
umask_value="${UMASK:-022}"

# A mistyped umask is worth catching here: `umask 0o22` or `umask rwx` would
# otherwise either fail obscurely or silently leave the default in place, and the
# consequence — files the NAS user cannot read — turns up much later.
case "$umask_value" in
    [0-7][0-7][0-7] | [0-7][0-7][0-7][0-7]) ;;
    *) refuse "UMASK is '$umask_value', which is not a three or four digit octal mask." ;;
esac

umask "$umask_value"

# Someone who started the container with Compose's own `user:` has answered the
# identity question already, and this process has neither the need nor the
# permission to answer it again.
if [ "$(id -u)" -ne 0 ]; then
    say "Running as uid $(id -u) rather than root, so PUID, PGID and the ownership of $data_directory are left as they are."
    mkdir -p "$data_directory" \
        || refuse "Could not create $data_directory as uid $(id -u). Mount it, or give the user it is mounted for permission to write there."
    exec "$@"
fi

puid="${PUID:-1000}"
pgid="${PGID:-1000}"

case "$puid" in '' | *[!0-9]*) refuse "PUID is '$puid', which is not a number." ;; esac
case "$pgid" in '' | *[!0-9]*) refuse "PGID is '$pgid', which is not a number." ;; esac

# An id that already belongs to something in the image keeps the name it has
# there; only an id nothing answers to gets one of ours. Adding a second name for
# an existing id is what makes `ls -l` inside the container disagree with `ls -l`
# on the NAS.
group_name="$(getent group "$pgid" | cut -d: -f1)"
if [ -z "$group_name" ]; then
    group_name=fab
    groupadd --gid "$pgid" "$group_name"
fi

user_name="$(getent passwd "$puid" | cut -d: -f1)"
if [ -z "$user_name" ]; then
    user_name=fab
    useradd \
        --uid "$puid" \
        --gid "$pgid" \
        --no-create-home \
        --home-dir "$data_directory" \
        --shell /usr/sbin/nologin \
        "$user_name"
fi

if [ "$puid" -eq 0 ]; then
    say "PUID is 0, so the application will run as root and every file it files will be owned by root. Set PUID and PGID to the user your library belongs to."
fi

mkdir -p "$data_directory" \
    || refuse "Could not create $data_directory. It is where the database and the log live, so the tool cannot start without it."

# ADR 0034: the tool's own volume, and nothing else. Taking ownership of a
# library recursively is slow on a NAS and is not this tool's business — the
# media keeps the owner it arrived with, and umask above is what decides who can
# read what the tool writes into it.
#
# A share that refuses chown outright is not fatal: what matters is whether the
# database can be written, and the migrator says so plainly if it cannot.
if ! chown --recursive "$puid:$pgid" "$data_directory" 2>/dev/null; then
    say "Could not take ownership of $data_directory — some network shares do not allow it. Carrying on; the tool will say so at startup if it cannot write there."
fi

# Anything that looks for a home directory finds the data volume, which is the
# one place in this container that survives a restart and is writable by the
# identity below.
HOME="$data_directory"
export HOME

say "Starting as $user_name ($puid:$pgid), umask $umask_value, data directory $data_directory."

exec setpriv --reuid "$puid" --regid "$pgid" --init-groups -- "$@"
