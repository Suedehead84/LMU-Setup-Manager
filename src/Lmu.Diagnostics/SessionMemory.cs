using Lmu.Localization;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Lmu.Diagnostics;

public sealed partial class SharedMemorySource
{
    // Compiler-verified against installed 14200 SDK: generic.appInfo(72) + mOptionsLocation(24).
    public const int SessionOptionsLocation=96;
    private double? lastScoringClock;private DateTimeOffset lastSessionPublication;
    public Sample ReadSession(DateTimeOffset now)
    {
        try {
            using var map=MemoryMappedFile.OpenExisting(mappingName,MemoryMappedFileRights.Read);
            using var view=map.CreateViewAccessor(0,0,MemoryMappedFileAccess.Read);
            if(view.Capacity<Layout.MappingSize||view.ReadInt32(64)!=14200)return Missing(now,UiText.C("diagnostics.sessionSourceValidatedAbi14200Required"));
            for(int attempt=0;attempt<3;attempt++) {
                var events=Enumerable.Range(0,16).Select(i=>view.ReadInt32(i*4)).ToArray();
                // Lifecycle events must win even when the old scoring buffer remains populated.
                if(events.Any(e=>e is 3 or 5 or 7)){lastScoringClock=null;lastSessionPublication=default;return SessionMissing(now,UiText.C("diagnostics.nativeSessionEnded"),events.Any(e=>e is 3 or 5)?"Ended":"SessionEnded");}
                if(events.Contains(4)){lastScoringClock=null;lastSessionPublication=default;return SessionMissing(now,UiText.C("diagnostics.nativeLoading"),"Loading");}
                var location=view.ReadByte(SessionOptionsLocation);var et=view.ReadInt64(Layout.Scoring+Layout.S_mCurrentET);
                var s=new byte[Layout.S_Size];view.ReadArray(Layout.Scoring,s,0,s.Length);int n=Decoder.I(s,Layout.S_mNumVehicles);
                if(location==1||n is <1 or >104){lastScoringClock=null;lastSessionPublication=default;return Missing(now,UiText.C("diagnostics.loadingOrNoScoringVehicles"));}
                byte[]? player=null;int count=0;
                for(int i=0;i<n;i++){var v=new byte[Layout.V_Size];view.ReadArray(Layout.Vehicles+i*Layout.V_Size,v,0,v.Length);if(v[Layout.V_mIsPlayer]==1){count++;player=v;}}
                if(count!=1||player![Layout.V_mControl] is not (0 or 1))return Missing(now,UiText.C("diagnostics.scoringPlayerMissingOrNotUniquelyLocal"));
                var verify=new byte[Layout.V_Size];bool same=false;int verifyCount=0;
                for(int i=0;i<n;i++){view.ReadArray(Layout.Vehicles+i*Layout.V_Size,verify,0,verify.Length);if(verify[Layout.V_mIsPlayer]==1){verifyCount++;if(verify.SequenceEqual(player))same=true;}}
                if(!same||verifyCount!=1||location!=view.ReadByte(SessionOptionsLocation)||et!=view.ReadInt64(Layout.Scoring+Layout.S_mCurrentET)||n!=view.ReadInt32(Layout.Scoring+Layout.S_mNumVehicles))continue;
                var elapsed=Decoder.D(s,Layout.S_mCurrentET);
                if(!events.SequenceEqual(Enumerable.Range(0,16).Select(i=>view.ReadInt32(i*4))))continue;
                bool publication=mappingName=="LMU_Data"&&NativePublication.Poll()&&events.Contains(10);
                if(publication||lastScoringClock is {} old&&elapsed!=old)lastSessionPublication=now;
                lastScoringClock=elapsed;
                if(now-lastSessionPublication>TimeSpan.FromSeconds(2)) {
                    // Not fresh telemetry. Only an already-confirmed binding may be retained by
                    // a separately fresh, context-bound REST navigation proof in SessionIdentityState.
                    var frozen=DecodeSession(s,player,location,now);
                    frozen.Fields["SessionSourceStatus"]=new("Frozen",UiText.C("diagnostics.scoringTooOld"));
                    return frozen with{Error=UiText.C("diagnostics.noCurrentNativeScoringPublication")};
                }
                // No telemetry slot, physics clock, driver profile or electronics are required/read here.
                return DecodeSession(s,player,location,now);
            }
            return Missing(now,UiText.C("diagnostics.inconsistentSessionCopy"));
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){lastScoringClock=null;lastSessionPublication=default;return Missing(now,UiText.C("diagnostics.sessionSharedMemoryUnavailable"));}
    }
    private static Sample SessionMissing(DateTimeOffset now,string reason,string status){var s=Missing(now,reason);s.Fields["SessionSourceStatus"]=new(status,UiText.C("diagnostics.sdkLifecycleEvent"));return s;}
    public static Sample DecodeSession(byte[] scoring,byte[] player,byte location,DateTimeOffset at)
    {
        var fields=Decoder.Decode(scoring,null,null,14200);
        fields["PlayerVehicleFile"]=new(Decoder.Text(player,Layout.V_mVehFilename,32),"LMU_Data.scoring.mVehFilename");
        fields["PlayerSlotId"]=new(Decoder.I(player,Layout.V_mID),"LMU_Data.scoring.mID");
        fields["PlayerControl"]=new((int)(sbyte)player[Layout.V_mControl],"LMU_Data.scoring.mControl");
        fields["OptionsLocation"]=new((int)location,"LMU_Data.generic.appInfo.mOptionsLocation");
        fields["GameRunning"]=new(true,"Windows.Process");return new(at,"LIVE",fields);
    }
}

// Only SYNCHRONIZE handles and zero-timeout waits, following SDK Hold -> Data order.
// No lock-memory writes, SetEvent, ResetEvent, or FILE_MAP_WRITE.
internal static class NativePublication
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeWaitHandle OpenEventW(uint access,bool inherit,string name);
    [DllImport("kernel32.dll")]private static extern uint WaitForSingleObject(SafeWaitHandle handle,uint ms);
    public static bool Poll(){using var hold=OpenEventW(0x00100000,false,"LMU_Data_HoldEvent");using var data=OpenEventW(0x00100000,false,"LMU_Data_DataEvent");return !hold.IsInvalid&&!data.IsInvalid&&WaitForSingleObject(hold,0)==0&&WaitForSingleObject(data,0)==0;}
}
