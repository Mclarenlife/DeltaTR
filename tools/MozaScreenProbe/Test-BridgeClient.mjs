// Exercise the actual QML handler with a simulated clock/network; no hardware calls.
import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const source = fs.readFileSync(new URL('../../src/DeltaTR.Hardware.Moza/PitHouseBridge.qml', import.meta.url), 'utf8');
const body = source.slice(source.indexOf('onTriggered: {') + 'onTriggered: {'.length, source.lastIndexOf('\n    }\n}'));
const release = source.slice(source.indexOf('    function release()'), source.indexOf('    onTriggered:'));
let clock = 100000, pending = [], writes = [];
class Request {
    static DONE = 4;
    open(method, url) { assert.equal(method, 'GET'); assert.ok(url.startsWith('http://127.0.0.1:')); }
    setRequestHeader(name) { assert.equal(name, 'X-DeltaTR-Key'); }
    send() { pending.push(this); }
    abort() { this.aborted = true; }
    answer(packet) { this.readyState = 4; this.status = 200; this.responseText = JSON.stringify(packet); this.onreadystatechange(); }
}
const timer = { request: null, requestStarted: 0, lease: 0, session: '', ack: 0, ownsText: false, allowReset: true, dashboardSwitch: null };
let show = false;
const context = vm.createContext({ createDeltaTrDashboardSwitch: () => ({update(mode){
    if (mode === 'radio') { assert.equal(writes.at(-1), 'reply|102|新提醒'); show = true; }
}, release(){show=false;}, status(){return 'disabled';}, visible(){return show;}}), timer, deltaTrBridgeTimer: timer, XMLHttpRequest: Request,
    Date: { now: () => clock }, console: { info() {}, warn() {} },
    TestSendTelemetryData: { isSwitch: false, sendTelemetryData(data) {
        assert.deepEqual(Object.keys(data), Array.from({length:5}, (_,i) => 'v1/gameData/DeltaTRRadio' + (i ? i + 1 : ''))); writes.push(data['v1/gameData/DeltaTRRadio']);
    } } });
vm.runInContext(`with (timer) { ${release}; timer.release = release; } function fire() { with(timer) { ${body} } }`, context);
function reply(overrides = {}) {
    context.fire();
    const request = pending.at(-1);
    const text = overrides.text || 'reply|左前胎温八十六度';
    const rows = Array.from({length:5}, (_,i) => i ? 'reply|102|' : text.replace('|','|102|'));
    request.answer({ version: 4, screenMode: 'keep', screenRequest: 1, field: 'v1/gameData/DeltaTRRadio', session: 'a'.repeat(32), seq: 1, enabled: true, allowReset: true,
        rows, expires: clock + 2000, ...overrides });
}
reply({version:1}); assert.equal(writes.length,0,'reject legacy server');
reply({field:'v1/gameData/PlayerName'}); assert.equal(writes.length,0,'reject shared field');
reply(); assert.equal(writes.at(-1), 'reply|102|左前胎温八十六度');
reply(); assert.equal(writes.length, 1, 'heartbeat must not write repeatedly');
reply({ seq: 2, text: 'listen|正在聆听' }); assert.equal(writes.at(-1), 'listen|102|正在聆听');
reply({ seq: 1, text: 'reply|过期顺序' }); assert.equal(writes.length, 2, 'out-of-order message rejected');
reply({ seq: 3, expires: clock - 1 }); assert.equal(writes.length, 2, 'expired lease rejected');
reply({ seq: 3, text: 'eval|anything' }); assert.equal(writes.length, 2, 'unknown state rejected');
reply({ seq: 3, enabled: false, allowReset: false }); assert.equal(writes.length, 2, 'explicit reset denial must not send cleanup');
assert.equal(timer.ownsText, false);
reply({ seq: 4, text: 'reply|恢复' }); assert.equal(writes.at(-1), 'reply|102|恢复');
reply({ seq: 5, enabled: false }); assert.equal(writes.at(-1), 'idle|102|', 'normal stop releases once');
const released = writes.length;
reply({ seq: 5, enabled: false }); assert.equal(writes.length, released, 'no repeated stop writes');
reply({ session: 'b'.repeat(32), seq: 1, text: 'reply|新会话' }); assert.equal(writes.at(-1), 'reply|102|新会话');
clock += 2100; context.fire(); assert.equal(writes.at(-1), 'idle|102|', 'lost host expires and releases text');
const afterCrash = writes.length;
clock += 1300; context.fire(); assert.equal(pending.at(-1).aborted, true, 'unanswered request is aborted');
context.fire(); assert.equal(writes.length, afterCrash, 'never spam idle while host absent');
console.log('PASS QML bridge: Chinese, state transition, deduplication, sequence, lease, reset permission, graceful stop, restart and host loss');
reply({session:'c'.repeat(32), seq:1, text:'reply|新提醒', screenMode:'radio'});
assert.equal(writes.at(-1), 'reply|102|新提醒');
const afterShow = writes.length;
reply({session:'c'.repeat(32), seq:1, text:'reply|新提醒', screenMode:'radio'});
assert.equal(writes.length, afterShow, 'stable visible page is not rewritten');
