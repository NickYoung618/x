from pathlib import Path
import json, os, socket, subprocess, time, urllib.request

base = Path(__file__).resolve().parent
root = base / 'snapshot'
result = root / 'review-results'
dotnet = '/home/ubuntu/.local/share/gaode-dotnet-10.0.401/dotnet'
reservations = [socket.socket(), socket.socket()]
for sock in reservations:
    sock.bind(('127.0.0.1', 0))
http_port, modbus_port = [sock.getsockname()[1] for sock in reservations]
for sock in reservations:
    sock.close()
url = f'http://127.0.0.1:{http_port}'
env = dict(os.environ, Dashboard__OpenBrowserOnStart='false',
           Modbus__ListenAddress='127.0.0.1', Modbus__Port=str(modbus_port))
summary = {'host_target': 'net10.0', 'sdk': '10.0.401',
           'test_driver_original_target': 'net8.0',
           'test_driver_review_target': 'net10.0', 'test_driver_language': 'C# 12',
           'source_modified': False, 'http_port': http_port, 'modbus_port': modbus_port}

with (result / 'host-net10.log').open('w') as log:
    host = subprocess.Popen([dotnet, str(base / 'published-host/VirtualPlc.dll'),
                             '--urls', url], cwd=base / 'published-host', env=env,
                            stdout=log, stderr=subprocess.STDOUT)
    try:
        for _ in range(100):
            if host.poll() is not None:
                raise RuntimeError('Host exited before readiness')
            try:
                with urllib.request.urlopen(url + '/health', timeout=0.5) as response:
                    summary['health'] = json.load(response)
                break
            except OSError:
                time.sleep(0.1)
        else:
            raise TimeoutError('Host was not ready in 10 seconds')

        suite = subprocess.run([
            dotnet, str(base / 'review-driver/bin/Release/net10.0/VirtualPlc.SystemValidation.dll'),
            '--http', url, '--modbus-host', '127.0.0.1', '--modbus-port', str(modbus_port),
            '--repo-root', str(root), '--json-output', str(result / 'suite-net10.json'),
            '--markdown-output', str(result / 'suite-net10.md'),
            '--sdk-version', '10.0.401', '--execution-target', 'net10.0',
            '--mode', 'published-net10-host-with-linked-net10-review-driver',
            '--production-gate-status', 'PASS',
            '--production-gate-reason', 'Unmodified net10.0 host published successfully; original test source linked unchanged into a separate net10.0 review driver with C# 12 matching the original net8.0 language default. Original validation entry is not claimed as passed.'
        ], capture_output=True, text=True, timeout=90)
        (result / 'suite-stdout.log').write_text(suite.stdout + suite.stderr)
        summary['suite_exit'] = suite.returncode
        print(suite.stdout, flush=True)

        probes = {}
        for category in ['Barcode', 'Recipe', 'Storage', 'DeviceTimeout', 'Algorithm', 'DeviceAction']:
            with urllib.request.urlopen(url + f'/api/simulator/flow-decision?category={category}&stepSucceeded=false', timeout=2) as response:
                probes[category] = json.load(response)
        (result / 'flow-policy-probes.json').write_text(json.dumps(probes, ensure_ascii=False, indent=2))
        summary['v13_policy_conflicts_confirmed'] = [
            key for key in ['Barcode', 'Recipe', 'Storage', 'DeviceTimeout']
            if probes[key]['continueFlow']
        ]
        print(json.dumps(summary, ensure_ascii=False, indent=2), flush=True)
    finally:
        host.terminate()
        try:
            host.wait(timeout=5)
        except subprocess.TimeoutExpired:
            host.kill()
            host.wait(timeout=5)
        summary['own_host_stopped'] = host.poll() is not None
        (result / 'review-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2))
