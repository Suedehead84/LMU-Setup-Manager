using Lmu.Localization;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
namespace Lmu.Diagnostics;
[SupportedOSPlatform("windows")]
public sealed partial class SharedMemorySource : ITelemetrySource
{
    private readonly string mappingName;
    public SharedMemorySource(string mappingName = "LMU_Data") => this.mappingName = mappingName;
    private string? lastWitness;
    private DateTimeOffset lastProgress;
    public Sample Read(DateTimeOffset now)
    {
        try
        {
            // Open and dispose each poll: avoids holding a dead mapping after game shutdown.
            using var map=MemoryMappedFile.OpenExisting(mappingName,MemoryMappedFileRights.Read);
            using var view=map.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read);
            byte[] ReadBytes(long o,int n) { var b=new byte[n]; if(view.ReadArray(o,b,0,n)!=n) throw new IOException(UiText.C("diagnostics.shortMappingRead")); return b; }
            for(int attempt=0;attempt<3;attempt++)
            {
                if(view.Capacity<68) return Missing(now,UiText.C("diagnostics.mappingTooSmallForVersionField"));
                int version=view.ReadInt32(64);
                if(!SupportedAbi.IsSupported(version))
                    return Missing(now,UiText.C("diagnostics.unsupportedAbi",version));
                if(view.Capacity<Layout.MappingSize) return Missing(now,UiText.C("diagnostics.mappingTooSmallForValidatedAbi"));
                var header=ReadBytes(Layout.Telemetry,3);
                int active=header[0],idx=header[1],has=header[2];

                if(active>104 || idx>=104 || has>1) return Missing(now,UiText.C("diagnostics.invalidTelemetryHeader"));
                long telem=Layout.TelemStart+idx*Layout.T_Size;
                var beforeS=view.ReadInt64(Layout.Scoring+Layout.S_mCurrentET);
                var beforeT=view.ReadInt64(telem+Layout.T_mElapsedTime);
                var s=ReadBytes(Layout.Scoring,Layout.S_Size);
                int count=Decoder.I(s,Layout.S_mNumVehicles);
                if(count<0 || count>104) return Missing(now,UiText.C("diagnostics.invalidVehicleCount"));
                byte[]? t=has==1 && active>0 ? ReadBytes(telem,Layout.T_Size):null;
                byte[]? player=null;
                if(t is not null)
                    for(int i=0;i<count;i++)
                    {
                        var v=ReadBytes(Layout.Vehicles+i*Layout.V_Size,Layout.V_Size);
                        if(v[Layout.V_mIsPlayer]==1 && Decoder.I(v,Layout.V_mID)==Decoder.I(t,Layout.T_mID)) { player=v;break; }
                    }
                if(!header.SequenceEqual(ReadBytes(Layout.Telemetry,3)) ||
                    beforeS!=view.ReadInt64(Layout.Scoring+Layout.S_mCurrentET) ||
                    beforeT!=view.ReadInt64(telem+Layout.T_mElapsedTime) ||
                    count!=view.ReadInt32(Layout.Scoring+Layout.S_mNumVehicles)) continue;
                var witness=$"{beforeS}:{beforeT}:{count}:{has}";
                if(witness!=lastWitness) {lastWitness=witness;lastProgress=now;}
                if(now-lastProgress>TimeSpan.FromSeconds(2)) return Missing(now,UiText.C("diagnostics.sharedMemoryUnchangedForMoreThan2S"));
                if(count==0) return Missing(now,UiText.C("diagnostics.noActiveSession"));
                return new(now,"LIVE",Decoder.Decode(s,t,player,version));
            }
            return Missing(now,UiText.C("diagnostics.inconsistentCopy"));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException)
        { lastWitness=null; return Missing(now,e.GetType().Name+": "+e.Message); }
    }
    private static Sample Missing(DateTimeOffset now,string reason) => new(now,"LIVE",Decoder.Empty(),reason);
}
public sealed class DemoSource : ITelemetrySource
{
    private int tick;
    public Sample Read(DateTimeOffset now)
    {
        var f=Decoder.Empty();
        f["CarName"]=new(UiText.C("diagnostics.syntheticTestData"),"demo");
        f["FixedSetup"]=new(tick<4 ? true:null,"demo");
        f["TC"]=new(tick++<2?3:4,"demo","Stufe");
        return new(now,"DEMO",f,UiText.C("diagnostics.noConnectionToLmuSyntheticTestDataOnly"));
    }
}


