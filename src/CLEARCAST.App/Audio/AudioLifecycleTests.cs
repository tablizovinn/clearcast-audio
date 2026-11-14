using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
namespace CLEARCAST.App.Audio;
internal static class AudioLifecycleTests {
 static async Task Until(Func<bool> ready){var time=Stopwatch.StartNew();while(!ready()){if(time.Elapsed.TotalSeconds>10)throw new Exception("Lifecycle wait timed out");await Task.Delay(10);}}
 static void Require(bool value,string message){if(!value)throw new Exception(message);}
 public static async Task Run(string report){
  var results=new List<string>();int calls=0;
  var basic=new AudioController(async(token,publish)=>{Interlocked.Increment(ref calls);publish(new("Running","test",Blocks:42));await Task.Delay(Timeout.Infinite,token);});
  Require(basic.Start(),"Start rejected");Require(!basic.Start(),"Double start accepted");await Until(()=>basic.Status.State=="Running");await basic.StopAsync();Require(!basic.Busy&&basic.Status.State=="Stopped","Stop failed");
  Require(basic.Status.Blocks==42,"Stop cleared metrics");Require(basic.Start(),"Restart rejected");await Until(()=>calls==2);await basic.StopAsync();results.Add("Start, duplicate start rejection, stop, restart, retained stop metrics: PASS");
  int attempts=0;var recovery=new AudioController(async(token,publish)=>{if(Interlocked.Increment(ref attempts)==1)throw new COMException("test unplug",unchecked((int)0x88890004));publish(new("Running","test"));await Task.Delay(Timeout.Infinite,token);},(_,token)=>Task.Delay(1,token));
  recovery.Start();await Until(()=>recovery.Status.State=="Running");Require(attempts==2&&recovery.Status.Recoveries==1,"Recovery failed");await recovery.StopAsync();results.Add("Simulated endpoint invalidation followed by recovery: PASS");
  var exhausted=new AudioController((token,publish)=>throw new COMException("test unplug",unchecked((int)0x88890004)),(_,token)=>Task.Delay(1,token));exhausted.Start();await Until(()=>!exhausted.Busy);Require(exhausted.Status.State=="Error"&&exhausted.Status.Recoveries==3,"Retries not bounded");results.Add("Recovery exhaustion after three retries: PASS");
  var wait=new AudioController((token,publish)=>throw new COMException("test unplug",unchecked((int)0x88890004)));wait.Start();await Until(()=>wait.Status.State=="Recovering");await wait.StopAsync();Require(wait.Status.State=="Stopped","Recovery cancellation failed");results.Add("Stop while reconnecting: PASS");
  var fault=new AudioController((token,publish)=>throw new InvalidDataException("bad model"));fault.Start();await Until(()=>!fault.Busy);Require(fault.Status.State=="Error"&&fault.Status.Recoveries==0,"Model failure retried");results.Add("Non-device error stops without retry: PASS");
  File.WriteAllText(report,JsonSerializer.Serialize(new{status="PASS",results,physicalDeviceUnplug="NOT_TESTED"},new JsonSerializerOptions{WriteIndented=true}));
 }
}
