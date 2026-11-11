using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace DeltaTR.Voice;

public sealed class TencentVoiceOptions(string appId, string secretId, string secretKey, int voice)
{
    internal string AppId { get; } = appId.Trim();
    internal string SecretId { get; } = secretId.Trim();
    internal string SecretKey { get; } = secretKey.Trim();
    public int Voice { get; } = voice;
    public void Validate()
    {
        if (!uint.TryParse(AppId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0 ||
            SecretId.Length == 0 || SecretKey.Length == 0 || Voice <= 0)
            throw new ArgumentException("请填写腾讯云 AppID、SecretId、SecretKey 并选择音色。");
    }
}

internal static class TencentSpeech
{
    private const string Endpoint = "tts.cloud.tencent.com/stream_ws";
    internal static Uri SignedUri(TencentVoiceOptions options, string text, long now, string session, int rate = 0)
    {
        options.Validate();
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal) {
            ["Action"] = "TextToStreamAudioWS", ["AppId"] = options.AppId, ["Codec"] = "pcm",
            ["Expired"] = (now + 600).ToString(CultureInfo.InvariantCulture), ["SampleRate"] = "16000",
            ["SecretId"] = options.SecretId, ["SessionId"] = session, ["Text"] = text,
            ["Timestamp"] = now.ToString(CultureInfo.InvariantCulture), ["VoiceType"] = options.Voice.ToString(CultureInfo.InvariantCulture)
        };
        if (rate != 0) values["Speed"] = rate.ToString(CultureInfo.InvariantCulture);
        string raw = string.Join('&', values.Select(x => x.Key + "=" + x.Value));
        var signature = HMACSHA1.HashData(Encoding.UTF8.GetBytes(options.SecretKey), Encoding.UTF8.GetBytes("GET" + Endpoint + "?" + raw));
        string query = string.Join('&', values.Select(x => x.Key + "=" + Uri.EscapeDataString(x.Value)));
        return new Uri("wss://" + Endpoint + "?" + query + "&Signature=" + Uri.EscapeDataString(Convert.ToBase64String(signature)));
    }

    // Leave room below the 1800 UTF-8 byte limit and finish at punctuation where possible.
    internal static IEnumerable<string> Parts(string text)
    {
        while (text.Length > 0)
        {
            int end = 0, boundary = 0, bytes = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 1500) break;
                bytes += rune.Utf8SequenceLength;
                end += rune.Utf16SequenceLength;
                if ("。！？；，.!?;\n".Contains((char)rune.Value)) boundary = end;
            }
            int count = end == text.Length || boundary == 0 ? end : boundary;
            yield return text[..count];
            text = text[count..];
        }
    }

    internal static async Task ReceiveAsync(WebSocket socket, Func<byte[], int, CancellationToken, Task> write, CancellationToken token)
    {
        var buffer = new byte[8192];
        using var control = new MemoryStream();
        bool receivedAudio = false;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(TimeSpan.FromSeconds(12));
            WebSocketReceiveResult frame;
            try { frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), idle.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("腾讯云语音响应超时。"); }
            if (frame.MessageType == WebSocketMessageType.Close) throw new IOException("腾讯云提前结束了语音连接。");
            if (frame.MessageType == WebSocketMessageType.Binary)
            {
                if (frame.Count > 0) { await write(buffer, frame.Count, token).ConfigureAwait(false); receivedAudio = true; }
                continue;
            }
            if (control.Length + frame.Count > 65536) throw new IOException("腾讯云语音响应超出限制。");
            control.Write(buffer, 0, frame.Count);
            if (!frame.EndOfMessage) continue;
            using var json = JsonDocument.Parse(control.GetBuffer().AsMemory(0, (int)control.Length));
            var root = json.RootElement;
            int code = root.GetProperty("code").GetInt32();
            if (code != 0) throw new IOException($"腾讯云拒绝合成（{code}），请检查密钥、服务开通状态、音色权限及额度。");
            if (root.TryGetProperty("final", out var final) && final.GetInt32() == 1)
            {
                if (!receivedAudio) throw new IOException("腾讯云未返回音频。");
                return;
            }
            control.SetLength(0);
        }
    }

    // The player can outrun the network. Pad during synthesis, then signal EOF after the final PCM sample.
    private sealed class LivePcm : IWaveProvider
    {
        public BufferedWaveProvider Buffer { get; } = new(new WaveFormat(16000, 16, 1)) { ReadFully = false };
        public volatile bool Complete;
        public WaveFormat WaveFormat => Buffer.WaveFormat;
        public int Read(byte[] bytes, int offset, int count)
        {
            bool complete = Complete;
            int read = Buffer.Read(bytes, offset, count);
            if (complete || read == count) return read;
            Array.Clear(bytes, offset + read, count - read);
            return count;
        }
    }

    public static async Task PlayAsync(TencentVoiceOptions options, string text, int device, int volume, int rate,
        Action started, CancellationToken token)
    {
        var pcm = new LivePcm();
        using var output = new WaveOutEvent { DeviceNumber = device, DesiredLatency = 120 };
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) => { if (e.Exception is null) ended.TrySetResult(); else ended.TrySetException(e.Exception); };
        bool playing = false;
        byte? carry = null;
        async Task Write(byte[] bytes, int count, CancellationToken ct)
        {
            // A WebSocket fragment can end between the two bytes of a PCM sample.
            var data = new byte[count + (carry.HasValue ? 1 : 0)];
            if (carry is byte first) data[0] = first;
            Array.Copy(bytes, 0, data, data.Length - count, count);
            int aligned = data.Length & ~1;
            carry = aligned == data.Length ? null : data[^1];
            if (aligned == 0) return;
            while (pcm.Buffer.BufferLength - pcm.Buffer.BufferedBytes < aligned)
            {
                if (ended.Task.IsCompleted) { await ended.Task.ConfigureAwait(false); throw new IOException("音频设备已停止。"); }
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            pcm.Buffer.AddSamples(data, 0, aligned);
            if (!playing)
            {
                output.Init(pcm);
                output.Volume = Math.Clamp(volume, 0, 100) / 100f;
                ct.ThrowIfCancellationRequested();
                output.Play(); playing = true; started();
            }
        }
        foreach (string part in Parts(text))
        {
            token.ThrowIfCancellationRequested();
            using var socket = new ClientWebSocket();
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
            connect.CancelAfter(TimeSpan.FromSeconds(6));
            var uri = SignedUri(options, part, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Guid.NewGuid().ToString("D"), rate);
            try { await socket.ConnectAsync(uri, connect.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("腾讯云连接超时。"); }
            catch (WebSocketException) { throw new IOException("无法连接腾讯云，请检查网络、系统时间和密钥。"); }
            await ReceiveAsync(socket, Write, token).ConfigureAwait(false);
            if (carry.HasValue) throw new IOException("腾讯云返回了不完整的音频。");
            if (socket.State == WebSocketState.Open)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", token).ConfigureAwait(false);
        }
        pcm.Complete = true;
        if (playing) await ended.Task.WaitAsync(token).ConfigureAwait(false);
    }
}
