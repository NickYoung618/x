#!/usr/bin/env python3
"""Exercise only confirmed 2026-09-11 points against a separate simulator process."""

import argparse
import json
import os
from pathlib import Path
import socket
import struct
import subprocess
import time
import urllib.error
import urllib.request


ROOT = Path(__file__).resolve().parents[3]
SIM = ROOT / "simulator/VirtualPlc/src/VirtualPlc"
HOST = ROOT / "backend/src/Inspection.Host/bin/Release/net10.0/Inspection.Host.dll"
EVIDENCE = ROOT / "specs/010-v13-device-simulator/evidence"


def free_port():
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def read_json(url, method="GET"):
    with urllib.request.urlopen(urllib.request.Request(url, method=method), timeout=3) as reply:
        return json.load(reply)


class Modbus:
    def __init__(self, port):
        self.socket = socket.create_connection(("127.0.0.1", port), timeout=3)
        self.sequence = 0

    def close(self):
        self.socket.close()

    def exchange(self, pdu):
        self.sequence += 1
        request = struct.pack(">HHHB", self.sequence, 0, len(pdu) + 1, 1) + pdu
        self.socket.sendall(request)
        header = self.exact(7)
        transaction, protocol, length, unit = struct.unpack(">HHHB", header)
        assert (transaction, protocol, unit) == (self.sequence, 0, 1)
        response = self.exact(length - 1)
        return response

    def exact(self, count):
        data = b""
        while len(data) < count:
            part = self.socket.recv(count - len(data))
            if not part:
                raise EOFError("Modbus peer disconnected")
            data += part
        return data

    def coil(self, point, value):
        pdu = struct.pack(">BHH", 5, point - 1, 0xFF00 if value else 0)
        return self.exchange(pdu)

    def register(self, point, value):
        pdu = struct.pack(">BHH", 6, point - 1, value)
        return self.exchange(pdu)

    def registers(self, point, count):
        reply = self.exchange(struct.pack(">BHH", 3, point - 1, count))
        assert reply[:2] == bytes((3, count * 2))
        return struct.unpack(">" + "H" * count, reply[2:])

    def write_float(self, point, value):
        pdu = struct.pack(">BHHBf", 16, point - 1, 2, 4, value)
        return self.exchange(pdu)


def wait_until(predicate, seconds=3):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            if predicate():
                return
        except (OSError, urllib.error.URLError):
            pass
        time.sleep(0.02)
    raise TimeoutError("Device fact did not reach the expected state")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", required=True)
    args = parser.parse_args()
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    plc_port, web_port = free_port(), free_port()
    while plc_port == web_port:
        web_port = free_port()
    base = f"http://127.0.0.1:{web_port}"
    log = (EVIDENCE / "base-simulator.log").open("w", encoding="utf-8")
    env = os.environ.copy()
    env.update({"Simulation__ProtocolProfile": "Protocol20260911Synthetic",
                "Modbus__ListenAddress": "127.0.0.1", "Modbus__Port": str(plc_port),
                "Dashboard__OpenBrowserOnStart": "false"})
    process = subprocess.Popen([args.dotnet, str(SIM / "bin/Release/net10.0/VirtualPlc.dll"),
                                "--urls", base], cwd=SIM, env=env, stdout=log,
                               stderr=subprocess.STDOUT)
    client = None
    try:
        wait_until(lambda: read_json(base + "/health")["protocolProfile"] == "Protocol20260911Synthetic")
        host_env = env.copy()
        host_env.update({"Plc__Provider": "Virtual", "Plc__Contract": "plc-upper-20260911-hex1-f32",
                         "Plc__AddressConvention": "HexOneBased", "Plc__Float32ByteOrder": "Abcd",
                         "Plc__CoordinateFrame": "synthetic-v13", "Plc__Unit": "synthetic-unit",
                         "Plc__Host": "127.0.0.1", "Plc__Port": str(plc_port),
                         "Plc__Report": str(EVIDENCE / "base-host-read-only.json")})
        subprocess.run([args.dotnet, str(HOST), "--plc-device-check"], cwd=ROOT,
                       env=host_env, check=True, capture_output=True, text=True, timeout=15)
        host = json.loads((EVIDENCE / "base-host-read-only.json").read_text())
        assert host["outcome"] == "ReadOnly" and len(host["exchanges"]) == 4
        client = Modbus(plc_port)
        assert client.coil(3, True) == struct.pack(">BHH", 5, 2, 0xFF00)
        wait_until(lambda: read_json(base + "/api/v13/state")["plcReady"])
        assert client.register(0x23, 1) == struct.pack(">BHH", 6, 0x22, 1)
        wait_until(lambda: client.registers(0x24, 1)[0] == 1)
        assert client.write_float(0x0003, -12.5) == struct.pack(">BHH", 16, 2, 2)
        assert client.registers(0x0003, 2) == struct.unpack(">HH", struct.pack(">f", -12.5))
        assert client.register(0x0001, 2) == bytes((0x86, 0x02)), "Undocumented 3D/F command must fail"
        read_json(base + "/api/v13/faults/ManualZoneOccupied?active=true", "POST")
        assert read_json(base + "/api/v13/state")["manualZoneOccupied"]
        assert not read_json(base + "/api/v13/state")["plcReady"]
        assert client.register(0x23, 1) == struct.pack(">BHH", 6, 0x22, 1)
        wait_until(lambda: client.registers(0x24, 1)[0] == 2)
        state = read_json(base + "/api/v13/state")
        trace = read_json(base + "/api/v13/trace")
        assert any(entry["kind"] == "Modbus" and entry["request"] for entry in trace)
        assert any(entry["kind"] == "Clamp" for entry in trace)
        (EVIDENCE / "base-simulator-state.json").write_text(json.dumps(state, indent=2) + "\n")
        (EVIDENCE / "base-simulator-trace.json").write_text(json.dumps(trace, indent=2) + "\n")
        print("PASS: separate Host read-only, Float32 words, clamp, unsafe fault, unknown command rejection")
    finally:
        if client:
            client.close()
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
        log.close()


if __name__ == "__main__":
    main()
