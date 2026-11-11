using System.IO.MemoryMappedFiles;

namespace DeltaTR.Telemetry.ACC.SharedMemory;

internal sealed class ReadOnlyPage : IDisposable
{
    private readonly MemoryMappedFile file;
    private readonly MemoryMappedViewAccessor view;
    private readonly int size;

    public ReadOnlyPage(string name, int size)
    {
        this.size = size;
        file = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
        try { view = file.CreateViewAccessor(0, size, MemoryMappedFileAccess.Read); }
        catch { file.Dispose(); throw; }
    }

    public byte[] Read(bool hasPacketId)
    {
        var bytes = new byte[size];
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var before = hasPacketId ? view.ReadInt32(0) : 0;
            view.ReadArray(0, bytes, 0, size);
            if (hasPacketId)
            {
                var after = view.ReadInt32(0);
                if (before == after && BitConverter.ToInt32(bytes, 0) == after) return bytes;
            }
            else
            {
                var second = new byte[size];
                view.ReadArray(0, second, 0, size);
                if (bytes.AsSpan().SequenceEqual(second)) return bytes;
            }
        }
        throw new IOException("共享内存读取期间发生更新；丢弃不一致帧。");
    }

    public void Dispose() { view.Dispose(); file.Dispose(); }
}
