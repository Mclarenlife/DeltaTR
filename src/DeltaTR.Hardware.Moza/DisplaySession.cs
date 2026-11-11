// Experimental KS Pro display-only transport. Never writes motor settings.
// Wire format reference: giantorth/AZOM protocol documentation, commit
// 6f820a8dd373d51764600b42e5c922204f663743. No motor-setting commands.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

public sealed class MozaDisplaySession
{
    readonly Action<byte[]> write;
    readonly Func<int> read;
    readonly Action<string> log;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<byte> wire = new List<byte>();
    readonly Dictionary<byte,List<byte>> streams = new Dictionary<byte,List<byte>>();
    readonly Dictionary<byte,ushort> expected = new Dictionary<byte,ushort>();
    readonly Dictionary<byte,SortedDictionary<ushort,byte[]>> reorder = new Dictionary<byte,SortedDictionary<ushort,byte[]>>();
    readonly HashSet<string> acks = new HashSet<string>();
    readonly Dictionary<byte,ushort> outbound = new Dictionary<byte,ushort> { {1,2}, {2,3} };
    readonly Dictionary<byte,uint> ends = new Dictionary<byte,uint>();
    string model = "";
    byte catalogSession, channel;
    bool acknowledge;
    int pushCount;
    int frameLogCount;
    CancellationToken cancellation;

