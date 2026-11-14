#requires -Version 5.1
[CmdletBinding()]
param([switch]$Offline,[switch]$Zip)
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root 'src\CLEARCAST.App\CLEARCAST.App.csproj'
$package=Join-Path $root 'artifacts\CLEARCAST-Audio'
$app=Join-Path $package 'app'
$results=Join-Path $root 'artifacts\test-results'
New-Item -ItemType Directory -Path $app,$results -Force | Out-Null
$modelHashes=Get-Content (Join-Path $root 'models\model-hashes.json') -Raw | ConvertFrom-Json
foreach($property in $modelHashes.PSObject.Properties){
 $path=Join-Path $root ('models\'+$property.Name)
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $property.Value){throw "Model hash failed: $($property.Name)"}
}
$publish=@('publish',$project,'-c','Release','-r','win-x64','--self-contained','false','-p:NuGetAudit=false','--disable-build-servers','-m:1','-o',$app)
if($Offline){
 $config=Join-Path $results 'NuGet.Offline.Config'
 '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath $config -Encoding UTF8
 $publish+=@('--configfile',$config)
}
& dotnet @publish
if($LASTEXITCODE -ne 0){throw 'Application publish failed'}
$models=Join-Path $app 'models'
New-Item -ItemType Directory -Path $models -Force | Out-Null
Get-ChildItem (Join-Path $root 'models') -File -Filter '*.onnx' | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $models -Force}
Copy-Item -LiteralPath (Join-Path $root 'models\model-hashes.json') -Destination (Join-Path $app 'model-hashes.json') -Force
# Deterministic 3.01-second mono float32 fixture, including a partial final block.
$wav=Join-Path $results 'synthetic-reference.wav'
$count=144480
$writer=New-Object IO.BinaryWriter([IO.File]::Create($wav))
try{
 $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'));$writer.Write([int](36+$count*4));$writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '));$writer.Write([int]16)
 $writer.Write([int16]3);$writer.Write([int16]1);$writer.Write([int]48000);$writer.Write([int]192000);$writer.Write([int16]4);$writer.Write([int16]32)
 $writer.Write([Text.Encoding]::ASCII.GetBytes('data'));$writer.Write([int]($count*4))
 $random=New-Object Random(731)
 for($i=0;$i -lt $count;$i++){
  $t=$i/48000.0
  $value=if($t -lt 0.25 -or $t -gt 2.7){0.0}else{0.06*[Math]::Sin(2*[Math]::PI*180*$t)+0.02*[Math]::Sin(2*[Math]::PI*1300*$t)+0.008*($random.NextDouble()-0.5)}
  $writer.Write([single]$value)
 }
}finally{$writer.Dispose()}
$exe=Join-Path $app 'CLEARCAST.App.exe'
$process=Start-Process -FilePath $exe -ArgumentList @('--verify-engine',('"'+$models+'"'),('"'+$wav+'"')) -Wait -PassThru -WindowStyle Hidden
if($process.ExitCode -ne 0){throw 'Synthetic temporal parity failed. See startup-error.txt'}
Copy-Item -LiteralPath (Join-Path $app 'offline-parity.json') -Destination (Join-Path $results 'synthetic-parity.json') -Force
$process=Start-Process -FilePath $exe -ArgumentList @('--selftest',('"'+(Join-Path $results 'lifecycle-tests.json')+'"')) -Wait -PassThru -WindowStyle Hidden
if($process.ExitCode -ne 0){throw 'Lifecycle/settings tests failed'}
$launcher="@echo off`r`nstart `"`" `"%~dp0app\CLEARCAST.App.exe`"`r`n"
[IO.File]::WriteAllText((Join-Path $package 'Start CLEARCAST.cmd'),$launcher,[Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $package 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'third-party') -Destination $package -Recurse -Force
if($Zip){
 # Enumerate only distribution files; never include saved device choices or sessions.
 Add-Type -AssemblyName System.IO.Compression
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zipPath=Join-Path $root 'artifacts\CLEARCAST-Audio-Windows-x64.zip'
 $stream=[IO.File]::Open($zipPath,[IO.FileMode]::Create)
 $archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($file in Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object {$_.FullName -notmatch '\\profile\\' -and $_.Name -notin @('last-audio-session.json','startup-error.txt')}){
  $name='CLEARCAST-Audio/'+$file.FullName.Substring($package.Length+1).Replace('\','/')
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
 }}finally{$archive.Dispose();$stream.Dispose()}
}
Write-Host "BUILD_AND_TESTS=PASS; APPLICATION=$exe"
