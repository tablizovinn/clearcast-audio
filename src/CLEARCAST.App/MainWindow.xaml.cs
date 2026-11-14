using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CLEARCAST.App.ViewModels;
using CLEARCAST.App.Audio;
using ClearcastAudioInspection;
namespace CLEARCAST.App;
public partial class MainWindow : Window {
 AudioController controller;
 readonly string models=Path.Combine(AppContext.BaseDirectory,"models");
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(300)};
 readonly Stopwatch clock=new();
 AudioStatus latest=new("Stopped","Ready.");
 bool verified,closing,allowClose;
 readonly string settingsPath=Path.Combine(AppContext.BaseDirectory,"profile","devices.json");
 DevicePreferences preferences=new();
 string settingsWarning;
 void SavePreferences(){
  preferences=preferences with{MicrophoneId=(Microphone.SelectedItem as DeviceInfo)?.Id??preferences.MicrophoneId,OutputId=(Destination.SelectedItem as DeviceInfo)?.Id??preferences.OutputId};
  try{preferences.Save(settingsPath);}catch(Exception ex){settingsWarning="Could not save device settings: "+ex.Message;}
 }

 public MainWindow(MainViewModel viewModel){
  InitializeComponent();DataContext=viewModel;
  try{preferences=DevicePreferences.Load(settingsPath);}catch(Exception){settingsWarning="Saved device settings could not be read. Choose your devices again.";}

  timer.Tick+=(_,_)=>RenderStatus();timer.Start();
  Loaded+=async(_,_)=>{try{
   await System.Threading.Tasks.Task.Run(()=>{
    string candidate=AppContext.BaseDirectory;
    using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(candidate,"offline-parity.json")));
    if(report.RootElement.GetProperty("status").GetString()!="PASS")throw new Exception("Audio verification is missing.");
    var hashes=JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string,string>>(File.ReadAllText(Path.Combine(candidate,"model-hashes.json")));
    foreach(var pair in hashes){using var file=File.OpenRead(Path.Combine(models,pair.Key));if(Convert.ToHexString(SHA256.HashData(file))!=pair.Value)throw new Exception("Model integrity check failed: "+pair.Key);}
   });verified=true;await RefreshDevices();
  }catch(Exception ex){StateLabel.Text="Unavailable";StatusText.Text=ex.Message;RefreshButton.IsEnabled=false;}};
  Closing+=OnClosing;
 }
 async System.Threading.Tasks.Task RefreshDevices(){
  if(closing||controller?.Busy==true)return;
  string mic=(Microphone.SelectedItem as DeviceInfo)?.Id??preferences.MicrophoneId,output=(Destination.SelectedItem as DeviceInfo)?.Id??preferences.OutputId;
  RefreshButton.IsEnabled=false;StartButton.IsEnabled=false;
  try{
   var choices=await System.Threading.Tasks.Task.Run(()=>(Inspector.CaptureDevices().Where(x=>x.State=="Active").ToArray(),Inspector.RenderDevices().Where(x=>x.State=="Active"&&x.Name.Contains("CABLE Input",StringComparison.OrdinalIgnoreCase)).ToArray()));
   Microphone.ItemsSource=choices.Item1;Destination.ItemsSource=choices.Item2;
   Microphone.SelectedItem=choices.Item1.FirstOrDefault(x=>string.Equals(x.Id,mic,StringComparison.OrdinalIgnoreCase))??(mic==null?choices.Item1.FirstOrDefault(x=>x.Name.Contains("Microphone (Realtek",StringComparison.OrdinalIgnoreCase)):null);
   Destination.SelectedItem=choices.Item2.FirstOrDefault(x=>string.Equals(x.Id,output,StringComparison.OrdinalIgnoreCase))??(output==null&&choices.Item2.Length==1?choices.Item2[0]:null);
   controller=null;latest=new("Stopped","Ready.");
   StateLabel.Text="Ready";StatusText.Text=settingsWarning??(mic!=null&&Microphone.SelectedItem==null?"Your saved microphone is unavailable. Reconnect it and refresh, or select another microphone.":output!=null&&Destination.SelectedItem==null?"Your saved virtual cable is unavailable. Refresh after reconnecting it.":"Choose your microphone and virtual cable, then start.");
  }catch(Exception ex){StateLabel.Text="Device check failed";StatusText.Text=ex.Message;}
  finally{RefreshButton.IsEnabled=!closing;StartButton.IsEnabled=verified&&!closing;}
 }
 async void Refresh_Click(object sender,RoutedEventArgs e)=>await RefreshDevices();
 void Start_Click(object sender,RoutedEventArgs e){
  if(!verified||closing||controller?.Busy==true)return;
  if(Microphone.SelectedItem is not DeviceInfo input||Destination.SelectedItem is not DeviceInfo output){StatusText.Text="Select a microphone and CABLE Input first.";return;}
  SavePreferences();
  controller=new AudioController((token,changed)=>LiveAudioBackend.Run(models,input.Id,output.Id,token,changed));
  controller.Changed+=s=>System.Threading.Volatile.Write(ref latest,s);
  latest=new("Starting","Preparing audio…");clock.Restart();controller.Start();RenderStatus();
 }
 async void Stop_Click(object sender,RoutedEventArgs e){if(controller==null)return;StopButton.IsEnabled=false;StateLabel.Text="Stopping…";await controller.StopAsync();clock.Stop();RenderStatus();}
 void RenderStatus(){
  if(!verified||closing)return;
  bool busy=controller?.Busy==true;
  Microphone.IsEnabled=Destination.IsEnabled=RefreshButton.IsEnabled=!busy;StartButton.IsEnabled=!busy;StopButton.IsEnabled=busy;
  if(controller==null)return;
  var s=System.Threading.Volatile.Read(ref latest);StateLabel.Text=s.State;StatusText.Text=s.Message;
  InputLevel.Value=s.State=="Running"?s.InputPeak:0;
  Metrics.Text=$"{clock.Elapsed:mm\\:ss} elapsed · {s.Blocks:N0} blocks · {s.MeanMs:F2} ms average · {s.MaxMs:F2} ms max\n{s.Misses} late blocks · {s.Drops} dropped frames · {s.Underruns} underrun frames · {s.Recoveries} reconnect attempts";
  if(!busy)clock.Stop();
 }
 async void OnClosing(object sender,CancelEventArgs e){
  if(allowClose)return;e.Cancel=true;if(closing)return;closing=true;IsEnabled=false;StateLabel.Text="Stopping audio…";
  SavePreferences();if(controller!=null)await controller.StopAsync();timer.Stop();allowClose=true;Close();
 }
}
