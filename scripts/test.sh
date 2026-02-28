#!/usr/bin/env bash
set -euo pipefail

TEST_PORT="${STDB_TEST_PORT:-3001}"
TEST_DB="tvs"
MODULE_PATH="./spacetimedb"
SERVER_URL="http://localhost:${TEST_PORT}"
SERVER_PID=""
DATA_DIR=""

cleanup() {
    if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
        echo "Stopping SpacetimeDB test server (pid $SERVER_PID)..."
        kill "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || true
    fi
    if [ -n "$DATA_DIR" ] && [ -d "$DATA_DIR" ]; then
        rm -rf "$DATA_DIR"
    fi
}
trap cleanup EXIT

DATA_DIR="$(mktemp -d)"
echo "=== Starting in-memory SpacetimeDB on port ${TEST_PORT} (data-dir: ${DATA_DIR}) ==="
spacetime start --in-memory --listen-addr "0.0.0.0:${TEST_PORT}" --data-dir "$DATA_DIR" &
SERVER_PID=$!

echo "Waiting for server to be ready..."
for i in $(seq 1 30); do
    if curl -sf "${SERVER_URL}/v1/identity" -X POST -o /dev/null 2>/dev/null; then
        echo "Server ready after ${i}s"
        break
    fi
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then
        echo "ERROR: Server process died"
        exit 1
    fi
    sleep 1
done

if ! curl -sf "${SERVER_URL}/v1/identity" -X POST -o /dev/null 2>/dev/null; then
    echo "ERROR: Server did not become ready in time"
    exit 1
fi

echo ""
echo "=== Publishing module to ${TEST_DB} ==="
spacetime publish "$TEST_DB" --clear-database -y --module-path "$MODULE_PATH" --server "$SERVER_URL"

echo ""
echo "=== Running tests ==="
STDB_TEST_URL="$SERVER_URL" STDB_TEST_DB="$TEST_DB" dotnet test ./tests
TEST_EXIT=$?

echo ""
if [ $TEST_EXIT -eq 0 ]; then
    echo "=== All tests passed ==="
else
    echo "=== Tests failed (exit code: $TEST_EXIT) ==="
fi

exit $TEST_EXIT
