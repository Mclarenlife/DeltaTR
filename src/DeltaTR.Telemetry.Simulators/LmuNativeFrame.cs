using System.Globalization;
using System.Text;
using DeltaTR.Core;
using static DeltaTR.Core.LmuSchema;

namespace DeltaTR.Telemetry.Simulators;

internal sealed class LmuNativeFrame(byte[] data, int car, int player, int count, DateTimeOffset physics, DateTimeOffset? scoring)
{
    private const int Score = 1632, Cars = 2192, PhysicsCars = 128468;
    private readonly Lazy<Dictionary<string,string>> reports = new(() =>
    {
        var entries = Enumerable.Range(0,count).Select(i => Cars + 584*i).Select(row =>
            new SimulatorRace.Car(BitConverter.ToInt32(data,row),data[row+199],Text(data,row+4,32)));
        var yellow = ((sbyte)data[Score+109]) switch {0=>"无全场黄旗",1=>"黄旗待定",2=>"维修区关闭",3=>"领先圈车辆可进站",
            4=>"维修区开放",5=>"黄旗最后一圈",6=>"恢复比赛",_=>"全场黄旗状态未知"};
        var local = data[player+504] switch {0=>"绿旗",6=>"蓝旗",var code=>$"旗语代码{code}"};
        var flags = $"{yellow}，本车{local}，赛段旗语代码 {data[Score+110]}、{data[Score+111]}、{data[Score+112]}";
        return SimulatorRace.Reports(entries,BitConverter.ToInt32(data,car),flags);
    });

    private static string Text(byte[] bytes,int offset,int length)
    {
        var end = Array.IndexOf(bytes,(byte)0,offset,length);
        return Encoding.UTF8.GetString(bytes,offset,end < 0 ? length : end-offset).Trim();
    }

    public Measurement? Read(string key)
    {
        if (key.StartsWith("RaceFeed.",StringComparison.Ordinal))
            return scoring is { } time ? SimulatorRace.Text(reports.Value.GetValueOrDefault(key),time,"LMU.Scoring") : null;
        if (!Channels.TryGetValue(key,out var c)) return null;
        var m = c.Member;
        bool timed = m.Section is Section.Timing or Section.Session or Section.Results;
        var stamp = timed ? scoring : physics;
        if (stamp is not { } at) return null;
        if (c.Grid)
        {
            if (scoring is not { } scored) return null;
            var values = new List<string>();
            var slots = new Dictionary<int,int>();
            if (!timed)
            {
                var elapsed = BitConverter.ToDouble(data,car+12);
                for (var i=0; i<data[128464]; i++)
                {
                    int slot=PhysicsCars+i*1888;
                    if (Math.Abs(elapsed-BitConverter.ToDouble(data,slot+12)) < .5)
                        slots[BitConverter.ToInt32(data,slot)] = slot;
                }
                slots[BitConverter.ToInt32(data,car)] = car;
            }
            for (var i=0;i<count;i++)
            {
                var row=Cars+i*584;
                var id=BitConverter.ToInt32(data,row);
                int slot=row;
                if (!timed && !slots.TryGetValue(id,out slot)) continue;
                var reading = At(c,slot,at);
                if (reading != null) values.Add($"车辆{id} {reading.TextValue ?? reading.Value?.ToString("0.###",CultureInfo.InvariantCulture)} {reading.Unit}".TrimEnd());
            }
            return SimulatorRace.Text(string.Join("；",values), scored < at ? scored : at, key);
        }
        var start = m.Section switch { Section.Header => 0, Section.Results or Section.Session => Score,
            Section.Timing => player, _ => car };
        return At(c,start,at);
    }

    private Measurement? At(Channel c,int start,DateTimeOffset time)
    {
        var m=c.Member;
        var offset=start+m.Offset+(m.Section==Section.Wheel ? 848+260*c.Wheel : 0);
        if (m.Type==Wire.Text) return SimulatorRace.Text(Text(data,offset,m.Count),time,c.Field.Key);
        int width=m.Type switch {Wire.F64=>8, Wire.F32 or Wire.I32 or Wire.U32=>4, Wire.U16 or Wire.I16=>2, _=>1};
        double Number(int i)
        {
            int pos=offset+i*width;
            double v=m.Type switch { Wire.F64=>BitConverter.ToDouble(data,pos),Wire.F32=>BitConverter.ToSingle(data,pos),
                Wire.I32=>BitConverter.ToInt32(data,pos),Wire.U32=>BitConverter.ToUInt32(data,pos),Wire.I16=>BitConverter.ToInt16(data,pos),
                Wire.U16=>BitConverter.ToUInt16(data,pos),Wire.I8=>(sbyte)data[pos],_=>data[pos] };
            return m.Unit switch { "K"=>v-273.15,"fraction"=>v*100,"byteFraction"=>v*100/255,"kPa"=>v/6.894757293168,_=>v };
        }
        var unit=m.Unit switch {"K"=>"°C","fraction" or "byteFraction"=>"%","kPa"=>"psi",_=>m.Type==Wire.Bool?"boolean":m.Unit};
        if (m.Count==1)
        {
            var value=Number(0);
            if (!double.IsFinite(value) || (m.Unit=="K" && value < -100) || (m.Unit=="fraction" && value is <0 or >100)) return null;
            return new(value,unit,time,DataQuality.Valid,c.Field.Key,"");
        }
        var parts=Enumerable.Range(0,m.Count).Select(i=>double.IsFinite(Number(i)) ? Number(i).ToString("0.###",CultureInfo.InvariantCulture) : "无读数");
        return SimulatorRace.Text(string.Join("、",parts),time,c.Field.Key,unit);
    }
}
