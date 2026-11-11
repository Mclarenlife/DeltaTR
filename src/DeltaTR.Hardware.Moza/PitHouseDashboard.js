// Embedded into the version-pinned QML timer. Only the existing applyDashboard API is called.
function createDeltaTrDashboardSwitch() {
    var radioId = "";
    var devices = null, model = null, manager = null, device = "", previous = "", owns = false;
    var pending = "", deadline = 0, suppressed = "", status = "disabled", visible = false, lastError = "";
    function context() {
        if (!devices) devices = Qt.createQmlObject('import QtQuick 2.15; import RacingSimulator 1.0; QtObject { property ValidBaseControllerProxyListModel bases: ValidBaseControllerProxyListModel { Component.onCompleted: onUpdate() } property ValidHubControllerProxyListModel hubs: ValidHubControllerProxyListModel { Component.onCompleted: onUpdate() } }', deltaTrBridgeTimer);
        var proxies = [], found = [], seen = {};
        var groups = [devices.bases.controllerProxies, devices.hubs.controllerProxies];
        for (var g = 0; g < groups.length; g++) {
            var group = groups[g] || [];
            for (var k = 0; k < group.length; k++) proxies.push(group[k]);
        }
        for (var key = 0; key < proxies.length; key++) {
            var programs = proxies[key].programs || {};
            for (var slot = 16; slot <= 18; slot++) {
                var raw = programs[slot];
                if (!raw) continue;
                var p = ScreenProgramManager.program(raw);
                if (p && p.connectedState && p.controllerInfo && p.controllerInfo.productType === "W18 Display") {
                    var uid = MOZARacing.QByteArrayToHex(p.controllerInfo.mcuUid);
                    if (!seen[uid]) { seen[uid] = true; found.push(p); }
                }
            }
        }
        if (found.length !== 1) { status = "device"; return null; }
        var info = found[0].controllerInfo;
        var next = ResourceManagerProxy.resourceManagerMap[info.mcuUid];
        if (!next || typeof next.applyDashboard !== "function" || next.resetBusy) { status = "busy"; return null; }
        if (manager !== next || device !== String(info.mcuUid)) {
            if (model) model.destroy();
            // Pit House regenerates GUIDs on import; resolve the uniquely named uploaded dashboard.
            model = Qt.createQmlObject('import QtQuick 2.15; import RacingSimulator 1.0; Item { visible: false; property alias manager: rows.manager; property alias ids: rows.ids; DashboardListModel { id: rows } Repeater { id: entries; model: rows; delegate: Item { property var info: model.localInfo } } function radioIds() { var found = []; for (var i = 0; i < entries.count; i++) { var row = entries.itemAt(i); if (row && row.info && row.info.title === "DeltaTR KS Pro Fullscreen Radio") found.push(String(row.info.id)); } return found; } }', deltaTrBridgeTimer);
            model.manager = next; manager = next; device = String(info.mcuUid);
            previous = ""; owns = false; pending = ""; suppressed = ""; radioId = "";
        }
        var ids = [], source = model.ids || [];
        for (var i = 0; i < source.length; i++) ids.push(String(source[i]));
        var matches = model.radioIds();
        if (!owns && !pending) radioId = matches.length === 1 ? matches[0] : "";
        var index = Number(manager.index), radio = ids.indexOf(radioId);
        if (radio < 0 || index < 0 || index >= ids.length || index % 1 !== 0) { status = "dashboard"; return null; }
        visible = ids[index] === radioId;
        return { ids: ids, current: ids[index], radio: radio };
    }
    function apply(ctx, id) {
        var index = ctx.ids.indexOf(id);
        if (index < 0) { status = "missing-return"; return false; }
        manager.applyDashboard(index);
        pending = id; deadline = Date.now() + 2500;
        status = "switching";
        return true;
    }
    function update(mode, request) {
        try {
            if (mode === "keep" && !owns && !pending) { status = "disabled"; return; }
            var ctx = context();
            if (!ctx) return;
            if (pending) {
                if (ctx.current === pending) {
                    if (pending !== radioId) { owns = false; previous = ""; }
                    pending = "";
                } else if (Date.now() < deadline) return;
                else { pending = ""; owns = false; suppressed = request; status = "timeout"; return; }
            }
            // Respect an external Pit House / wheel-button change, instead of fighting it every poll.
            if (owns && ctx.current !== radioId) {
                owns = false; previous = ""; suppressed = request; status = "external";
            }
            if (mode === "radio") {
                if (suppressed === request) return;
                if (ctx.current === radioId) { status = owns ? "radio" : "no-return"; return; }
                previous = ctx.current;
                if (apply(ctx, radioId)) owns = true;
            } else {
                if (owns && ctx.current === radioId) {
                    if (!apply(ctx, previous)) { owns = false; previous = ""; }
                } else { status = ctx.current === radioId ? "no-return" : "race"; }
            }
        } catch (e) {
            status = "unavailable";
            var detail = String(e);
            if (detail !== lastError) { console.warn("DELTATR_DASHBOARD " + detail); lastError = detail; }
        }
    }
    function release() { if (owns || pending) update("return", "release"); }
    return { update: update, release: release, status: function() { return status; }, visible: function() { return visible; } };
}
