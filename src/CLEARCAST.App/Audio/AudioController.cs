using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;
using ClearcastAudioInspection;
using ClearcastAudioRoute;
using System.Linq;
namespace CLEARCAST.App.Audio;
internal record AudioStatus(string State,string Message,long Blocks=0,double MeanMs=0,double MaxMs=0,long Misses=0,long Drops=0,long Underruns=0,int Recoveries=0,double InputPeak=0);
internal sealed class AudioController {
 readonly object gate=new();
 readonly Func<CancellationToken,Action<AudioStatus>,Task> run;
 readonly Func<TimeSpan,CancellationToken,Task> delay;
 CancellationTokenSource cancellation;
 Task current=Task.CompletedTask;
 public event Action<AudioStatus> Changed;
 public AudioStatus Status {get;private set;}=new("Stopped","Choose your microphone and virtual cable, then start.");
 public bool Busy {get{lock(gate)return !current.IsCompleted;}}
 public AudioController(Func<CancellationToken,Action<AudioStatus>,Task> run,Func<TimeSpan,CancellationToken,Task> delay=null){this.run=run;this.delay=delay??Task.Delay;}
 void Publish(AudioStatus status){Status=status;Changed?.Invoke(status);}
 public bool Start(){lock(gate){if(!current.IsCompleted)return false;cancellation?.Dispose();cancellation=new();var token=cancellation.Token;
  current=Task.Run(async()=>{
   int retries=0;
   try{while(true){token.ThrowIfCancellationRequested();Publish(new("Starting","Preparing audio…",Recoveries:retries));
    try{await run(token,s=>Publish(s with{Recoveries=retries}));token.ThrowIfCancellationRequested();Publish(Status with{State="Stopped",Message="Audio stopped.",Recoveries=retries});break;}
    catch(Exception e) when(IsDeviceFailure(e)&&retries<3&&!token.IsCancellationRequested){retries++;Publish(new("Recovering","Device disconnected. Reconnecting to the same selected devices…",Recoveries:retries));await delay(TimeSpan.FromSeconds(2),token);}
   }}catch(OperationCanceledException)when(token.IsCancellationRequested){Publish(Status with{State="Stopped",Message="Audio stopped.",Recoveries=retries});}
   catch(Exception e){Publish(Status with{State="Error",Message="Audio stopped: "+e.Message+" Refresh devices and start again.",Recoveries=retries});}
  });return true;
 }}
 public async Task StopAsync(){Task pending;lock(gate){cancellation?.Cancel();pending=current;}await pending.ConfigureAwait(false);}
 internal static bool IsDeviceFailure(Exception e){for(Exception x=e;x!=null;x=x.InnerException)if(x is COMException&&(x.HResult==unchecked((int)0x88890004)||x.HResult==unchecked((int)0x88890026)))return true;return false;}
}
internal static class LiveAudioBackend {
 public static Task Run(string models,string inputId,string outputId,CancellationToken token,Action<AudioStatus> changed)=>Task.Run(()=>{
  token.ThrowIfCancellationRequested();
  var input=Inspector.CaptureDevices().SingleOrDefault(x=>x.State=="Active"&&x.Id==inputId);
  var output=Inspector.RenderDevices().SingleOrDefault(x=>x.State=="Active"&&x.Id==outputId&&x.Name.Contains("CABLE Input",StringComparison.OrdinalIgnoreCase));
  if(input==null||output==null)throw new COMException("Selected endpoint is unavailable",unchecked((int)0x88890004));
  using var engine=new TemporalEngine(models);
  for(int i=0;i<16;i++){token.ThrowIfCancellationRequested();engine.Process(new float[1920]);}engine.Reset();
  double peak=0;
  Action<RouteResult> update=result=>{
   changed(new("Running","Noise reduction is on. Select CABLE Output in your meeting app.",result.ProcessedBlocks,result.ProcessedBlocks>0?result.SumProcessingUs/(double)result.ProcessedBlocks/1000:0,result.MaxBlockProcessingUs/1000.0,result.ProcessingDeadlineMisses,result.CaptureDrops,result.RenderUnderruns,InputPeak:peak));
  };
  var final=Router.Run(inputId,outputId,output.Name,int.MaxValue,block=>{peak=block.Select(v=>Math.Abs((double)v)).Max();engine.Process(block);},token,update);
  update(final);
  System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"last-audio-session.json"),System.Text.Json.JsonSerializer.Serialize(new{final.ProcessedBlocks,final.CapturedFrames,final.WrittenFrames,final.StagedRemainingFrames,final.MaxBlockProcessingUs,final.ProcessingDeadlineMisses,final.CaptureDrops,final.RenderUnderruns,final.EnqueuedBlocks,final.CompletedBlocks,final.OutputConsumedBlocks,final.FifoRemainingFrames,final.InputQueueOverflowBlocks,final.OutputQueueOverflowBlocks},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
  token.ThrowIfCancellationRequested();
 },token);
}
