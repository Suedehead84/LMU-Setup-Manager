using System.Globalization;
using System.Reflection;
using Lmu.Diagnostics;

internal static class AbiEvidenceTests
{
    public static void Run(Action<bool,string> check)
    {
        var rows=File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,"abi-14200.tsv"))
            .Select(line=>line.Split('\t'))
            .ToDictionary(p=>p[0],p=>(Offset:int.Parse(p[1],CultureInfo.InvariantCulture),Size:int.Parse(p[2],CultureInfo.InvariantCulture),Align:int.Parse(p[3],CultureInfo.InvariantCulture),Type:p[4]));
        foreach(var field in typeof(Layout).GetFields(BindingFlags.Public|BindingFlags.Static)
            .Where(f=>f.Name.StartsWith("T_m") || f.Name.StartsWith("S_m") || f.Name.StartsWith("V_m")))
            check(rows.TryGetValue(field.Name,out var row) && row.Offset==(int)field.GetRawConstantValue()!,"official 14200 offset "+field.Name);
        foreach(var (name,size) in new[]{("TelemVect3",24),("TelemWheelV01",260),("TelemInfoV01",1888),("VehicleScoringInfoV01",584),("ScoringInfoV01",548),("ApplicationStateV01",260),("SharedMemoryGeneric",332),("SharedMemoryPathData",1300),("SharedMemoryScoringData",126832),("SharedMemoryTelemetryData",196356),("SharedMemoryObjectOut",324824),("SharedMemoryLayout",324824)})
            check(rows[name].Size==size,"official 14200 sizeof "+name);
        foreach(var (name,size,type) in new[]{
            ("S_mSession",4,"signed"),("S_mCurrentET",8,"float64"),("S_mNumVehicles",4,"signed"),("S_mIsFixedSetup",1,"bool"),
            ("T_mID",4,"signed"),("T_mElapsedTime",8,"float64"),("T_mLapStartET",8,"float64"),
            ("T_mRearBrakeBias",8,"float64"),("T_mLapInvalidated",1,"bool"),("T_mTrackLimitsSteps",1,"unsigned"),
            ("V_mID",4,"signed"),("V_mIsPlayer",1,"bool"),("V_mBestLapTime",8,"float64"),
            ("V_mInPits",1,"bool"),("V_mInGarageStall",1,"bool"),("Generic_gameVersion",4,"signed"),
            ("Telemetry_active",1,"unsigned"),("Telemetry_playerIndex",1,"unsigned"),("Telemetry_playerHasVehicle",1,"bool")})
            check(rows[name].Size==size && rows[name].Type==type,"official 14200 read type "+name);
        foreach(var (name,size,type) in new[]{
            ("T_mLapNumber",4,"signed"),("V_mTotalLaps",2,"signed"),("V_mLapStartET",8,"float64"),
            ("V_mLastLapTime",8,"float64"),("V_mCountLapFlag",1,"unsigned"),("V_mPitState",1,"unsigned"),
            ("V_mControl",1,"signed"),("V_mSteamID",8,"unsigned"),("S_mInRealtime",1,"bool"),("S_mGamePhase",1,"unsigned")})
            check(rows[name].Size==size && rows[name].Type==type,"official lap-analysis type "+name);
        check(rows["V_mDriverName"].Size==32,"official driver-name length");
        foreach(var name in new[]{"TC","TCMax","TCCut","TCCutMax","TCSlip","TCSlipMax","ABS","ABSMax"})
            check(rows["T_m"+name].Size==1 && rows["T_m"+name].Type=="unsigned","official 14200 electronics type "+name);
        check(rows["V_mVehFilename"].Size==32 && rows["T_mVehicleModel"].Size==30 && rows["S_mStartET"].Size==4 && rows["S_mStartET"].Type=="float32","official identity field types and lengths");
        check(rows["T_mVehicleName"].Size==64 && rows["S_mTrackName"].Size==64,"official 14200 string lengths");
        check(rows["Object_scoring"].Offset==Layout.Scoring &&
            rows["Object_scoring"].Offset+rows["Scoring_vehicles"].Offset==Layout.Vehicles &&
            rows["Object_telemetry"].Offset==Layout.Telemetry &&
            rows["Object_telemetry"].Offset+rows["Telemetry_vehicles"].Offset==Layout.TelemStart,"official 14200 container offsets");
        check(rows["Telemetry_active"].Offset==0 && rows["Telemetry_playerIndex"].Offset==1 && rows["Telemetry_playerHasVehicle"].Offset==2,"official 14200 player header offsets");
        check(rows["Generic_gameVersion"].Offset==64,"official 14200 version offset");
        check(rows["Telemetry_vehicles"].Size==104*Layout.T_Size && rows["Scoring_vehicles"].Size==104*Layout.V_Size,"official 14200 array strides and slot counts");
        check(Layout.MappingSize==324820 && rows["SharedMemoryObjectOut"].Size-Layout.MappingSize==4,"payload extent versus C++ trailing alignment padding");
    }
}


