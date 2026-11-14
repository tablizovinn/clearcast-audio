
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
namespace ClearcastAudioInspection {
    public enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    public enum ERole { Console = 0, Multimedia = 1, Communications = 2 }
    [Flags] public enum DeviceState : uint { Active = 1, Disabled = 2, NotPresent = 4, Unplugged = 8, All = 15 }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorClass { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, DeviceState stateMask, out IMMDeviceCollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, uint clsctx, IntPtr activationParams, out IntPtr interfacePointer);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out DeviceState state);
    }
    [StructLayout(LayoutKind.Sequential)] public struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Explicit)] public struct PROPVARIANT {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
        [FieldOffset(8)] public int intValue;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient {
        [PreserveSig] int Initialize(int shareMode, uint flags, long bufferDuration, long periodicity, IntPtr format, ref Guid sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr requested, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, out IntPtr service);
    }
    [StructLayout(LayoutKind.Sequential, Pack = 2)] public struct WaveFormatEx {
        public ushort FormatTag; public ushort Channels; public uint SamplesPerSec;
        public uint AvgBytesPerSec; public ushort BlockAlign; public ushort BitsPerSample; public ushort ExtraSize;
    }
    public class DeviceInfo {
        public string Id { get; set; }
        public string Name { get; set; }
        public string State { get; set; }
        public bool DefaultConsole { get; set; }
        public bool DefaultMultimedia { get; set; }
        public bool DefaultCommunications { get; set; }
        public int MixSampleRate { get; set; }
        public int MixChannels { get; set; }
        public int MixBitsPerSample { get; set; }
        public int MixFormatTag { get; set; }
        public double DefaultPeriodMs { get; set; }
        public double MinimumPeriodMs { get; set; }
        public string Diagnostic { get; set; }
    }
    public static class Inspector {
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT value);
        private static string DefaultId(IMMDeviceEnumerator e, ERole role) {
            IMMDevice d = null;
            try { if (e.GetDefaultAudioEndpoint(EDataFlow.Capture, role, out d) != 0 || d == null) return null;
                string id; return d.GetId(out id) == 0 ? id : null;
            } finally { if (d != null) Marshal.ReleaseComObject(d); }
        }
        public static DeviceInfo[] CaptureDevices() { return Devices(EDataFlow.Capture); }
        public static DeviceInfo[] RenderDevices() { return Devices(EDataFlow.Render); }
        public static DeviceInfo[] Devices(EDataFlow flow) {
            var result = new List<DeviceInfo>();
            IMMDeviceEnumerator e = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            IMMDeviceCollection c = null;
            try {
                string dc = DefaultId(e, ERole.Console), dm = DefaultId(e, ERole.Multimedia), dcom = DefaultId(e, ERole.Communications);
                int hr = e.EnumAudioEndpoints(flow, DeviceState.All, out c);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                uint count; hr = c.GetCount(out count); if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                for (uint i = 0; i < count; i++) {
                    IMMDevice d = null; IPropertyStore store = null; IntPtr audioPtr = IntPtr.Zero; IAudioClient client = null;
                    var info = new DeviceInfo();
                    try {
                        hr = c.Item(i, out d); if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                        string id; d.GetId(out id); info.Id = id;
                        DeviceState state; d.GetState(out state); info.State = state.ToString();
                        info.DefaultConsole = String.Equals(id, dc, StringComparison.OrdinalIgnoreCase);
                        info.DefaultMultimedia = String.Equals(id, dm, StringComparison.OrdinalIgnoreCase);
                        info.DefaultCommunications = String.Equals(id, dcom, StringComparison.OrdinalIgnoreCase);
                        hr = d.OpenPropertyStore(0, out store);
                        if (hr == 0 && store != null) {
                            PROPERTYKEY key = new PROPERTYKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
                            PROPVARIANT value;
                            if (store.GetValue(ref key, out value) == 0) {
                                try { if (value.vt == 31 || value.vt == 30) info.Name = Marshal.PtrToStringUni(value.pointerValue); }
                                finally { PropVariantClear(ref value); }
                            }
                        }
                        if (String.IsNullOrEmpty(info.Name)) info.Name = "(unnamed capture endpoint)";
                        if (state == DeviceState.Active) {
                            Guid iid = typeof(IAudioClient).GUID;
                            hr = d.Activate(ref iid, 23, IntPtr.Zero, out audioPtr);
                            if (hr == 0 && audioPtr != IntPtr.Zero) {
                                client = (IAudioClient)Marshal.GetObjectForIUnknown(audioPtr);
                                IntPtr format;
                                if (client.GetMixFormat(out format) == 0 && format != IntPtr.Zero) {
                                    try { WaveFormatEx w = (WaveFormatEx)Marshal.PtrToStructure(format, typeof(WaveFormatEx));
                                        info.MixSampleRate = (int)w.SamplesPerSec; info.MixChannels = w.Channels;
                                        info.MixBitsPerSample = w.BitsPerSample; info.MixFormatTag = w.FormatTag;
                                    } finally { Marshal.FreeCoTaskMem(format); }
                                }
                                long defaultP, minP;
                                if (client.GetDevicePeriod(out defaultP, out minP) == 0) {
                                    info.DefaultPeriodMs = defaultP / 10000.0;
                                    info.MinimumPeriodMs = minP / 10000.0;
                                }
                            } else { info.Diagnostic = "IAudioClient activation HRESULT 0x" + hr.ToString("X8"); }
                        }
                    } catch (Exception ex) { info.Diagnostic = ex.GetType().Name + ": " + ex.Message; }
                    finally {
                        if (client != null) Marshal.ReleaseComObject(client);
                        if (audioPtr != IntPtr.Zero) Marshal.Release(audioPtr);
                        if (store != null) Marshal.ReleaseComObject(store);
                        if (d != null) Marshal.ReleaseComObject(d);
                    }
                    result.Add(info);
                }
            } finally { if (c != null) Marshal.ReleaseComObject(c); Marshal.ReleaseComObject(e); }
            return result.ToArray();
        }
    }
}

