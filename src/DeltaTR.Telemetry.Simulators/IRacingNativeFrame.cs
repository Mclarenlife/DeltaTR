using System.Globalization;
using System.Text;
using DeltaTR.Core;

namespace DeltaTR.Telemetry.Simulators;

internal sealed record SdkVariable(int Type,int Offset,int Count,string Unit,string Description,bool CountAsTime = false)
{
    internal static int Width(int type) => type switch {0 or 1=>1,2 or 3 or 4=>4,5=>8,_=>0};
    internal double Number(byte[] row,int index)
    {
        var at=Offset+index*Width(Type);
        return Type switch {0 or 1=>row[at],2=>BitConverter.ToInt32(row,at),3=>BitConverter.ToUInt32(row,at),
            4=>BitConverter.ToSingle(row,at),5=>BitConverter.ToDouble(row,at),_=>double.NaN};
    }

    internal Measurement? Read(byte[] row,string name,DateTimeOffset stamp,int? element = null)
    {
        if (element is int index)
        {
            if (index < 0 || index >= Count) return null;
            return (this with { Offset = Offset + index * Width(Type), Count = 1, CountAsTime = false }).Read(row,name,stamp);
        }
        if (Type==0 && Count>1)
        {
            var bytes = row.AsSpan(Offset,Count);
            var end=bytes.IndexOf((byte)0);
            return SimulatorRace.Text(Encoding.Latin1.GetString(end<0?bytes:bytes[..end]),stamp,name);
        }
        var tyrePressure = (Unit is "kPa" or "Pa") && (name.EndsWith("coldPressure") || name.EndsWith("pressure") ||
            name.EndsWith("TireColdPress") || name is "IR.Var.PitSvLFP" or "IR.Var.PitSvRFP" or "IR.Var.PitSvLRP" or "IR.Var.PitSvRRP");
        var unit = Type==1 ? "boolean" : tyrePressure ? "psi" : Unit switch { "C"=>"°C", "l"=>"L", _=>Unit };
        double Value(int i) => tyrePressure ? Number(row,i)/(Unit == "Pa" ? 6894.757293168 : 6.894757293168) : Number(row,i)*(Unit=="%"?100:1);
        string Format(int i) => Type==3 ? $"0x{(uint)Number(row,i):X8}" :
            double.IsFinite(Value(i)) ? Value(i).ToString("0.#####",CultureInfo.InvariantCulture) : "暂无";
        if (Count==1 && Type!=3)
        {
            var n=Value(0);
            return double.IsFinite(n)?new(n,unit,stamp,DataQuality.Valid,name,""):null;
        }
        var text=Count==1 ? Format(0) : string.Join("；",Enumerable.Range(0,Count).Select(i=>$"[{i}] {Format(i)}"));
        return SimulatorRace.Text(text,stamp,name,unit);
    }
}

internal sealed class IRacingNativeFrame(byte[] row,IReadOnlyDictionary<string,SdkVariable> vars,
    IReadOnlyDictionary<string,string> session,IReadOnlyDictionary<int,string> drivers,int playerId,DateTimeOffset stamp)
{
    private readonly Lazy<Dictionary<string,string>> reports = new(() =>
    {
        IEnumerable<SimulatorRace.Car> cars=[];
        if (vars.TryGetValue("CarIdxPosition",out var positions) && positions.Type==2)
            cars = Enumerable.Range(0,positions.Count).Select(i=>new SimulatorRace.Car(i,(int)positions.Number(row,i),drivers.GetValueOrDefault(i)??""));
        string? flags=null;
        if (vars.TryGetValue("SessionFlags",out var f) && f.Count==1 && f.Type==3)
        {
            var bits=(uint)f.Number(row,0);
            var labels=new List<string>();
            foreach (var (mask,name) in new (uint,string)[]{(1,"方格旗"),(2,"白旗"),(4,"绿旗"),(8,"黄旗"),(16,"红旗"),(32,"蓝旗"),
                (64,"碎片旗"),(256,"挥动黄旗"),(0x4000,"全场警戒"),(0x8000,"全场警戒挥旗"),(0x10000,"黑旗"),(0x20000,"取消资格"),(0x100000,"维修旗")})
                if ((bits&mask)!=0) labels.Add(name);
            flags = bits==0 ? "无旗" : string.Join("、",labels)+$"（标志位 0x{bits:X8}）";
        }
        return SimulatorRace.Reports(cars,playerId,flags);
    });

    public Measurement? Read(string key)
    {
        if (key.StartsWith("IR.Var.",StringComparison.Ordinal) && vars.TryGetValue(key[7..],out var v))
            return v.Read(row,key,stamp);
        if (key.StartsWith("IR.Player.",StringComparison.Ordinal) && vars.TryGetValue(key[10..],out var own))
            return own.Read(row,key,stamp,playerId);
        if (key.StartsWith("RaceFeed.",StringComparison.Ordinal)) return SimulatorRace.Text(reports.Value.GetValueOrDefault(key),stamp,key);
        return session.TryGetValue(key,out var value) ? SimulatorRace.Text(value,stamp,key) : null;
    }
}
