"""Test raw UDP -> isolated TouchService -> real Unity WebSocket client.

Copies current service source; never writes to its checkout or binds live ports.
Run with the bundled Python runtime or Python 3.10+ on Windows.
"""
from pathlib import Path
import hashlib
import json
import shutil
import socket
import struct
import subprocess
import time
import urllib.request

PROJECT = Path(__file__).resolve().parents[1]
SERVICE = PROJECT.parent / "NangangProjection/NangangProjection-git/TouchService"
UNITY = Path("C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe")
SANDBOX = PROJECT / "TestResults/TouchServiceSmoke"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    config_before = digest(SERVICE / "config.json")
    for port, kind in [(29001, socket.SOCK_DGRAM), (29111, socket.SOCK_STREAM)]:
        with socket.socket(socket.AF_INET, kind) as probe:
            probe.bind(("127.0.0.1", port))
    SANDBOX.mkdir(parents=True, exist_ok=True)
    # A fresh numbered source snapshot avoids overwriting any previous run.
    snapshot = SANDBOX / ("source-" + time.strftime("%H%M%S"))
    shutil.copytree(SERVICE / "src/TouchService", snapshot, ignore=shutil.ignore_patterns("bin", "obj"))
    shutil.copyfile(SERVICE / "venues/704.json", SANDBOX / "704.json")
    config = json.loads((SERVICE / "config.json").read_text(encoding="utf-8-sig"))
    config.update(listenPort=29001, venueProfile="704.json", lockedCells=[], touchMode="grid")
    config["autoLock"]["enabled"] = False
    config["ui"].update(autoLaunch=False, trayEnabled=False)
    config["udpOutput"]["enabled"] = False
    config["webSocketOutput"].update(enabled=True, port=29111)
    config["log"].update(dir="service-logs", console=True)
    config_path = SANDBOX / "config.json"
    config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")
    ready = SANDBOX / "ready.json"
    if ready.exists():
        ready.unlink()
    with (SANDBOX / "service-build.log").open("w", encoding="utf-8") as log:
        subprocess.run(["dotnet", "build", str(snapshot / "TouchService.csproj"), "-c", "Release", "-o", str(SANDBOX / "bin")], stdout=log, stderr=subprocess.STDOUT, check=True, creationflags=subprocess.CREATE_NO_WINDOW)
    log = (SANDBOX / "service-console.log").open("w", encoding="utf-8")
    service = subprocess.Popen(["dotnet", str(SANDBOX / "bin/TouchService.dll"), "--config", str(config_path)], stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
    editor = None
    try:
        for _ in range(100):
            if service.poll() is not None:
                raise RuntimeError("Isolated service exited before startup")
            try:
                with urllib.request.urlopen("http://127.0.0.1:29111/venue", timeout=.3) as response:
                    assert json.load(response)["canvas"]["width"] == 2688
                break
            except OSError:
                time.sleep(.1)
        else:
            raise TimeoutError("Isolated service did not become ready")
        editor = subprocess.Popen([str(UNITY), "-batchmode", "-nographics", "-quit", "-projectPath", str(PROJECT), "-executeMethod", "ScanPro704.Editor.TouchServiceVerification.Run", "-logFile", str(PROJECT / "Logs/touchservice-integration.log")], creationflags=subprocess.CREATE_NO_WINDOW)
        deadline = time.monotonic() + 45
        while not ready.exists() and time.monotonic() < deadline:
            if editor.poll() is not None:
                raise RuntimeError("Unity exited before test handshake")
            time.sleep(.1)
        target = json.loads(ready.read_text(encoding="utf-8-sig"))
        face = json.loads((SANDBOX / "704.json").read_text())["faces"][target["face"]]
        # Vertical service cells are 32px wide and 16px high.
        raw_x = round(face["rawMinX"] + (target["x"] - face["originX"]) / (face["colCount"] * 32) * (face["rawMaxX"] - face["rawMinX"]))
        raw_y = round(face["rawMinY"] + (target["y"] - face["originY"]) / (face["rowCount"] * 16) * (face["rawMaxY"] - face["rawMinY"]))
        # Wait until the real WebSocket consumer has completed its handshake.
        time.sleep(.7)
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sender:
            payload = struct.pack(">HH", raw_x, raw_y)
            for frame in range(10):
                packet = b"\x55\x55" + bytes([frame]) + struct.pack(">H", len(payload)) + payload + struct.pack(">H", sum(payload) & 0xffff)
                sender.sendto(packet, ("127.0.0.1", 29001))
                time.sleep(.08)
        assert editor.wait(timeout=35) == 0, "Unity integration check failed"
        assert digest(SERVICE / "config.json") == config_before, "Original service config changed"
        (SANDBOX / "isolation-checks.txt").write_text("PASS Original TouchService config SHA-256 unchanged\nPASS Isolated UDP 29001 / WebSocket 29111\nPASS UI, tray, autoLock, UDP publisher disabled\n", encoding="utf-8")
        print((SANDBOX / "touchservice-checks.txt").read_text())
    finally:
        if editor is not None and editor.poll() is None:
            editor.terminate()
            editor.wait(timeout=10)
        try:
            request = urllib.request.Request("http://127.0.0.1:29111/command", data=b'{"cmd":"quit"}', headers={"Content-Type": "application/json"}, method="POST")
            urllib.request.urlopen(request, timeout=2).close()
            service.wait(timeout=5)
        except (OSError, subprocess.TimeoutExpired):
            if service.poll() is None:
                service.terminate()
                service.wait(timeout=10)
        log.close()


if __name__ == "__main__":
    main()
