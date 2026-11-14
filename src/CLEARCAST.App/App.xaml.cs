using System;
using System.IO;
using System.Windows;
using CLEARCAST.App.Audio;
using CLEARCAST.App.ViewModels;
using CLEARCAST.Application.Services;
namespace CLEARCAST.App;
public partial class App : System.Windows.Application {
 protected override async void OnStartup(StartupEventArgs e){
  base.OnStartup(e);
  try{
   if(e.Args.Length>0&&e.Args[0]=="--verify-engine"){Shutdown(Parity.Run(e.Args[1],e.Args[2]));return;}
   if(e.Args.Length>0&&e.Args[0]=="--selftest"){DevicePreferences.Verify(Path.GetDirectoryName(Path.GetFullPath(e.Args[1])));await AudioLifecycleTests.Run(e.Args[1]);Shutdown(0);return;}
   MainWindow=new MainWindow(new MainViewModel(new ProcessingModeService()));MainWindow.Show();
  }catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"startup-error.txt"),ex.ToString());Shutdown(1);}
 }
}
