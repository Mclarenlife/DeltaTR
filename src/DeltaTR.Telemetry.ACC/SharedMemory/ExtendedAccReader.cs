using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using DeltaTR.Core;

namespace DeltaTR.Telemetry.ACC.SharedMemory;

internal static class ExtendedAccReader
{
    private sealed record Binding(AccReading Reading, int Offset);
    private static readonly Binding[] Bindings = AccChannels.Readings.Select(r =>
    {
        var layout = r.Channel.Page switch { AccPage.Physics => AccLayout.Physics, AccPage.Graphics => AccLayout.Graphics, _ => AccLayout.Static };
        var stride = r.Channel.Shape is AccShape.WheelVectors or AccShape.CarSlot ? 12 : 4;
        return new Binding(r, layout[r.Channel.Member] + Math.Max(0, r.Index) * stride);
    }).ToArray();

    public static void Read(byte[] physics, byte[] graphics, byte[] info, Dictionary<string, RawValue> values, Dictionary<string, RawValue> local)
    {
        var cars = Int(graphics, AccLayout.Graphics["activeCars"]);
        foreach (var binding in Bindings)
        {
            var r = binding.Reading;
            var c = r.Channel;
            var data = c.Page switch { AccPage.Physics => physics, AccPage.Graphics => graphics, _ => info };
            var offset = binding.Offset;
            double number;
            string? text = null;
            var min = c.Min;
            var max = c.Max;
            switch (c.Shape)
            {
                case AccShape.CarSlot:
                    if (cars is < 1 or > 60 || r.Slot >= cars) continue;
                    var carId = Int(graphics, AccLayout.Graphics["carID"] + r.Slot * 4);
                    var position = Vector(data, offset);
                    text = carId >= 0 && position.Length > 0 ? $"车辆编号 {carId}，{position}" : "";
                    number = 0;
                    break;
                case AccShape.Text15:
                case AccShape.Text33:
                    var chars = c.Shape == AccShape.Text15 ? 15 : 33;
                    text = Encoding.Unicode.GetString(data, offset, chars * 2).Split('\0')[0].Trim();
                    if (text is "-:--:---" or "--:--.---" || text.Any(char.IsControl)) text = "";
                    number = 0;
                    break;
                case AccShape.WheelVectors:
                case AccShape.Vector:
                    text = Vector(data, offset);
                    number = 0;
                    break;
                case AccShape.Integer:
                case AccShape.Switch:
                    number = Int(data, offset);
                    if (c.Shape == AccShape.Switch) { min = 0; max = 1; }
                    if (c.Unit == "ms" && number == int.MaxValue) number = double.NaN;
                    break;
                case AccShape.Fraction:
                    number = Float(data, offset) * 100d;
                    min = 0; max = 100;
                    break;
                default:
                    number = Float(data, offset);
                    break;
            }
            var destination = c.LocalOnly ? local : values;
            destination.Add(r.Key, new(number, c.Unit, r.Source, min, max, text));
        }
    }

    private static int Int(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4));
    private static float Float(byte[] bytes, int at) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at, 4));
    private static string Vector(byte[] bytes, int at)
    {
        var x = Float(bytes, at);
        var y = Float(bytes, at + 4);
        var z = Float(bytes, at + 8);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return "";
        return string.Create(CultureInfo.InvariantCulture, $"X {x:0.###}，Y {y:0.###}，Z {z:0.###}");
    }
}
