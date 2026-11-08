using System.Text.Json;
namespace Lmu.Diagnostics;
public sealed partial class LapAnalyzer
{
    // Only a bounded observation association, never a persistent setup history or proof of continuity.
    private sealed class Episode(string id,LapFrame first)
    {
        public string Id {get;}=id;
        public LapFrame Last {get;set;}=first;
        public List<string> Segments {get;}=[];
        public List<LapSetupChange> Changes {get;}=[];
        public bool HadInterruption {get;set;}
    }
    private readonly Dictionary<(string NativeContext,int Number,double Start),Episode> episodes=[];
    private long episodeSequence;
    private LapFrame? lastEpisodeFrame;
    private static string NativeScope(LapFrame frame)=>frame.NativeContext??
        JsonSerializer.Serialize(new object?[]{frame.Context,frame.Identity,frame.FixedSetup,frame.DriverToken});
    private Episode GetEpisode(LapFrame frame,string segment,List<LapEvent> events)
    {
        if(lastEpisodeFrame is { } last && (NativeScope(last)!=NativeScope(frame) ||
            frame.Elapsed<last.Elapsed || frame.LapNumber<last.LapNumber || frame.LapStartET<last.LapStartET))
            episodes.Clear();
        var key=(NativeScope(frame),frame.LapNumber,frame.LapStartET);
        if(!episodes.TryGetValue(key,out var episode))
        {
            if(episodes.Count>=8)episodes.Remove(episodes.Keys.First());
            episode=new($"{runId}:native-observation-{++episodeSequence}",frame);
            episodes.Add(key,episode);
        }
        else
        {
            episode.HadInterruption=true;
            foreach(var (field,before,after) in episode.Last.Setup.Differences(frame.Setup))
            {
                var change=new LapSetupChange(frame.ObservedAt,frame.Elapsed,field,before,after,
                    before is not null && after is not null ? "ObservedAcrossInterruption":"AvailabilityAcrossInterruption",
                    episode.Last.ObservedAt,episode.Last.Elapsed,false);
                episode.Changes.Add(change);
                events.Add(new("setup-change-across-interruption",frame.ObservedAt,segment,new {
                    PreviousSegmentId=episode.Segments.LastOrDefault(),NewSegmentId=segment,
                    NativeLapObservationId=episode.Id,Change=change,ContinuityProven=false }));
            }
        }
        episode.Segments.Add(segment);episode.Last=frame;lastEpisodeFrame=frame;
        return episode;
    }
}
