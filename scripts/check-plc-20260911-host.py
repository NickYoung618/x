#!/usr/bin/env python3
"""Run the real Host Modbus client against an isolated 2026-09-11 read-only server.

This is a wire smoke test, not a full virtual PLC or an equipment safety test.
"""
import argparse
import json
import os
from pathlib import Path
import socketserver
import struct
import subprocess
import tempfile
import threading

CONTRACT = 'plc-upper-20260911-hex1-f32'


def exactly(sock, size):
    data = bytearray()
    while len(data) < size:
        chunk = sock.recv(size - len(data))
        if not chunk:
            raise EOFError()
        data.extend(chunk)
    return bytes(data)


class PlcHandler(socketserver.BaseRequestHandler):
    def handle(self):
        while True:
            try:
                header = exactly(self.request, 7)
                length = struct.unpack('>H', header[4:6])[0]
                pdu = exactly(self.request, length - 1)
            except EOFError:
                return
            function = pdu[0]
            offset, count = struct.unpack('>HH', pdu[1:5])
            self.server.requests.append((function, offset, count))
            if function == 1:
                assert (offset, count) == (0, 16)
                coils = self.server.coils
                payload = bytearray((count + 7) // 8)
                for i in range(count):
                    if coils[offset + i]:
                        payload[i // 8] |= 1 << (i % 8)
                reply = bytes([1, len(payload)]) + payload
            elif function == 3:
                assert (offset, count) in {(0, 23), (31, 9), (79, 2)}
                payload = b''.join(struct.pack('>H', word) for word in self.server.registers[offset:offset + count])
                reply = bytes([3, len(payload)]) + payload
            else:
                self.server.writes.append((function, offset, count))
                raise AssertionError(f'unexpected write function {function}')
            mbap = header[:4] + struct.pack('>H', len(reply) + 1) + header[6:7]
            self.request.sendall(mbap + reply)


class PlcServer(socketserver.TCPServer):
    allow_reuse_address = True
    def __init__(self):
        super().__init__(('127.0.0.1', 0), PlcHandler)
        self.requests = []
        self.writes = []
        self.coils = [False] * 64
        self.registers = [0] * 128
        self.coils[0] = True
        self.coils[4] = True
        self.coils[7] = True
        self.registers[1] = 1
        self.registers[18] = 2
        self.registers[21] = 2
        self.registers[22] = 3
        self.registers[0x21] = 2
        self.registers[0x23] = 1
        self.registers[0x27] = 1
        self.registers[0x4f] = 1 << 5
        self.registers[0x50] = 2
        for offset, value in [(12, 123.5), (14, -2.25), (16, 7.75)]:
            self.registers[offset:offset + 2] = struct.unpack('>HH', struct.pack('>f', value))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--report', help='Where to save the Host JSON report')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    dll = root / 'backend/src/Inspection.Host/bin/Release/net10.0/Inspection.Host.dll'
    if not dll.is_file():
        raise SystemExit('Build Release Host first; DLL not found')
    report = Path(args.report) if args.report else Path(tempfile.mkdtemp(prefix='gaode-plc-20260911-')) / 'host.json'
    report.parent.mkdir(parents=True, exist_ok=True)
    with PlcServer() as server:
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        env = dict(os.environ)
        env.update({
            'Plc__Provider': 'Virtual',
            'Plc__Contract': CONTRACT,
            'Plc__AddressConvention': 'HexOneBased',
            'Plc__Float32ByteOrder': 'Abcd',
            'Plc__CoordinateFrame': 'synthetic-machine',
            'Plc__Unit': 'synthetic-mm',
            'Plc__Host': '127.0.0.1',
            'Plc__Port': str(server.server_address[1]),
            'Plc__Report': str(report),
        })
        completed = subprocess.run([args.dotnet, str(dll), '--plc-device-check'],
                                   cwd=root, env=env, text=True, capture_output=True, timeout=15)
        rejected_env = dict(env)
        rejected_env['Plc__EngineeringActionEnabled'] = 'true'
        rejected = subprocess.run([args.dotnet, str(dll), '--plc-device-check'],
                                  cwd=root, env=rejected_env, text=True, capture_output=True, timeout=15)
        server.shutdown()
        thread.join(timeout=3)
    if completed.returncode != 0:
        raise SystemExit(f'Host failed ({completed.returncode}): {completed.stdout}\n{completed.stderr}')
    result = json.loads(report.read_text())
    snapshot = result['snapshot']
    expected_requests = [(1, 0, 16), (3, 0, 23), (3, 31, 9), (3, 79, 2)]
    assert server.requests == expected_requests, server.requests
    assert not server.writes, server.writes
    assert rejected.returncode != 0 and 'read-only' in rejected.stderr, rejected.stderr
    assert result['contract'] == CONTRACT and result['outcome'] == 'ReadOnly', result
    assert (snapshot['actualX'], snapshot['actualY'], snapshot['actualZ']) == (123.5, -2.25, 7.75), snapshot
    assert snapshot['currentFace'] == 3 and snapshot['alarmBits'] == 32 and snapshot['alarmSeverity'] == 2, snapshot
    assert len(result['exchanges']) == 4, result['exchanges']
    print(f'PASS: Host read-only 2026-09-11 Modbus check; 4 reads, 0 writes; action flag rejected; report={report}')


if __name__ == '__main__':
    main()
