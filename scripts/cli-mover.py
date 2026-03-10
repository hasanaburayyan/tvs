#!/usr/bin/env python3
import subprocess
import sys
import tty
import termios
import json

if len(sys.argv) < 3:
    print("Usage: cli-mover.py <game_session_id> <player_name>")
    sys.exit(1)

GAME_SESSION_ID = int(sys.argv[1])
PLAYER_NAME = sys.argv[2]
DB_NAME = "tvs"
STEP = 0.5

x, y, z = 0.0, 2.0, 0.0

def teleport(dx, dz):
    global x, z
    x += dx
    z += dz
    pos = json.dumps({"x": round(x, 4), "y": round(y, 4), "z": round(z, 4)})
    subprocess.run(
        ["spacetime", "call", DB_NAME, "teleport_player",
         str(GAME_SESSION_ID), json.dumps(PLAYER_NAME), pos],
        capture_output=True,
    )
    print(f"\r  pos: x={x:.2f} y={y:.2f} z={z:.2f}    ", end="", flush=True)

print(f"CLI Mover (teleport) — game={GAME_SESSION_ID}, player={PLAYER_NAME}, step={STEP}")
print("WASD to move, Q to quit")
teleport(0, 0)

fd = sys.stdin.fileno()
old = termios.tcgetattr(fd)
try:
    tty.setraw(fd)
    while True:
        ch = sys.stdin.read(1).lower()
        if ch == "q":
            break
        elif ch == "w":
            teleport(0, -STEP)
        elif ch == "s":
            teleport(0, STEP)
        elif ch == "a":
            teleport(-STEP, 0)
        elif ch == "d":
            teleport(STEP, 0)
finally:
    termios.tcsetattr(fd, termios.TCSADRAIN, old)
    print("\nDone.")
