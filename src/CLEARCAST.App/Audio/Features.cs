using System;
using System.IO;
using System.Linq;
using System.Numerics;
public sealed class ExperimentalMaskSynth {
 const int N=960,H=480;
 static readonly int[] Widths={2,2,2,2,2,2,2,2,2,2,2,2,2,5,5,7,7,8,10,12,13,15,18,20,24,28,31,37,42,50,56,67};
 readonly System.Collections.Generic.Queue<Complex[]> delayed=new();
 readonly System.Collections.Generic.Queue<Complex[]> history=new();
 public long DeepFilterFrames {get;private set;}
 public long MaskFrames {get;private set;}
 public long CleanFrames {get;private set;}
 public long ZeroFrames {get;private set;}
 readonly double[] overlap=new double[H];
 readonly double[] window=new double[N];
 public long SynthesizedFrames {get;private set;}
 public double Peak {get;private set;}
 public ExperimentalMaskSynth(){for(int i=0;i<N;i++){double q=Math.Sin(Math.PI*(i+.5)/N);window[i]=Math.Sin(.5*Math.PI*q*q);}}
 public void PushSpectrum(Complex[] spectrum){var copy=(Complex[])spectrum.Clone();delayed.Enqueue(copy);history.Enqueue(copy);while(history.Count>5)history.Dequeue();}
 public float[] Render(float[] mask,float[,] coefficients,float snr){
  if(mask.Length!=32)throw new Exception("Mask length mismatch");
  var output=new float[H];
  if(delayed.Count<=2)return output;
  var half=delayed.Dequeue();int index=0;
  if(snr < -10){Array.Fill(half,Complex.Zero);ZeroFrames++;}
  else {
   if(snr<=30){for(int b=0;b<32;b++){double g=Math.Clamp((double)mask[b],0,1);for(int j=0;j<Widths[b];j++)half[index++]*=g;}MaskFrames++;}
   else CleanFrames++;
   if(snr<=20){
    // Source-aligned reference: taps cover current input frame and four previous frames.
    var h=history.ToArray();
    if(h.Length==5){for(int k=0;k<96;k++){Complex sum=Complex.Zero;for(int tap=0;tap<5;tap++)sum+=h[tap][k]*new Complex(coefficients[k,2*tap],coefficients[k,2*tap+1]);if(!double.IsFinite(sum.Real)||!double.IsFinite(sum.Imaginary))throw new Exception("Nonfinite DF spectrum");half[k]=sum;}DeepFilterFrames++;}
   }
  }
  var full=new Complex[N];full[0]=half[0];full[N/2]=half[N/2];
  for(int k=1;k<N/2;k++){full[k]=half[k];full[N-k]=Complex.Conjugate(half[k]);}
  var time=RealMicFeatures.FFT(full,true);
  for(int i=0;i<H;i++){
   // Normalized IFFT must be multiplied by N because analysis scaled by 1/N.
   double v=overlap[i]+time[i].Real*N*window[i];
   if(!double.IsFinite(v))throw new Exception("Nonfinite synthesis sample");
   output[i]=(float)Math.Clamp(v*0.94,-0.98,0.98);
   Peak=Math.Max(Peak,Math.Abs(output[i]));
   overlap[i]=time[i+H].Real*N*window[i+H];
  }
  SynthesizedFrames++;return output;
 }
}
public static class RealMicFeatures {
    const int SR=48000, N=960, H=480, Bands=32, DfBins=96;
    static void Require(bool cond,string msg) { if (!cond) throw new InvalidDataException(msg); }
    // Cooley-Tukey mixed-radix FFT: 960 = 2^6 * 3 * 5. The inverse is normalized.
    static Complex[] Transform(Complex[] input,bool inverse)
    {
        int len=input.Length;
        if(len==1) return new[]{input[0]};
        int radix=0;
        foreach(int r in new[]{2,3,5}) if(len%r==0){radix=r;break;}
        Require(radix!=0,"Unsupported FFT length "+len);
        int subLen=len/radix;
        var sub=new Complex[radix][];
        for(int j=0;j<radix;j++)
        {
            var seq=new Complex[subLen];
            for(int k=0;k<subLen;k++) seq[k]=input[k*radix+j];
            sub[j]=Transform(seq,inverse);
        }
        var output=new Complex[len];
        double sign=inverse?1:-1;
        for(int k=0;k<len;k++)
        {
            Complex acc=Complex.Zero;
            for(int j=0;j<radix;j++)
            {
                double angle=sign*2*Math.PI*j*k/len;
                acc+=sub[j][k%subLen]*Complex.FromPolarCoordinates(1,angle);
            }
            output[k]=acc;
        }
        return output;
    }
    public static Complex[] FFT(Complex[] x,bool inverse)
    {
        var y=Transform(x,inverse);
        if(inverse) for(int i=0;i<y.Length;i++) y[i]/=y.Length;
        return y;
    }
    static float[] ReadWav(string path)
    {
        using var fs=File.OpenRead(path);using var br=new BinaryReader(fs);
        Require(new string(br.ReadChars(4))=="RIFF","Not RIFF");br.ReadUInt32();
        Require(new string(br.ReadChars(4))=="WAVE","Not WAVE");
        bool fmt=false;byte[]? data=null;
        while(fs.Position+8<=fs.Length)
        {
            string id=new string(br.ReadChars(4));uint size=br.ReadUInt32();
            Require(size<=int.MaxValue && fs.Position+size<=fs.Length,"Invalid WAV chunk");
            long end=fs.Position+size;
            if(id=="fmt ")
            {
                Require(size>=16,"Invalid fmt chunk");
                ushort format=br.ReadUInt16(),channels=br.ReadUInt16();uint rate=br.ReadUInt32();
                br.ReadUInt32();br.ReadUInt16();ushort bits=br.ReadUInt16();
                Require(format==3 && channels==1 && rate==SR && bits==32,"Expected mono IEEE float32 48k WAV");
                fmt=true;
            }
            if(id=="data") data=br.ReadBytes((int)size);
            fs.Position=end+(size%2);
        }
        Require(fmt && data!=null && data.Length%4==0,"Missing or invalid WAV data");
        var samples=new float[data!.Length/4];Buffer.BlockCopy(data,0,samples,0,data.Length);
        Require(samples.All(float.IsFinite),"Input contains non-finite samples");return samples;
    }
    static readonly int[] Widths={2,2,2,2,2,2,2,2,2,2,2,2,2,5,5,7,7,8,10,12,13,15,18,20,24,28,31,37,42,50,56,67};
    public sealed class FeatureEngine
    {
        public Complex[] LastSpectrum {get;private set;} = new Complex[N/2+1];
        readonly float[] history=new float[H];
        readonly float[] mean=new float[Bands];
        readonly float[] unit=new float[DfBins];
        readonly double[] window=new double[N];
        const float alpha=.99f;
        public FeatureEngine()
        {
            for(int n=0;n<N;n++) { double s=Math.Sin(Math.PI*(n+.5)/N);window[n]=Math.Sin(.5*Math.PI*s*s); }
            for(int b=0;b<Bands;b++)mean[b]=-60f+b*(-30f/31f);
            for(int k=0;k<DfBins;k++)unit[k]=.001f+k*(-.0009f/95f);
        }
        public float[] Step(float[] samples)
        {
            Require(samples.Length==H,"Incorrect hop");
            var block=new Complex[N];
            for(int n=0;n<H;n++) {block[n]=new Complex(history[n]*window[n],0);block[n+H]=new Complex(samples[n]*window[n+H],0);}
            Array.Copy(samples,history,H);
            var spec=FFT(block,false);
            for(int z=0;z<=N/2;z++)LastSpectrum[z]=spec[z]/960.0;
            var re=new float[N/2+1];var im=new float[N/2+1];
            for(int k=0;k<N/2+1;k++){re[k]=(float)(spec[k].Real/960.0);im[k]=(float)(spec[k].Imaginary/960.0);}
            var result=new float[224];int index=0;
            for(int b=0;b<Bands;b++)
            {
                float energy=0f;float scale=1f/Widths[b];
                for(int j=0;j<Widths[b];j++,index++)energy+=(re[index]*re[index]+im[index]*im[index])*scale;
                float db=10f*(float)Math.Log10(energy+1e-10f);
                mean[b]=db*(1f-alpha)+mean[b]*alpha;
                result[b]=(db-mean[b])/40f;
            }
            for(int k=0;k<DfBins;k++)
            {
                float mag=(float)Math.Sqrt(re[k]*re[k]+im[k]*im[k]);
                unit[k]=mag*(1f-alpha)+unit[k]*alpha;
                float den=(float)Math.Sqrt(unit[k]);
                result[32+k]=re[k]/den;result[128+k]=im[k]/den;
            }
            Require(result.All(float.IsFinite),"Nonfinite feature value");
            return result;
        }
    }
    public static (float[] samples, float[] features, int frames) Extract(string wav) {
       var samples=ReadWav(wav);
       int frames=(samples.Length+H-1)/H;
       var features=new float[frames*224];var engine=new FeatureEngine();
       for(int f=0;f<frames;f++) {
          var hop=new float[H];int offset=f*H;
          Array.Copy(samples,offset,hop,0,Math.Min(H,samples.Length-offset));
          var value=engine.Step(hop);
          Array.Copy(value,0,features,f*224,224);
       }
       return (samples,features,frames);
    }
}
