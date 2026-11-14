using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
internal sealed class TemporalEngine : IDisposable {
 const string alias="clearcast_encoder_gru_x_external", traceName="clearcast_encoder_gru_x_trace";
 readonly InferenceSession traceSession,injected,erbStateSession,dfStateSession;
 readonly DenseTensor<float> state0=new(new[]{1,1,256});
 DenseTensor<float> prev;
 DenseTensor<float>[] erbPrev,dfPrev;
 readonly Dictionary<int,DenseTensor<float>[]> localDfStates=new();
 readonly Dictionary<int,DenseTensor<float>> recentEmb=new(),recentC0=new();
 Dictionary<string,DenseTensor<float>> pastFeatures;
 int start;
 RealMicFeatures.FeatureEngine features=new();
 public ExperimentalMaskSynth Synth {get;private set;}=new();
 public Dictionary<string,DenseTensor<float>> LastEncoder,LastErb,LastDf;
 public TemporalEngine(string models) {
  using var o=new SessionOptions{IntraOpNumThreads=1,InterOpNumThreads=1,GraphOptimizationLevel=GraphOptimizationLevel.ORT_DISABLE_ALL};
  traceSession=new(Path.Combine(models,"enc.gru-x-trace.onnx"),o);
  injected=new(Path.Combine(models,"enc.gru-x-injected.onnx"),o);
  erbStateSession=new(Path.Combine(models,"erb_dec.state.onnx"),o);
  dfStateSession=new(Path.Combine(models,"df_dec.state.onnx"),o);
  Reset();
 }
 public void Reset(){start=0;prev=state0;erbPrev=new[]{Zero(),Zero()};dfPrev=new[]{Zero(),Zero()};localDfStates.Clear();localDfStates[0]=dfPrev;recentEmb.Clear();recentC0.Clear();pastFeatures=null;features=new();Synth=new();LastEncoder=null;LastErb=null;LastDf=null;}
 static DenseTensor<float> Zero()=>new(new[]{1,1,256});
 public void Dispose(){traceSession.Dispose();injected.Dispose();erbStateSession.Dispose();dfStateSession.Dispose();}
 internal static DenseTensor<float> Copy(Tensor<float> t){var x=new DenseTensor<float>(t.Dimensions.ToArray());int i=0;foreach(float v in t)x.Buffer.Span[i++]=v;return x;}
 internal static Dictionary<string,DenseTensor<float>> Run(InferenceSession session,Dictionary<string,DenseTensor<float>> args){using var result=session.Run(args.Select(k=>NamedOnnxValue.CreateFromTensor(k.Key,k.Value)).ToList());return result.ToDictionary(k=>k.Name,k=>Copy(k.AsTensor<float>()));}
 internal static DenseTensor<float> Slice(DenseTensor<float> x,int start,int count,int axis){var d=x.Dimensions.ToArray();int old=d[axis];if(start+count>old)throw new Exception("Slice outside bounds");d[axis]=count;var y=new DenseTensor<float>(d);int inner=1,outer=1;for(int i=axis+1;i<d.Length;i++)inner*=d[i];for(int i=0;i<axis;i++)outer*=d[i];for(int o=0;o<outer;o++)for(int t=0;t<count;t++)for(int k=0;k<inner;k++)y.Buffer.Span[(o*count+t)*inner+k]=x.Buffer.Span[(o*old+start+t)*inner+k];return y;}
 internal static DenseTensor<float> JoinTime(DenseTensor<float> a,DenseTensor<float> b,int axis){
  var da=a.Dimensions.ToArray();var db=b.Dimensions.ToArray();
  if(da.Length!=db.Length)throw new Exception("Join rank mismatch");
  for(int i=0;i<da.Length;i++)if(i!=axis&&da[i]!=db[i])throw new Exception("Join shape mismatch");
  int left=da[axis],right=db[axis],inner=1,outer=1;
  for(int i=axis+1;i<da.Length;i++)inner*=da[i];
  for(int i=0;i<axis;i++)outer*=da[i];
  da[axis]+=right;var output=new DenseTensor<float>(da);
  for(int o=0;o<outer;o++){
   a.Buffer.Span.Slice(o*left*inner,left*inner).CopyTo(output.Buffer.Span.Slice(o*(left+right)*inner,left*inner));
   b.Buffer.Span.Slice(o*right*inner,right*inner).CopyTo(output.Buffer.Span.Slice((o*(left+right)+left)*inner,right*inner));
  }
  return output;
 }
 internal static double Difference(DenseTensor<float> a,DenseTensor<float> b){
  if(!a.Dimensions.SequenceEqual(b.Dimensions))throw new Exception("Shape mismatch: "+string.Join(',',a.Dimensions.ToArray())+" vs "+string.Join(',',b.Dimensions.ToArray()));
  double max=0;for(int i=0;i<a.Length;i++){double x=a.Buffer.Span[i],y=b.Buffer.Span[i];if(!double.IsFinite(x)||!double.IsFinite(y))throw new Exception("Nonfinite tensor");max=Math.Max(max,Math.Abs(x-y));}return max;
 }

