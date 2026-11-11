using System.Net.WebSockets;
using System.Text;
using DeltaTR.Voice;

internal static class TencentSpeechTests
{
    public static void Register(List<(string Name, Func<Task> Run)> cases)
    {
        cases.Add(("Tencent TTS / reference signature, escaping and lossless long UTF8 input", () =>
        {
            var options = new TencentVoiceOptions("12345", "test-id", "fixture-only", 101004);
            var uri = TencentSpeech.SignedUri(options, "黄旗，A+B & 26.5", 1700000000, "fixed").AbsoluteUri;
            var slower = TencentSpeech.SignedUri(options, "黄旗，A+B & 26.5", 1700000000, "fixed", -1).AbsoluteUri;
            if (!slower.Contains("&Speed=-1&") || slower.EndsWith(uri.Split("&Signature=")[1]))
                throw new Exception("Speed was not included in the signed speech request");
            if (!uri.EndsWith("Signature=6PIcDHgHIEWwM3yUjxT8arvWguA%3D") || !uri.Contains("A%2BB%20%26%2026.5") ||
                uri.Contains("fixture-only")) throw new Exception("Incorrect signature or encoding");
            var text = string.Concat(Enumerable.Repeat("前车快零点三秒🏁。", 260));
            var parts = TencentSpeech.Parts(text).ToArray();
            if (parts.Length < 2 || string.Concat(parts) != text || parts.Any(p => Encoding.UTF8.GetByteCount(p) > 1500 || char.IsHighSurrogate(p[^1])))
                throw new Exception("Invalid text chunk boundary");
            return Task.CompletedTask;
        }));
        cases.Add(("Tencent TTS / PCM delivered before final, fragmented status and rejection", async () =>
        {
            byte[] Frame(byte opcode, byte[] bytes) => [opcode, (byte)bytes.Length, .. bytes];
            byte[] Json(byte opcode, string value) => Frame(opcode, Encoding.UTF8.GetBytes(value));
            async Task<byte[]> Read(byte[] wire)
            {
                using var input = new MemoryStream(wire);
                using var socket = WebSocket.CreateFromStream(input, false, null, Timeout.InfiniteTimeSpan);
                using var audio = new MemoryStream();
                await TencentSpeech.ReceiveAsync(socket, (bytes, count, _) => { audio.Write(bytes, 0, count); return Task.CompletedTask; }, CancellationToken.None);
                return audio.ToArray();
            }
            var hello = Json(0x81, "{\"code\":0,\"final\":0}");
            var final = Json(0x81, "{\"code\":0,\"final\":1}");
            var pcm = await Read([..hello,..Frame(0x02,[1,2,3]),..Frame(0x80,[4]),..Json(0x01,"{\"code\":"),..Json(0x80,"0,\"final\":1}")]);
            if (!pcm.SequenceEqual(new byte[]{1,2,3,4})) throw new Exception("Incomplete PCM");
            foreach (var invalid in new[] { final, Json(0x81,"{\"code\":10001,\"final\":1}"), new byte[] {0x88,0} })
            {
                try { await Read(invalid); throw new Exception("Expected synthesis failure"); }
                catch (IOException) { }
            }
        }));
    }
}