    public MozaDisplaySession(Action<byte[]> write, Func<int> read, Action<string> log)
    { this.write=write; this.read=read; this.log=log; }
    void Report(string s) { log(DateTimeOffset.Now.ToString("O") + " " + s); }
    public static uint Crc(byte[] data)
    {
        uint crc=0xffffffff;
        foreach (byte b in data) { crc^=b; for(int j=0;j<8;j++) crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0u); }
        return crc^0xffffffff;
    }
    public static byte[] Frame(byte[] payload)
    {
        if(payload.Length<1 || payload.Length>64) throw new ArgumentOutOfRangeException("payload");
        var raw=new List<byte>{0x7e,(byte)payload.Length,0x43,0x17}; raw.AddRange(payload);
        int checksum=13+raw.Sum(x=>(int)x)+raw.Skip(2).Count(x=>x==0x7e)*0x7e;
        raw.Add((byte)checksum);
        var result=new List<byte>{0x7e};
        foreach(byte b in raw.Skip(1)) { result.Add(b); if(b==0x7e)result.Add(b); }
        return result.ToArray();
    }
    static byte[] Tlv(byte kind, byte[] body)
    { return new[]{kind}.Concat(BitConverter.GetBytes((uint)body.Length)).Concat(body).ToArray(); }
    public static byte[] StringRecord(byte index,string value)
    {
        if(index==0) throw new ArgumentOutOfRangeException("index");
        var text=new List<byte>();
        foreach(var rune in value.EnumerateRunes())
        {
            byte[] bytes=Encoding.UTF8.GetBytes(rune.ToString());
            if(text.Count+bytes.Length>127)break;
            text.AddRange(bytes);
        }
        return Tlv(5,new[]{index,(byte)(128|text.Count)}.Concat(text).ToArray());
    }
    void Send(params byte[] payload) { write(Frame(payload)); }
    static string AckKey(byte session,ushort seq) { return session+":"+seq; }
    void Pump(int ms)
    {
        long until=clock.ElapsedMilliseconds+ms;
        while(clock.ElapsedMilliseconds<until)
        {
            cancellation.ThrowIfCancellationRequested();
            int b=read(); if(b<0)continue;
            wire.Add((byte)b); ParseFrames();
            if(wire.Count>2048)throw new IOException("Receive buffer limit exceeded.");
        }
    }
    void ParseFrames()
    {
        while(wire.Count>=2)
        {
            if(wire[0]!=0x7e) {wire.RemoveAt(0);continue;}
            int length=wire[1];
            if(length<1 || length>64) {wire.RemoveAt(0);continue;}
            var decoded=new List<byte>{0x7e}; int at=1; bool bad=false;
            while(decoded.Count<length+5)
            {
                if(at>=wire.Count)return;
                byte b=wire[at++];
                if(b==0x7e)
                {
                    if(at>=wire.Count)return;
                    if(wire[at]!=0x7e){bad=true;break;} at++;
                }
                decoded.Add(b);
            }
            if(bad){wire.RemoveAt(0);continue;}
            int check=13+decoded.Take(decoded.Count-1).Sum(x=>(int)x)
                +decoded.Skip(2).Take(decoded.Count-3).Count(x=>x==0x7e)*0x7e;
            if((byte)check!=decoded[decoded.Count-1]){wire.RemoveAt(0);continue;}
            wire.RemoveRange(0,at);
            if(acknowledge && decoded[2]==0xc3 && frameLogCount++<40)
                Report("RX "+Convert.ToHexString(decoded.Take(Math.Min(decoded.Count,20)).ToArray()));
            if(decoded[2]==0xc3 && decoded[3]==0x71) Handle(decoded.Skip(4).Take(length).ToArray());
        }
    }
    void Handle(byte[] p)
    {
        if(p.Length>=2 && p[0]==0x87)
        { model=Encoding.UTF8.GetString(p,2,p.Length-2).TrimEnd('\0'); Report("DISPLAY "+model); return; }
        if(p.Length==5 && p[0]==0xfc && p[1]==0)
        {acks.Add(AckKey(p[2],BitConverter.ToUInt16(p,3)));return;}
        if(p.Length<6 || p[0]!=0x7c || p[1]!=0)return;
        byte session=p[2],type=p[3]; ushort seq=BitConverter.ToUInt16(p,4);
        if(!acknowledge)return;
        if(type==0x81 || type==0)
        {
            Send(0xfc,0,session,(byte)seq,(byte)(seq>>8));
            Report("SESSION "+session+" control="+type+" seq="+seq);
            return;
        }
        if(type!=1 || p.Length<10)return;
        byte[] net=p.Skip(6).Take(p.Length-10).ToArray();
        if(Crc(net)!=BitConverter.ToUInt32(p,p.Length-4))return;
        Send(0xfc,0,session,(byte)seq,(byte)(seq>>8));
        if(session!=1 && session!=2)return;
        if(!expected.ContainsKey(session))
        {expected[session]=seq; reorder[session]=new SortedDictionary<ushort,byte[]>();streams[session]=new List<byte>();}
        // Sequence numbers are 16-bit and wrap during long connections.
        if(unchecked((short)(seq-expected[session]))<0)return;
        reorder[session][seq]=net;
        if(reorder[session].Count>128)throw new IOException("Too many out-of-order chunks.");
        while(reorder[session].TryGetValue(expected[session],out var ordered))
        {
            reorder[session].Remove(expected[session]); expected[session]++;
            // KS Pro sends a CRC-protected one-byte FF stream-start marker.
            // It is not the beginning of a length-prefixed FF record.
            if(ordered.Length==1 && ordered[0]==0xff)continue;
            // The four-byte zero transport keepalive is not a channel TLV.
            if(ordered.Length==4 && ordered.All(x=>x==0))continue;
            streams[session].AddRange(ordered); ParseStream(session);
        }
    }
    void ParseStream(byte session)
    {
        var b=streams[session];
        while(b.Count>=5)
        {
            uint size=BitConverter.ToUInt32(b.ToArray(),1); byte type=b[0];
            if(type==0xff && size==0xff000000){b.RemoveRange(0,5);continue;}
            if(size>65536){b.RemoveAt(0);continue;}
            int header=type==0xff?9:5;
            if(b.Count<header+size)return;
            byte[] body=b.Skip(header).Take((int)size).ToArray(); b.RemoveRange(0,header+(int)size);
            if(type==4 && body.Length>8)
            {
                string url=Encoding.ASCII.GetString(body,1,body.Length-1);
                if(url.StartsWith("v1/"))Report("CATALOG session="+session+" idx="+body[0]+" "+url);
                if(url=="v1/gameData/DeltaTRRadio" && body[0]!=0)
                { catalogSession=session; channel=body[0]; }
            }
            if(type==6 && body.Length==4)
            { ends[session]=BitConverter.ToUInt32(body,0); Report("END session="+session+" counter="+ends[session]); }
        }
        if(b.Count>65536)throw new IOException("Stream limit exceeded.");
    }
    void Data(byte session,byte[] bytes)
    {
        for(int offset=0;offset<bytes.Length;offset+=54)
        {
            byte[] net=bytes.Skip(offset).Take(54).ToArray(); ushort seq=outbound[session]++;
            var payload=new byte[]{0x7c,0,session,1,(byte)seq,(byte)(seq>>8)}.Concat(net).Concat(BitConverter.GetBytes(Crc(net))).ToArray();
            string key=AckKey(session,seq); acks.Remove(key);
            for(int attempt=0;attempt<3;attempt++)
            {
                Send(payload); Pump(250);
                if(acks.Remove(key))goto accepted;
            }
            throw new IOException("Display did not acknowledge data on session "+session+" seq="+seq);
            accepted: ;
        }
    }
    public void Run(int seconds) => RunCore(seconds, null, CancellationToken.None);
    public void RunLive(Func<string> message, CancellationToken token) => RunCore(null, message, token);
    void RunCore(int? seconds, Func<string>? messageProvider, CancellationToken token)
    {
        if(seconds.HasValue && (seconds<10 || seconds>120))throw new ArgumentOutOfRangeException("seconds");
        cancellation=token;
        // Identity is read-only. Refuse session writes to any unidentified display.
        Send(7,1); Pump(1500);
        if(!model.Contains("W18"))throw new IOException("Expected W18 Display; found '"+model+"'.");
        try
        {
            for(byte s=1;s<=3;s++)Send(0x7c,0,s,0,0,0);
            Report("Waiting 14 seconds without session traffic for previous host to retire."); Pump(14000);
            wire.Clear(); acks.Clear(); acknowledge=true;
            for(byte s=1;s<=2;s++) {Send(0x7c,0,s,0x81,s,0,s,0,0xfd,2);Pump(500);}
            long until=clock.ElapsedMilliseconds+18000;
            while((channel==0 || !ends.ContainsKey(catalogSession)) && clock.ElapsedMilliseconds<until)Pump(200);
            if(channel==0 || !ends.ContainsKey(catalogSession))throw new IOException("No fresh DeltaTRRadio catalog + END; no guessed channel writes performed.");
            byte ff=(byte)(catalogSession==1?2:1);
            // Only benign display handshake records 2/7. No properties, FFB catalogs or switches.
            byte[] nonce=BitConverter.GetBytes(2u).Concat(BitConverter.GetBytes((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                .Concat(new byte[]{0,0,0,0,0x90,0x9d,0xff,0xff}).ToArray();
            Data(ff,WrapFf(nonce));
            Data(ff,WrapFf(BitConverter.GetBytes(7u).Concat(BitConverter.GetBytes(3u)).Concat(new byte[4]).ToArray()));
            Data(catalogSession,Tlv(7,BitConverter.GetBytes(2u)).Concat(Tlv(3,Array.Empty<byte>())).ToArray());
            Data(catalogSession,Tlv(6,BitConverter.GetBytes(ends[catalogSession])));
            Report("STRING CHANNEL READY. Acknowledgement does not prove physical rendering.");
            long stop=seconds.HasValue?clock.ElapsedMilliseconds+seconds.Value*1000L:long.MaxValue;
            string[] messages={"测试一：当前胎温八十六度","测试二：建议第五圈进站","测试三：已收到工程师回答"};
            while(clock.ElapsedMilliseconds<stop)
            {
                cancellation.ThrowIfCancellationRequested();
                string message=messageProvider!=null ? messageProvider() : messages[pushCount%messages.Length]+" #"+(pushCount+1);
                Data(catalogSession,StringRecord(channel,message));pushCount++;
                if(messageProvider==null)Report("PUSH ACKED "+message);
                Pump(messageProvider==null?2000:80);
            }
        }
        finally
        {
            acknowledge=false;
            for(byte s=1;s<=2;s++)try {Send(0x7c,0,s,0,0,0);}catch(IOException) { }
            Report("CLOSED. pushes="+pushCount+"; physical result requires observation.");
        }
    }
    static byte[] WrapFf(byte[] body)
    { return new byte[]{255}.Concat(BitConverter.GetBytes((uint)body.Length)).Concat(BitConverter.GetBytes(Crc(body))).Concat(body).ToArray(); }
    public static void SelfTest()
    {
        if(Crc(Encoding.ASCII.GetBytes("123456789"))!=0xcbf43926)throw new Exception("CRC test failed");
        if(Convert.ToHexString(Frame(new byte[]{7,1}))!="7E0243170701EF")throw new Exception("Frame test failed");
        var chinese=StringRecord(1,new string('胎',50));
        if(chinese.Length!=133 || chinese[6]!=254 || new UTF8Encoding(false,true).GetString(chinese,7,chinese.Length-7).Length!=42)
            throw new Exception("UTF-8 boundary test failed");
        var test=new MozaDisplaySession(_=>{},()=>-1,_=>{}); test.acknowledge=true;
        ushort seq=5;
        foreach(byte[] net in new[]{new byte[]{255},Tlv(3,BitConverter.GetBytes(1u)),
            Tlv(4,new byte[]{1}.Concat(Encoding.ASCII.GetBytes("v1/gameData/DeltaTRRadio")).ToArray()),Tlv(6,BitConverter.GetBytes(1u))})
        {
            test.Handle(new byte[]{0x7c,0,1,1,(byte)seq++,0}.Concat(net).Concat(BitConverter.GetBytes(Crc(net))).ToArray());
        }
        if(test.channel!=1 || test.catalogSession!=1 || test.ends[1]!=1)throw new Exception("KS Pro stream-start/catalog test failed");
        test.expected[1]=65535;
        foreach(ushort next in new ushort[]{65535,0})
        {
            byte[] marker={255};
            test.Handle(new byte[]{0x7c,0,1,1,(byte)next,(byte)(next>>8)}.Concat(marker).Concat(BitConverter.GetBytes(Crc(marker))).ToArray());
        }
        if(test.expected[1]!=1 || test.reorder[1].Count!=0)throw new Exception("Sequence wrap test failed");
    }
}

