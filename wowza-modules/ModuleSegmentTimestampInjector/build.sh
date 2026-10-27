#!/usr/bin/env bash
# Builds the module against the Wowza host's own libs (so it always matches the running
# Wowza version) and installs the jar into its lib folder. Run from any shell with ssh
# (Git Bash works): ./build.sh            build + install, then restart Wowza yourself
#                   ./build.sh --check    build only, to see that it compiles
set -euo pipefail

HOST="${WOWZA_HOST:-user@192.168.0.114}"
WOWZA_DIR="${WOWZA_DIR:-/home/user/wowza}"
JAR="wowza-timestamp-module.jar"
MODE="${1:-install}"

cd "$(dirname "$0")"

src=$(ssh "$HOST" mktemp -d)
scp -q ./*.java "$HOST:$src/"

ssh "$HOST" bash -s -- "$src" "$WOWZA_DIR" "$JAR" "$MODE" <<'EOF'
set -euo pipefail
src=$1; wowza=$2; jar=$3; mode=$4
trap 'rm -rf "$src"' EXIT

docker run --rm -v "$wowza/lib:/libs:ro" -v "$src:/src" eclipse-temurin:17-jdk sh -c \
    "mkdir -p /tmp/out && javac -Xlint:all -classpath '/libs/*' -d /tmp/out /src/*.java && jar cf /src/$jar -C /tmp/out ."

if [ "$mode" = "--check" ]; then
    echo "Build OK (not installed)."
    exit 0
fi

mkdir -p "$wowza/backup"
if [ -f "$wowza/lib/$jar" ]; then
    cp -p "$wowza/lib/$jar" "$wowza/backup/$jar.$(date +%Y%m%d-%H%M%S)"
fi
cp "$src/$jar" "$wowza/lib/$jar"
echo "Installed $wowza/lib/$jar (previous one backed up in $wowza/backup)."
EOF

if [ "$MODE" != "--check" ]; then
    echo "Restart Wowza to load it:  ssh $HOST docker restart wowza-engine"
fi
