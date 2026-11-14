using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Numerics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
internal static class Parity {
 public static int Run(string models,string wav) {
  var (samples,values,frames)=RealMicFeatures.Extract(wav);
  var e=new DenseTensor<float>(new[]{1,1,frames,32});var s=new DenseTensor<float>(new[]{1,2,frames,96});
  for(int t=0;t<frames;t++){
   for(int k=0;k<32;k++)e[0,0,t,k]=values[t*224+k];
   for(int k=0;k<96;k++){s[0,0,t,k]=values[t*224+32+k];s[0,1,t,k]=values[t*224+128+k];}
  }
  using var o=new SessionOptions{IntraOpNumThreads=1,InterOpNumThreads=1,GraphOptimizationLevel=GraphOptimizationLevel.ORT_DISABLE_ALL};
  using var enc=new InferenceSession(Path.Combine(models,"enc.onnx"),o);
  using var erb=new InferenceSession(Path.Combine(models,"erb_dec.onnx"),o);
  using var df=new InferenceSession(Path.Combine(models,"df_dec.onnx"),o);
  var reference=TemporalEngine.Run(enc,new(){{"feat_erb",e},{"feat_spec",s}});
  var er=TemporalEngine.Run(erb,new[]{"emb","e3","e2","e1","e0"}.ToDictionary(k=>k,k=>reference[k]));
  var dr=TemporalEngine.Run(df,new(){{"emb",reference["emb"]},{"c0",reference["c0"]}});
  using var live=new TemporalEngine(models);
  double encoderMax=0,erbMax=0,dfMax=0,pcmMax=0;
  // Exercise the actual live PCM entry point, including feature state, temporal
  // inference, delayed spectra and synthesis, against original whole-sequence ONNX.
  var featureReference=new RealMicFeatures.FeatureEngine();var synth=new ExperimentalMaskSynth();
  var times=new List<double>();int blocks=(frames+1)/2;
  for(int block=0;block<blocks;block++){
   int start=block*2,n=Math.Min(2,frames-start);var stereo=new float[1920];
   for(int i=0;i<960;i++){int p=start*480+i;float v=p<samples.Length?samples[p]:0;stereo[i*2]=stereo[i*2+1]=v;}
   var clock=System.Diagnostics.Stopwatch.StartNew();live.Process(stereo);times.Add(clock.Elapsed.TotalMilliseconds);
   foreach(var kv in reference){int axis=kv.Key=="emb"||kv.Key=="lsnr"?1:2;encoderMax=Math.Max(encoderMax,TemporalEngine.Difference(TemporalEngine.Slice(kv.Value,start,n,axis),TemporalEngine.Slice(live.LastEncoder[kv.Key],0,n,axis)));}
   foreach(var kv in er){int axis=kv.Value.Dimensions.Length==4?2:1;erbMax=Math.Max(erbMax,TemporalEngine.Difference(TemporalEngine.Slice(kv.Value,start,n,axis),TemporalEngine.Slice(live.LastErb[kv.Key],0,n,axis)));}
   foreach(var kv in dr){int axis=kv.Key=="coefs"?1:kv.Value.Dimensions.Length==4?2:1;dfMax=Math.Max(dfMax,TemporalEngine.Difference(TemporalEngine.Slice(kv.Value,start,n,axis),TemporalEngine.Slice(live.LastDf[kv.Key],0,n,axis)));}
   for(int t=0;t<n;t++){
    var hop=new float[480];int offset=(start+t)*480;Array.Copy(samples,offset,hop,0,Math.Min(480,samples.Length-offset));
    featureReference.Step(hop);synth.PushSpectrum(featureReference.LastSpectrum);
    var mask=new float[32];var coefs=new float[96,10];
    for(int k=0;k<32;k++)mask[k]=er["m"][0,0,start+t,k];
    for(int k=0;k<96;k++)for(int j=0;j<10;j++)coefs[k,j]=dr["coefs"][0,start+t,k,j];
    var pcm=synth.Render(mask,coefs,reference["lsnr"][0,start+t,0]);
    for(int i=0;i<480;i++){double delta=Math.Abs((double)pcm[i]-stereo[(t*480+i)*2]);if(!double.IsFinite(delta))throw new Exception("Nonfinite PCM delta");pcmMax=Math.Max(pcmMax,delta);}
   }
  }
  bool pass=encoderMax<=1e-5&&erbMax<=1e-5&&dfMax<=1e-5&&pcmMax<=1e-5;
  var data=new{milestone="P18G-7T02U-STAGE2C",status=pass?"PASS":"FAIL",frames,blocks,encoderMax,erbMax,dfMax,pcmMax,tolerance=1e-5,meanBlockMs=times.Average(),maxBlockMs=times.Max(),deadlineMisses=times.Count(t=>t>20),liveRouting="NOT_RUN",productionModified=false,scope="Actual live PCM entry point versus original whole-sequence ONNX and existing source-aligned synthesis; not upstream Rust or listening validation"};
  File.WriteAllText(Path.Combine(models,"..","offline-parity.json"),JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));
  Console.WriteLine(JsonSerializer.Serialize(data));
  // Check explicit reset on the same sessions: warmup history cannot enter live audio.
  live.Reset();var first=new float[1920];for(int i=0;i<960;i++)if(i<samples.Length)first[i*2]=first[i*2+1]=samples[i];live.Process(first);
  using var fresh=new TemporalEngine(models);var control=new float[1920];for(int i=0;i<960;i++)if(i<samples.Length)control[i*2]=control[i*2+1]=samples[i];fresh.Process(control);
  if(!first.SequenceEqual(control)||TemporalEngine.Difference(live.LastDf["coefs"],fresh.LastDf["coefs"])!=0)throw new Exception("Reset parity failed");
  Console.WriteLine("TEMPORAL_RESET_PARITY=PASS");return pass?0:2;
 }
}
