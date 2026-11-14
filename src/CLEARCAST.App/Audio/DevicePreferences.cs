using System;
using System.IO;
using System.Text.Json;
namespace CLEARCAST.App.Audio;
internal sealed record DevicePreferences(string MicrophoneId=null,string OutputId=null) {
 public static DevicePreferences Load(string path){
  if(!File.Exists(path))return new();
  if(new FileInfo(path).Length>16384)throw new InvalidDataException("Saved device settings are too large.");
  return JsonSerializer.Deserialize<DevicePreferences>(File.ReadAllText(path))??new();
 }
 public void Save(string path){
  Directory.CreateDirectory(Path.GetDirectoryName(path));
  string temporary=path+".tmp";
  File.WriteAllText(temporary,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
  File.Move(temporary,path,true);
 }
 public static void Verify(string folder){
  string path=Path.Combine(folder,"settings-test","devices.json");
  if(Load(path)!=new DevicePreferences())throw new Exception("Default settings failed");
  var expected=new DevicePreferences("microphone-id","cable-id");expected.Save(path);
  if(Load(path)!=expected)throw new Exception("Device selection persistence failed");
  var changed=expected with{MicrophoneId="other-microphone"};changed.Save(path);
  if(Load(path)!=changed)throw new Exception("Settings update failed");
  File.WriteAllText(path,"bad json");bool rejected=false;try{Load(path);}catch(JsonException){rejected=true;}
  if(!rejected)throw new Exception("Corrupt settings accepted");
  expected.Save(path);
  File.WriteAllText(Path.Combine(folder,"settings-tests.json"),"{\"status\":\"PASS\",\"roundTrip\":true,\"update\":true,\"corruptFileDetection\":true}");
 }
}