 public void Process(float[] stereo) {
  if(stereo.Length!=1920)throw new ArgumentException("Expected 960 stereo frames");
  var e=new DenseTensor<float>(new[]{1,1,2,32});var s=new DenseTensor<float>(new[]{1,2,2,96});
  var spectra=new Complex[2][];
  for(int t=0;t<2;t++){
   var mono=new float[480];for(int i=0;i<480;i++)mono[i]=.5f*(stereo[(t*480+i)*2]+stereo[(t*480+i)*2+1]);
   var f=features.Step(mono);spectra[t]=(Complex[])features.LastSpectrum.Clone();
   for(int k=0;k<32;k++)e[0,0,t,k]=f[k];
   for(int k=0;k<96;k++){s[0,0,t,k]=f[32+k];s[0,1,t,k]=f[128+k];}
  }
  ProcessFeatures(new(){{"feat_erb",e},{"feat_spec",s}});
  for(int t=0;t<2;t++){
   var mask=new float[32];var coefs=new float[96,10];
   for(int k=0;k<32;k++)mask[k]=LastErb["m"][0,0,t,k];
   for(int k=0;k<96;k++)for(int j=0;j<10;j++)coefs[k,j]=LastDf["coefs"][0,t,k,j];
   Synth.PushSpectrum(spectra[t]);var pcm=Synth.Render(mask,coefs,LastEncoder["lsnr"][0,t,0]);
   for(int i=0;i<480;i++){if(!float.IsFinite(pcm[i]))throw new InvalidDataException("Nonfinite PCM");stereo[(t*480+i)*2]=pcm[i];stereo[(t*480+i)*2+1]=pcm[i];}
  }
 }
 public void ProcessFeatures(Dictionary<string,DenseTensor<float>> feat) {
  int n=feat["feat_erb"].Dimensions[2];if(n<1||n>2)throw new ArgumentException("One or two fresh frames required");
  int history=Math.Min(2,start);
    var traceArgs=new Dictionary<string,DenseTensor<float>>();
    foreach(var kv in feat)traceArgs[kv.Key]=pastFeatures==null?kv.Value:JoinTime(pastFeatures[kv.Key],kv.Value,2);
    traceArgs["clearcast_gru_state_in_0"]=state0;
    var overlapTrace=Run(traceSession,traceArgs);
    var boundedX=Slice(overlapTrace[traceName],history,n,0);
    var injectedArgs=new Dictionary<string,DenseTensor<float>>();
    foreach(var kv in feat)injectedArgs[kv.Key]=kv.Value;
    injectedArgs["clearcast_gru_state_in_0"]=prev;
    injectedArgs[alias]=boundedX;
    var recurrentResult=Run(injected,injectedArgs);
    prev=recurrentResult["clearcast_gru_state_out_0"];
    var stitched=new Dictionary<string,DenseTensor<float>>();
    foreach(var name in new[]{"e0","e1","e2","e3","c0","emb","lsnr"}){
     int axis=(name=="emb"||name=="lsnr")?1:2;
     var actual=(name=="emb"||name=="lsnr")?recurrentResult[name]:Slice(overlapTrace[name],history,n,axis);
     stitched[name]=actual;
    }
    var erbInputs=new Dictionary<string,DenseTensor<float>>();
    foreach(var name in new[]{"emb","e3","e2","e1","e0"})erbInputs[name]=stitched[name];
    var dfInputs=new Dictionary<string,DenseTensor<float>>{{"emb",stitched["emb"]},{"c0",stitched["c0"]}};
    for(int i=0;i<2;i++){erbInputs["clearcast_gru_state_in_"+i]=erbPrev[i];dfInputs["clearcast_gru_state_in_"+i]=dfPrev[i];}
    var erbChunk=Run(erbStateSession,erbInputs);
    // Bounded streaming DF: maintain three actual past feature frames and
    // state checkpoints for those frames. No full-sequence oracle inputs.
    int dfHistory=Math.Min(3,start);
    int dfBegin=start-dfHistory;
    var dfWindowInputs=new Dictionary<string,DenseTensor<float>>();
    var dfEmb=stitched["emb"];
    var dfC0=stitched["c0"];
    for(int past=start-1;past>=dfBegin;past--){
     if(!recentEmb.ContainsKey(past)||!recentC0.ContainsKey(past))throw new Exception("Missing bounded DF feature history "+past);
     // Prepend newest-to-oldest so final window is chronological.
     dfEmb=JoinTime(recentEmb[past],dfEmb,1);
     dfC0=JoinTime(recentC0[past],dfC0,2);
    }
    if(!localDfStates.ContainsKey(dfBegin))throw new Exception("Missing bounded DF recurrent checkpoint "+dfBegin);
    dfWindowInputs["emb"]=dfEmb;
    dfWindowInputs["c0"]=dfC0;
    for(int i=0;i<2;i++)dfWindowInputs["clearcast_gru_state_in_"+i]=localDfStates[dfBegin][i];
    var dfWindow=Run(dfStateSession,dfWindowInputs);
    var dfChunk=new Dictionary<string,DenseTensor<float>>{
     {"coefs",Slice(dfWindow["coefs"],dfHistory,n,1)},
     {"235",Slice(dfWindow["235"],dfHistory,n,1)},
     {"clearcast_gru_state_out_0",dfWindow["clearcast_gru_state_out_0"]},
     {"clearcast_gru_state_out_1",dfWindow["clearcast_gru_state_out_1"]}
    };
    // Incremental state checkpoint updates: only last three frames need
    // independent checkpoints; derive them from fresh features and state at
    // chunk start. Prefix runs are bounded by chunk size, not recording length.
    for(int j=Math.Max(1,n-3);j<=n;j++){
     var prefixArgs=new Dictionary<string,DenseTensor<float>>{
      {"emb",Slice(stitched["emb"],0,j,1)},
      {"c0",Slice(stitched["c0"],0,j,2)},
      {"clearcast_gru_state_in_0",dfPrev[0]},
      {"clearcast_gru_state_in_1",dfPrev[1]}
     };
     var checkpoint=Run(dfStateSession,prefixArgs);
     localDfStates[start+j]=new[]{checkpoint["clearcast_gru_state_out_0"],checkpoint["clearcast_gru_state_out_1"]};
    }
    // For a chunk smaller than 3 frames, preserve earlier checkpoints.
    dfPrev=localDfStates[start+n];
    for(int j=0;j<n;j++){
     int frame=start+j;
     if(frame>=start+n-3){
      recentEmb[frame]=Slice(stitched["emb"],j,1,1);
      recentC0[frame]=Slice(stitched["c0"],j,1,2);
     }
    }
    foreach(int old in recentEmb.Keys.Where(k=>k<start+n-3).ToArray()){recentEmb.Remove(old);recentC0.Remove(old);}
    foreach(int old in localDfStates.Keys.Where(k=>k<start+n-3).ToArray())localDfStates.Remove(old);
    for(int i=0;i<2;i++)erbPrev[i]=erbChunk["clearcast_gru_state_out_"+i];

  pastFeatures=feat.ToDictionary(k=>k.Key,k=>{
   var all=pastFeatures==null?k.Value:JoinTime(pastFeatures[k.Key],k.Value,2);
   int len=all.Dimensions[2];return Slice(all,Math.Max(0,len-2),Math.Min(2,len),2);
  });
  start+=n;
  if(recentEmb.Count>3||localDfStates.Count>4)throw new Exception("Unbounded temporal cache");
  LastEncoder=stitched;LastErb=erbChunk;LastDf=dfChunk;
  foreach(var tensor in stitched.Values.Concat(erbChunk.Values).Concat(dfChunk.Values))foreach(float v in tensor)if(!float.IsFinite(v))throw new InvalidDataException("Nonfinite temporal output");
 }
}
