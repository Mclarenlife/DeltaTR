"""Reversible QML research probe. Default is names-only, without setters.

prepare: saves the original and candidate in ignored research artifacts.
install/restore: hash guarded; require permission to edit the Pit House install.
Restart Pit House normally after install, then restore the file and restart again.
The explicit text probe writes only the existing PlayerName channel: 75 test
messages followed by idle, nominally every 2 s after an initial startup delay.
Run it only without a driving simulator; it is not a production bridge.
"""
import argparse, hashlib, json
from pathlib import Path

TARGET = Path('D:/software/Moza Pit House/bin/CustomQmlPlugin/PageManager.qml')
OUT = Path('artifacts/moza-coexist/runtime-probe')
def sha(data): return hashlib.sha256(data).hexdigest()

PROBE = r'''
    // DeltaTR temporary names-only inspection. One shot, no writes or network.
    property Timer deltaTrReadOnlyProbe: Timer {
        interval: 15000
        running: true
        repeat: false
        onTriggered: {
            function inspect(label, getter) {
                try {
                    var obj = getter();
                    if (obj === undefined || obj === null) {
                        console.info("DELTATR_PROBE " + label + " unavailable");
                        return;
                    }
                    var names = [];
                    for (var name in obj) names.push(name);
                    names.sort();
                    console.info("DELTATR_PROBE " + label + " names=" + JSON.stringify(names));
                } catch (e) {
                    console.info("DELTATR_PROBE " + label + " error=" + String(e));
                }
            }
            console.info("DELTATR_PROBE begin names-only");
            inspect("TestSendTelemetryData", function() { return TestSendTelemetryData; });
            try {
                console.info("DELTATR_PROBE TestSendTelemetryData knownTypes=" + JSON.stringify({
                    sendTelemetryData: typeof TestSendTelemetryData.sendTelemetryData,
                    isSwitch: typeof TestSendTelemetryData.isSwitch,
                    sendInterval: typeof TestSendTelemetryData.sendInterval
                }));
            } catch (e) { console.info("DELTATR_PROBE knownTypes error=" + String(e)); }
            inspect("TelemetryDataPrinting", function() { return TelemetryDataPrinting; });
            inspect("TelemetryDataOutputBuffer", function() { return TelemetryDataOutputBuffer; });
            inspect("TelemetryDataInputBuffer", function() { return TelemetryDataInputBuffer; });
            inspect("mozaDeviceManager", function() { return mozaDeviceManager; });
            inspect("TelemetryServer", function() { return TelemetryServer; });
            console.info("DELTATR_PROBE complete");
        }
    }
'''

TEXT_PROBE = r'''
    // DeltaTR bounded bench test: one text field, no stress switch or device settings.
    property Timer deltaTrTextProbe: Timer {
        interval: 2000
        running: true
        repeat: true
        property int tick: 0
        onTriggered: {
            tick++;
            if (tick < 10) return;
            if (typeof TestSendTelemetryData.sendTelemetryData !== "function" || TestSendTelemetryData.isSwitch) {
                console.info("DELTATR_BRIDGE refused: method unavailable or stress test enabled");
                stop(); return;
            }
            try {
                var values = {};
                values["v1/gameData/PlayerName"] = tick < 85
                    ? "reply|内部桥接中文验证 #" + (tick - 9) : "idle|";
                TestSendTelemetryData.sendTelemetryData(values);
                console.info("DELTATR_BRIDGE submitted text-only seq=" + (tick - 9));
                if (tick >= 85) { stop(); console.info("DELTATR_BRIDGE stopped"); }
            } catch (e) {
                stop(); console.info("DELTATR_BRIDGE error=" + String(e));
            }
        }
    }
'''

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('action', choices=['prepare', 'install', 'restore', 'status'])
parser.add_argument('--run-id', default='runtime-probe')
parser.add_argument('--probe-kind', choices=['names', 'text'], default='names')
parser.add_argument('--text-count', type=int, default=75, choices=range(1,76), metavar='1..75')
args = parser.parse_args()
if not args.run_id.replace('-', '').isalnum(): raise SystemExit('Invalid run id.')
OUT = OUT.parent / args.run_id
manifest_file = OUT / 'manifest.json'
if args.action == 'prepare':
    if manifest_file.exists():
        raise SystemExit('Existing probe backup retained; use status/install/restore.')
    original = TARGET.read_bytes()
    source = original.decode('utf-8-sig')
    if 'DELTATR_PROBE' in source or 'DELTATR_BRIDGE' in source:
        raise SystemExit('Probe already present; refusing to overwrite backup.')
    pos = source.rfind('}')
    if pos < 0: raise SystemExit('Missing root closing brace.')
    probe = TEXT_PROBE.replace('tick < 85', 'tick < ' + str(10 + args.text_count)).replace('tick >= 85', 'tick >= ' + str(10 + args.text_count)) if args.probe_kind == 'text' else PROBE
    candidate = (source[:pos] + probe + source[pos:]).encode('utf-8')
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'PageManager.original.qml').write_bytes(original)
    (OUT / 'PageManager.probe.qml').write_bytes(candidate)
    manifest = dict(target=str(TARGET), kind=args.probe_kind, textCount=args.text_count if args.probe_kind == 'text' else 0, originalSha256=sha(original), probeSha256=sha(candidate))
    manifest_file.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(json.dumps(manifest))
else:
    manifest = json.loads(manifest_file.read_text(encoding='utf-8'))
    if manifest['target'] != str(TARGET): raise SystemExit('Target mismatch.')
    current = sha(TARGET.read_bytes())
    if args.action == 'status':
        print(json.dumps(dict(currentSha256=current, original=current == manifest['originalSha256'], probe=current == manifest['probeSha256'])))
    else:
        install = args.action == 'install'
        expected = manifest['originalSha256' if install else 'probeSha256']
        desired = manifest['probeSha256' if install else 'originalSha256']
        if current == desired:
            print('Already ' + ('installed' if install else 'restored'))
        else:
            if current != expected: raise SystemExit('File changed independently; refusing to overwrite.')
            data = (OUT / ('PageManager.probe.qml' if install else 'PageManager.original.qml')).read_bytes()
            if sha(data) != desired: raise SystemExit('Backup/candidate integrity mismatch.')
            TARGET.write_bytes(data)
            if sha(TARGET.read_bytes()) != desired: raise SystemExit('Write verification failed.')
            print('Installed ' + manifest.get('kind', 'names') + ' probe' if install else 'Restored original byte for byte')
