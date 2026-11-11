// Inserted inside the existing PageManager QtObject; no global stress switch.
property Timer deltaTrBridgeTimer: Timer {
    interval: 350
    running: true
    repeat: true
    property var request: null
    property double requestStarted: 0
    property double lease: 0
    property string session: ""
    property double ack: 0
    property bool ownsText: false
    property bool allowReset: true
    property bool confirmedVisible: false
    property var dashboardSwitch: null
    @@DASHBOARD_SWITCH@@
    function release() {
        if (dashboardSwitch) dashboardSwitch.release();
        if (!ownsText) return;
        ownsText = false;
        if (allowReset && !TestSendTelemetryData.isSwitch) {
            try {
                var idle = {};
                for (var i = 0; i < 5; i++) idle["v1/gameData/DeltaTRRadio" + (i ? i + 1 : "")] = "idle|102|";
                TestSendTelemetryData.sendTelemetryData(idle);
            } catch (e) { console.warn("DELTATR_BRIDGE reset failed"); }
        }
    }
    onTriggered: {
        var time = Date.now();
        if (!dashboardSwitch) dashboardSwitch = createDeltaTrDashboardSwitch();
        if (time > lease) dashboardSwitch.release();
        if (ownsText && time > lease) release();
        if (request) {
            if (time - requestStarted > 1200) {
                var expired = request; request = null; expired.abort();
            }
            return;
        }
        var xhr = new XMLHttpRequest();
        request = xhr; requestStarted = time;
        xhr.onreadystatechange = function() {
            if (xhr.readyState !== XMLHttpRequest.DONE) return;
            if (deltaTrBridgeTimer.request !== xhr) return;
            deltaTrBridgeTimer.request = null;
            if (xhr.status !== 200) return;
            try {
                if (xhr.responseText.length > 4096) return;
                var packet = JSON.parse(xhr.responseText);
                if (packet.version !== 4 || packet.field !== "v1/gameData/DeltaTRRadio" || typeof packet.session !== "string" || packet.session.length !== 32 ||
                    typeof packet.seq !== "number" || packet.seq <= 0 ||
                    !Array.isArray(packet.rows) || packet.rows.length !== 5 ||
                    typeof packet.expires !== "number" || packet.expires < Date.now() || packet.expires > Date.now() + 3000) return;
                deltaTrBridgeTimer.allowReset = packet.allowReset === true;
                if (!packet.enabled) { deltaTrBridgeTimer.release(); return; }
                if (TestSendTelemetryData.isSwitch || typeof TestSendTelemetryData.sendTelemetryData !== "function") return;
                var values = {};
                for (var row = 0; row < 5; row++) {
                    var line = packet.rows[row];
                    if (typeof line !== "string" || line.length > 127 || !/^(idle|listen|think|reply|offline)\|\d{1,3}\|/.test(line)) return;
                    values["v1/gameData/DeltaTRRadio" + (row ? row + 1 : "")] = line;
                }
                if (["keep", "radio", "return"].indexOf(packet.screenMode) < 0 || typeof packet.screenRequest !== "number") return;
                if (packet.session !== deltaTrBridgeTimer.session) {
                    deltaTrBridgeTimer.session = packet.session; deltaTrBridgeTimer.ack = 0;
                }
                if (packet.seq < deltaTrBridgeTimer.ack) return;
                deltaTrBridgeTimer.lease = packet.expires;
                if (packet.seq !== deltaTrBridgeTimer.ack || !deltaTrBridgeTimer.ownsText) {
                    TestSendTelemetryData.sendTelemetryData(values);
                    deltaTrBridgeTimer.ack = packet.seq;
                    deltaTrBridgeTimer.ownsText = true;
                    console.info("DELTATR_BRIDGE accepted seq=" + packet.seq);
                }
                dashboardSwitch.update(packet.screenMode, packet.session + ":" + packet.screenRequest);
                var visible = dashboardSwitch.visible();
                if (visible && !deltaTrBridgeTimer.confirmedVisible) TestSendTelemetryData.sendTelemetryData(values);
                deltaTrBridgeTimer.confirmedVisible = visible;
            } catch (e) { console.warn("DELTATR_BRIDGE message rejected"); }
        };
        xhr.open("GET", "http://127.0.0.1:@@PORT@@/v1/screen?protocol=4&field=DeltaTRRadio&screen=" + dashboardSwitch.status() + "&display=" + (dashboardSwitch.visible() ? "1" : "0") + "&session=" + session + "&ack=" + ack);
        xhr.setRequestHeader("X-DeltaTR-Key", "@@KEY@@");
        xhr.send();
    }
}