namespace ClearcastAudioCapture {
 using System;
 using System.IO;
 using System.Threading;
 using System.Runtime.InteropServices;
 using ClearcastAudioInspection;
 [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 public interface IAudioCaptureClient {
  [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
  [PreserveSig] int ReleaseBuffer(uint frames);
  [PreserveSig] int GetNextPacketSize(out uint frames);
 }
 public class CaptureResult {
  public string DeviceName {get;set;}
  public string DeviceId {get;set;}
  public string Format {get;set;}
  public long CapturedFrames {get;set;}
  public long EncoderCalls {get;set;}
  public long EncoderErrors {get;set;}
  public long ProcessedBlocks {get;set;}
  public long ProcessedFrames {get;set;}
  public long MaxBlockProcessingUs {get;set;}
  public long ProcessingDeadlineMisses {get;set;}
  public long SilentFrames {get;set;}
  public long Packets {get;set;}
  public long Full20msBlocks {get;set;}
  public int BytesPer20msBlock {get;set;}
  public long MaxPendingBytes {get;set;}
  public long MaxPacketBytes {get;set;}
  public long CaptureGapsOver40ms {get;set;}
  public double MaxPacketIntervalMs {get;set;}
  public long Bytes {get;set;}
  public double ElapsedSeconds {get;set;}
  public string WavePath {get;set;}
 }
 public static class Runner {
  static void Check(int hr, string stage) { if(hr<0) throw new COMException(stage+" HRESULT=0x"+hr.ToString("X8"),hr); }
  public static CaptureResult Record(string deviceId, string deviceName, string outputPath, int seconds) {
   IMMDeviceEnumerator enumerator=null; IMMDevice device=null; IAudioClient client=null; IAudioCaptureClient capture=null;
   IntPtr clientPtr=IntPtr.Zero, capturePtr=IntPtr.Zero, mixPtr=IntPtr.Zero;
   try {
    enumerator=(IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
    Check(enumerator.GetDevice(deviceId,out device),"GetDevice");
    Guid iid=typeof(IAudioClient).GUID;
    Check(device.Activate(ref iid,23,IntPtr.Zero,out clientPtr),"Activate audio client");
    client=(IAudioClient)Marshal.GetObjectForIUnknown(clientPtr);
    Check(client.GetMixFormat(out mixPtr),"GetMixFormat");
    WaveFormatEx fmt=(WaveFormatEx)Marshal.PtrToStructure(mixPtr,typeof(WaveFormatEx));
    if(fmt.SamplesPerSec<8000 || fmt.SamplesPerSec>192000 || fmt.Channels<1 || fmt.Channels>8 || fmt.BlockAlign<1 || fmt.BlockAlign>64) throw new InvalidOperationException("Invalid device mix format");
    // Copy complete WAVEFORMATEX/WAVEFORMATEXTENSIBLE header including cbSize.
    int formatBytes=18+fmt.ExtraSize;
    byte[] formatRaw=new byte[formatBytes]; Marshal.Copy(mixPtr,formatRaw,0,formatBytes);
    Guid session=Guid.Empty;
    Check(client.Initialize(0,0,1000000,0,mixPtr,ref session),"IAudioClient.Initialize shared mode");
    uint bufferFrames; Check(client.GetBufferSize(out bufferFrames),"GetBufferSize");
    Guid captureIid=typeof(IAudioCaptureClient).GUID;
    Check(client.GetService(ref captureIid,out capturePtr),"GetService IAudioCaptureClient");
    capture=(IAudioCaptureClient)Marshal.GetObjectForIUnknown(capturePtr);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
    var result=new CaptureResult {DeviceName=deviceName,DeviceId=deviceId,WavePath=outputPath,Format=fmt.SamplesPerSec+"Hz "+fmt.Channels+"ch "+fmt.BitsPerSample+"bit tag="+fmt.FormatTag+" block="+fmt.BlockAlign};
    using(var file=new FileStream(outputPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
    using(var writer=new BinaryWriter(file)) {
     // RIFF WAVE: use original mix format in fmt chunk, including extensible subformat.
     writer.Write(new char[]{'R','I','F','F'}); writer.Write((uint)0);
     writer.Write(new char[]{'W','A','V','E'});
     writer.Write(new char[]{'f','m','t',' '}); writer.Write((uint)formatBytes); writer.Write(formatRaw);
     if((formatBytes&1)!=0) writer.Write((byte)0);
     writer.Write(new char[]{'d','a','t','a'}); long dataSizePos=file.Position; writer.Write((uint)0);
     long dataStart=file.Position;
     int chunkBytes = checked((int)(fmt.SamplesPerSec / 50 * fmt.BlockAlign));
     if(fmt.SamplesPerSec % 50 != 0) throw new InvalidOperationException("Mix rate not divisible by 50: " + fmt.SamplesPerSec);
     result.BytesPer20msBlock=chunkBytes;
     byte[] chunk = new byte[chunkBytes]; int pending=0;
     var packetTimer=System.Diagnostics.Stopwatch.StartNew(); long lastPacketTicks=-1;
     Check(client.Start(),"Start");
     var timer=System.Diagnostics.Stopwatch.StartNew();
     try {
      while(timer.Elapsed.TotalSeconds<seconds) {
       Thread.Sleep(5);
       uint packet; Check(capture.GetNextPacketSize(out packet),"GetNextPacketSize");
       while(packet>0) {
        IntPtr data; uint frames,flags; ulong dp,qp;
        Check(capture.GetBuffer(out data,out frames,out flags,out dp,out qp),"GetBuffer");
        try {
         int byteCount=checked((int)(frames*fmt.BlockAlign));
         byte[] buffer=new byte[byteCount];
         if((flags&2)!=0) result.SilentFrames+=frames;
         else if(byteCount>0) Marshal.Copy(data,buffer,0,byteCount);
         writer.Write(buffer);
         if(byteCount>result.MaxPacketBytes) result.MaxPacketBytes=byteCount;
         long nowTicks=packetTimer.ElapsedTicks;
         if(lastPacketTicks>=0) {double dt=(nowTicks-lastPacketTicks)*1000.0/System.Diagnostics.Stopwatch.Frequency;
          if(dt>result.MaxPacketIntervalMs) result.MaxPacketIntervalMs=dt;
          if(dt>40.0) result.CaptureGapsOver40ms++;
         }
         lastPacketTicks=nowTicks;
         int off=0;
         while(off<byteCount) {int take=Math.Min(chunkBytes-pending,byteCount-off);
          Buffer.BlockCopy(buffer,off,chunk,pending,take); pending+=take;off+=take;
          if(pending>result.MaxPendingBytes) result.MaxPendingBytes=pending;
          if(pending==chunkBytes) {result.Full20msBlocks++;pending=0;}
         }
         result.Bytes+=byteCount; result.CapturedFrames+=frames; result.Packets++;
        } finally { Check(capture.ReleaseBuffer(frames),"ReleaseBuffer"); }
        Check(capture.GetNextPacketSize(out packet),"GetNextPacketSize");
       }
      }
     } finally { client.Stop(); timer.Stop(); result.ElapsedSeconds=timer.Elapsed.TotalSeconds; }
     if(result.Bytes>uint.MaxValue-1024) throw new InvalidOperationException("WAV too large");
     file.Position=dataSizePos; writer.Write((uint)result.Bytes);
     file.Position=4; writer.Write((uint)(file.Length-8));
    }
    return result;
   } finally {
    if(capture!=null) Marshal.ReleaseComObject(capture);
    if(capturePtr!=IntPtr.Zero) Marshal.Release(capturePtr);
    if(client!=null) Marshal.ReleaseComObject(client);
    if(clientPtr!=IntPtr.Zero) Marshal.Release(clientPtr);
    if(mixPtr!=IntPtr.Zero) Marshal.FreeCoTaskMem(mixPtr);
    if(device!=null) Marshal.ReleaseComObject(device);
    if(enumerator!=null) Marshal.ReleaseComObject(enumerator);
   }
  }
 }
}


namespace ClearcastAudioRoute {
 using System;
 using System.Threading;
 using System.Runtime.InteropServices;
 using ClearcastAudioInspection;
 using ClearcastAudioCapture;
 [ComImport,Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 public interface IAudioRenderClient {
  [PreserveSig] int GetBuffer(uint frames,out IntPtr data);
  [PreserveSig] int ReleaseBuffer(uint frames,uint flags);
 }
 public class RouteResult {
  public long ProcessedBlocks {get;set;}
  public long EncoderCalls {get;set;}
  public long ProcessedFrames {get;set;}
  public long MaxBlockProcessingUs {get;set;}
  public long ProcessingDeadlineMisses {get;set;}
  public long SumProcessingUs {get;set;}
  public long[] ProcessingUsSamples {get;set;} = new long[8192];
  public int ProcessingUsCount {get;set;}
  public int MaxInputQueueBlocks {get;set;}
  public int MaxOutputQueueBlocks {get;set;}
  public long InputQueueOverflowBlocks {get;set;}
  public long OutputQueueOverflowBlocks {get;set;}
  public long EnqueuedBlocks {get;set;}
  public long DequeuedBlocks {get;set;}
  public long CompletedBlocks {get;set;}
  public long OutputConsumedBlocks {get;set;}
  public int InputQueueRemaining {get;set;}
  public int OutputQueueRemaining {get;set;}
  public long FifoRemainingFrames {get;set;}
  public int StagedRemainingFrames {get;set;}
  public long CapturedFrames {get;set;}
  public long WrittenFrames {get;set;}
  public long SilentFrames {get;set;}
  public long CapturePackets {get;set;}
  public long RenderUnderruns {get;set;}
  public long CaptureDrops {get;set;}
  public double MaxCaptureIntervalMs {get;set;}
  public double ElapsedSeconds {get;set;}
  public int CaptureRate {get;set;}
  public int RenderRate {get;set;}
  public int CaptureChannels {get;set;}
  public int RenderChannels {get;set;}
  public string OutputName {get;set;}
 }
 public static class Router {
  static void Check(int hr,string stage){if(hr<0)throw new COMException(stage+" 0x"+hr.ToString("X8"),hr);}
  static bool FloatStereo(WaveFormatEx f,IntPtr raw){
   if(f.SamplesPerSec!=48000||f.Channels!=2||f.BitsPerSample!=32||f.BlockAlign!=8)return false;
   if(f.FormatTag==3)return true;
   if(f.FormatTag==65534&&f.ExtraSize>=22){
    // WAVEFORMATEXTENSIBLE SubFormat starts at offset 24 (format base 18 + samples 2 + mask 4)
    byte[] g=new byte[16];Marshal.Copy(IntPtr.Add(raw,24),g,0,16);
    return new Guid(g)==new Guid("00000003-0000-0010-8000-00AA00389B71");
   }
   return false;
  }
  public static RouteResult Run(string captureId,string renderId,string outputName,int seconds,Action<float[]> onBlock,CancellationToken cancellation=default,Action<RouteResult> progress=null){
   IMMDeviceEnumerator e=null;IMMDevice cd=null,rd=null;IAudioClient cc=null,rc=null;
   IAudioCaptureClient cap=null;IAudioRenderClient render=null;
   IntPtr cp=IntPtr.Zero,rp=IntPtr.Zero,fp=IntPtr.Zero,gp=IntPtr.Zero,csp=IntPtr.Zero,rsp=IntPtr.Zero;
   bool startedC=false,startedR=false;
   object queueLock=new object();bool stopWorker=false;Thread worker=null;
   try {
    e=(IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
    Check(e.GetDevice(captureId,out cd),"Capture GetDevice");Check(e.GetDevice(renderId,out rd),"Render GetDevice");
    Guid iid=typeof(IAudioClient).GUID;
    Check(cd.Activate(ref iid,23,IntPtr.Zero,out cp),"Capture Activate");cc=(IAudioClient)Marshal.GetObjectForIUnknown(cp);
    Check(rd.Activate(ref iid,23,IntPtr.Zero,out rp),"Render Activate");rc=(IAudioClient)Marshal.GetObjectForIUnknown(rp);
    Check(cc.GetMixFormat(out fp),"Capture GetMixFormat");Check(rc.GetMixFormat(out gp),"Render GetMixFormat");
    WaveFormatEx cf=(WaveFormatEx)Marshal.PtrToStructure(fp,typeof(WaveFormatEx));
    WaveFormatEx rf=(WaveFormatEx)Marshal.PtrToStructure(gp,typeof(WaveFormatEx));
    if(!FloatStereo(cf,fp)||!FloatStereo(rf,gp))throw new InvalidOperationException("Both endpoints must use 48kHz 2-channel 32-bit IEEE float shared-mode mix format. Capture="+cf.SamplesPerSec+"/"+cf.Channels+"/"+cf.FormatTag+" render="+rf.SamplesPerSec+"/"+rf.Channels+"/"+rf.FormatTag);
    Guid session=Guid.Empty;
    Check(cc.Initialize(0,0,1000000,0,fp,ref session),"Capture Initialize");
    Check(rc.Initialize(0,0,2000000,0,gp,ref session),"Render Initialize");
    uint renderBuffer;Check(rc.GetBufferSize(out renderBuffer),"Render buffer size");
    Guid ci=typeof(IAudioCaptureClient).GUID,ri=typeof(IAudioRenderClient).GUID;
    Check(cc.GetService(ref ci,out csp),"Capture service");cap=(IAudioCaptureClient)Marshal.GetObjectForIUnknown(csp);
    Check(rc.GetService(ref ri,out rsp),"Render service");render=(IAudioRenderClient)Marshal.GetObjectForIUnknown(rsp);
    // A bounded FIFO holds at most 200ms; never allocates in the steady-state capture/render loop.
    const int capacity=9600;float[] fifo=new float[capacity*2];int read=0,write=0,count=0;
    // Eight preallocated 20ms blocks for input and output; no callback on capture thread.
    const int blockFrames=960, slots=8;
    float[] staging=new float[blockFrames*2];int staged=0;
    float[][] pending=new float[slots][],ready=new float[slots][];
    for(int i=0;i<slots;i++){pending[i]=new float[1920];ready[i]=new float[1920];}
    int inHead=0,inTail=0,inCount=0,outHead=0,outTail=0,outCount=0;
    Exception workerError=null;
    float[] incoming=new float[9600*2];float[] block=new float[9600*2];
    var result=new RouteResult{CaptureRate=48000,RenderRate=48000,CaptureChannels=2,RenderChannels=2,OutputName=outputName};
    worker=new Thread(()=>{
      try {
        float[] work=new float[1920];
        while(true){
          lock(queueLock){
            while(inCount==0&&!stopWorker)System.Threading.Monitor.Wait(queueLock,20);
            if(inCount==0&&stopWorker)break;
            Array.Copy(pending[inHead],work,1920);inHead=(inHead+1)%slots;inCount--;result.DequeuedBlocks++;
          }
          long tick=System.Diagnostics.Stopwatch.GetTimestamp();
          onBlock?.Invoke(work);
          long elapsed=(long)((System.Diagnostics.Stopwatch.GetTimestamp()-tick)*1000000.0/System.Diagnostics.Stopwatch.Frequency);
          lock(queueLock){
            if(elapsed>result.MaxBlockProcessingUs)result.MaxBlockProcessingUs=elapsed;
            if(elapsed>20000)result.ProcessingDeadlineMisses++;
            result.SumProcessingUs+=elapsed;
            if(result.ProcessingUsCount<result.ProcessingUsSamples.Length)result.ProcessingUsSamples[result.ProcessingUsCount++]=elapsed;
            result.CompletedBlocks++;
            result.EncoderCalls++;result.ProcessedBlocks++;result.ProcessedFrames+=960;
            if(outCount==slots){result.CaptureDrops+=960;result.OutputQueueOverflowBlocks++;}
            else {Array.Copy(work,ready[outTail],1920);outTail=(outTail+1)%slots;outCount++;if(outCount>result.MaxOutputQueueBlocks)result.MaxOutputQueueBlocks=outCount;}
          }
        }
      }catch(Exception ex){lock(queueLock){workerError=ex;stopWorker=true;System.Threading.Monitor.PulseAll(queueLock);}}
    });
    worker.IsBackground=true;worker.Name="CLEARCAST bounded ONNX DSP worker";worker.Priority=ThreadPriority.AboveNormal;
    worker.Start();
    // Prime render with silence; do not route audio to physical speakers.
    uint prime=Math.Min(renderBuffer,2400);IntPtr ptr;Check(render.GetBuffer(prime,out ptr),"Prime GetBuffer");Check(render.ReleaseBuffer(prime,2),"Prime ReleaseBuffer");
    Check(rc.Start(),"Render Start");startedR=true;
    Check(cc.Start(),"Capture Start");startedC=true;
    var clock=System.Diagnostics.Stopwatch.StartNew();long last=-1;double nextProgress=0;
    while(clock.Elapsed.TotalSeconds<seconds&&!cancellation.IsCancellationRequested){
     Thread.Sleep(3);
     uint packet;Check(cap.GetNextPacketSize(out packet),"GetNextPacketSize");
     while(packet>0&&!cancellation.IsCancellationRequested){
      IntPtr data;uint frames,flags;ulong dp,qp;
      Check(cap.GetBuffer(out data,out frames,out flags,out dp,out qp),"Capture GetBuffer");
      try{
       if(frames>9600)throw new InvalidOperationException("Oversized capture packet");
       int n=(int)frames;
       if((flags&2)!=0){Array.Clear(incoming,0,n*2);result.SilentFrames+=frames;}
       else Marshal.Copy(data,incoming,0,n*2);
       for(int i=0;i<n;i++){
        staging[staged*2]=incoming[i*2];staging[staged*2+1]=incoming[i*2+1];
        staged++;
        if(staged==blockFrames){
         lock(queueLock){
          if(inCount==slots){result.CaptureDrops+=960;result.InputQueueOverflowBlocks++;}
          else {Array.Copy(staging,pending[inTail],1920);inTail=(inTail+1)%slots;inCount++;result.EnqueuedBlocks++;if(inCount>result.MaxInputQueueBlocks)result.MaxInputQueueBlocks=inCount;System.Threading.Monitor.Pulse(queueLock);}
         }
         staged=0;
        }
       }
       result.CapturedFrames+=frames;result.CapturePackets++;
       long now=clock.ElapsedTicks;
       if(last>=0){double ms=(now-last)*1000.0/System.Diagnostics.Stopwatch.Frequency;if(ms>result.MaxCaptureIntervalMs)result.MaxCaptureIntervalMs=ms;}
       last=now;
      }finally{Check(cap.ReleaseBuffer(frames),"Capture ReleaseBuffer");}
      Check(cap.GetNextPacketSize(out packet),"GetNextPacketSize");
     }
     // Transfer completed blocks without waiting for ONNX. Output is bounded by the FIFO.
     lock(queueLock){
       if(workerError!=null)throw new InvalidOperationException("DSP worker failed",workerError);
       while(outCount>0){
         float[] completed=ready[outHead];
         for(int j=0;j<960;j++){
           if(count==capacity){read=(read+1)%capacity;count--;result.CaptureDrops++;}
           fifo[write*2]=Math.Max(-0.98f,Math.Min(0.98f,completed[j*2]));
           fifo[write*2+1]=Math.Max(-0.98f,Math.Min(0.98f,completed[j*2+1]));
           write=(write+1)%capacity;count++;
         }
         outHead=(outHead+1)%slots;outCount--;result.OutputConsumedBlocks++;
       }
     }
     if(clock.Elapsed.TotalSeconds>=nextProgress){lock(queueLock){progress?.Invoke(result);}nextProgress=clock.Elapsed.TotalSeconds+.25;}
     uint padding;Check(rc.GetCurrentPadding(out padding),"Render GetCurrentPadding");
     uint available=renderBuffer-padding;
     // FIX1: The old implementation wrote at most 480 frames per loop.
     // With ~3-5ms scheduling intervals this capped throughput below 48kHz
     // and overflowed the 200ms FIFO. Drain the entire available render
     // capacity in bounded blocks, never exceeding the actual FIFO content.
     while(available>0 && count>0){
      uint toWrite=Math.Min(available,(uint)Math.Min(count,960));
      if(toWrite==0)break;
      Check(render.GetBuffer(toWrite,out ptr),"Render GetBuffer");
      for(int i=0;i<(int)toWrite;i++){
       block[i*2]=fifo[read*2];block[i*2+1]=fifo[read*2+1];
       read=(read+1)%capacity;count--;
      }
      Marshal.Copy(block,0,ptr,(int)toWrite*2);
      Check(render.ReleaseBuffer(toWrite,0),"Render ReleaseBuffer");
      result.WrittenFrames+=toWrite;
      available-=toWrite;
     }
    }
    lock(queueLock){stopWorker=true;System.Threading.Monitor.PulseAll(queueLock);}
    if(!worker.Join(5000))throw new TimeoutException("DSP worker did not stop within 5 seconds");
    if(workerError!=null)throw new InvalidOperationException("DSP worker failed",workerError);
    clock.Stop();result.ElapsedSeconds=clock.Elapsed.TotalSeconds;
    // The capture window has ended. The DSP worker has completed all queued input.
    // Account for completed output before stopping render. This is a bounded final drain,
    // NOT a claim of continuous zero-loss routing; render timing and shutdown are distinct.
    lock(queueLock){
      while(outCount>0){
        float[] completed=ready[outHead];
        for(int j=0;j<960;j++){
          if(count==capacity){read=(read+1)%capacity;count--;result.CaptureDrops++;}
          fifo[write*2]=Math.Max(-0.98f,Math.Min(0.98f,completed[j*2]));
          fifo[write*2+1]=Math.Max(-0.98f,Math.Min(0.98f,completed[j*2+1]));
          write=(write+1)%capacity;count++;
        }
        outHead=(outHead+1)%slots;outCount--;result.OutputConsumedBlocks++;
      }
      result.InputQueueRemaining=inCount;result.OutputQueueRemaining=outCount;
    }
    // The render device may still have buffered frames. Drain only while the
    // endpoint accepts data, without inventing silence or counting unrendered data.
    var drainClock=System.Diagnostics.Stopwatch.StartNew();
    while(count>0&&drainClock.ElapsedMilliseconds<500){
      uint padding;Check(rc.GetCurrentPadding(out padding),"Shutdown GetCurrentPadding");
      uint available=renderBuffer-padding;
      if(available==0){Thread.Sleep(2);continue;}
      uint toWrite=Math.Min(available,(uint)Math.Min(count,960));
      Check(render.GetBuffer(toWrite,out ptr),"Shutdown GetBuffer");
      for(int i=0;i<(int)toWrite;i++){
        block[i*2]=fifo[read*2];block[i*2+1]=fifo[read*2+1];
        read=(read+1)%capacity;count--;
      }
      Marshal.Copy(block,0,ptr,(int)toWrite*2);
      Check(render.ReleaseBuffer(toWrite,0),"Shutdown ReleaseBuffer");
      result.WrittenFrames+=toWrite;
    }
    result.FifoRemainingFrames=count;result.StagedRemainingFrames=staged;
    return result;
   }finally{
    lock(queueLock){stopWorker=true;System.Threading.Monitor.PulseAll(queueLock);}
    if(worker!=null&&worker.IsAlive)worker.Join();
    if(startedC)try{cc.Stop();}catch{};if(startedR)try{rc.Stop();}catch{};
    if(cap!=null)Marshal.ReleaseComObject(cap);if(render!=null)Marshal.ReleaseComObject(render);
    if(csp!=IntPtr.Zero)Marshal.Release(csp);if(rsp!=IntPtr.Zero)Marshal.Release(rsp);
    if(cc!=null)Marshal.ReleaseComObject(cc);if(rc!=null)Marshal.ReleaseComObject(rc);
    if(cp!=IntPtr.Zero)Marshal.Release(cp);if(rp!=IntPtr.Zero)Marshal.Release(rp);
    if(fp!=IntPtr.Zero)Marshal.FreeCoTaskMem(fp);if(gp!=IntPtr.Zero)Marshal.FreeCoTaskMem(gp);
    if(cd!=null)Marshal.ReleaseComObject(cd);if(rd!=null)Marshal.ReleaseComObject(rd);
    if(e!=null)Marshal.ReleaseComObject(e);
   }
  }
 }
}
