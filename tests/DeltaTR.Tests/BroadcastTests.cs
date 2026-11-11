using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.ACC.Broadcast;

internal static class BroadcastTests
{
    internal static async Task Loopback()
    {
        static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        var now = DateTimeOffset.UtcNow;
        var shared = RaceSnapshot.Empty(now, 0, SessionPhase.Live, "fixture") with { Track = "spa", SessionId = "fixture" };
        foreach (var (key, value) in new[] { ("Graphics.playerCarID", 7), ("Graphics.sessionIndex", 0) })
            shared.Fields[key] = new(value, "", now, DataQuality.Valid, "fixture", "1.9");
        using var client = new BroadcastClient(() => (port, "test"));
        client.Poll(1, shared, now);
        async Task<UdpReceiveResult> Receive() => await server.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var registration = await Receive();
        Check(registration.Buffer.SequenceEqual(new byte[] {1,4,7,0,68,101,108,116,97,84,82,4,0,116,101,115,116,250,0,0,0,0,0}), "registration v4 with empty command password");
        void Send(byte type, Action<BinaryWriter> write)
        {
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write(type); write(w); var bytes = ms.ToArray();
            server.Send(bytes, bytes.Length, registration.RemoteEndPoint);
        }
        void Text(BinaryWriter w, string s) { var bytes = Encoding.UTF8.GetBytes(s); w.Write((ushort)bytes.Length); w.Write(bytes); }
        void Lap(BinaryWriter w, ushort car)
        {
            w.Write(90000); w.Write(car); w.Write((ushort)0); w.Write((byte)3);
            w.Write(29000); w.Write(31000); w.Write(30000); w.Write(new byte[] {0,1,0,0});
        }
        void Session(ushort index)
        {
            Send(2, w => {
                w.Write((ushort)1); w.Write(index); w.Write((byte)10); w.Write((byte)5); w.Write(30000f); w.Write(900000f);
                w.Write(8); Text(w,"tv"); Text(w,"camera"); Text(w,"hud"); w.Write((byte)0);
                w.Write(123000f); w.Write(new byte[] {20,25,1,2,3}); Lap(w, 8);
            });
        }
        void Car(ushort id, ushort place, string name)
        {
            Send(6, w => {
                w.Write(id); w.Write((byte)10); Text(w,"Test team"); w.Write((int)id); w.Write(new byte[]{0,0});
                w.Write((ushort)0); w.Write((byte)1); Text(w,name); Text(w,"Driver"); Text(w,"TST"); w.Write((byte)1); w.Write((ushort)0);
            });
            Send(3, w => {
                w.Write(id); w.Write((ushort)0); w.Write((byte)1); w.Write((byte)5); w.Write(55f); w.Write(-1f); w.Write(.3f);
                w.Write((byte)1); w.Write((ushort)155); w.Write(place); w.Write(place); w.Write(place);
                w.Write(.5f); w.Write((ushort)5); w.Write(-155); Lap(w,id); Lap(w,id); Lap(w,id);
            });
        }
        Send(1, w => { w.Write(123); w.Write(new byte[] {1,0}); Text(w,""); });
        for (var i=0; i<20 && server.Available==0; i++) { client.Poll(1, shared, now); await Task.Delay(10); }
        var listRequest = await Receive(); var trackRequest = await Receive();
        Check(listRequest.Buffer.SequenceEqual(new byte[]{10,123,0,0,0}) && trackRequest.Buffer[0]==11, "requests only track and entry list");
        Send(4, w => { w.Write(123); w.Write((ushort)2); w.Write((ushort)7); w.Write((ushort)8); });
        Send(5, w => { w.Write(123); Text(w,"Spa"); w.Write(12); w.Write(7004); w.Write((byte)0); w.Write((byte)0); });
        Session(0); Car(8,1,"Rival"); Car(7,2,"Player");
        Send(7, w => { w.Write((byte)4); Text(w,"test incident"); w.Write(300); w.Write(8); });
        Send(3, w => w.Write((ushort)42)); // Truncation must leave the last complete state intact.
        await Task.Delay(40);
        var when = now.AddMilliseconds(300);
        var feed = client.Poll(1, shared, when);
        var view = shared with { Broadcast = feed, CapturedAt = when };
        var radio = new TelemetryQueries();
        Check(radio.Answer("前车是谁", view, when).Text == "Rival Driver", "uses player ID instead of focused car");
        Check(feed.Traffic is { PlayerId: 7, Racing: true } && feed.Traffic.AtPosition(1)?.SpeedKph == 155,
            "typed traffic associates player, race phase and opponent speeds");
        Check(feed.Fields["UDP.rank.1"].TextValue!.Contains("155 km/h"), "rank query contains live car packet");
        Check(feed.Fields["UDP.sectors"].TextValue!.Contains("第3段"), "computed sector ranks");
        Check(feed.Fields["UDP.event"].TextValue!.Contains("test incident"), "event decoded");
        Check(!JsonSerializer.Serialize(view).Contains("Rival"), "driver identity stays local");
        Check(!radio.Answer("前车是谁", view, when.AddSeconds(3)).HasVerifiedData, "stale opponents blocked");
        Session(1); await Task.Delay(25);
        feed = client.Poll(1, shared, now.AddMilliseconds(600));
        Check(!feed.Fields.ContainsKey("UDP.ahead.driver"), "new session drops old opponent and roster");
    }
}
