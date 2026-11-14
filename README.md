# CLEARCAST Audio

Local microphone noise reduction for Windows using DeepFilterNet3, ONNX Runtime, WASAPI, and a WPF interface built with C# / .NET 10.

Audio stays on your computer. The desktop application does not record audio. No RVM or camera feature is included.

## Requirements

- Windows 11 x64, with a microphone.
- [.NET 10 Windows Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) to run the framework-dependent package.
- [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) installed separately.
- Selected capture and render endpoints providing a 48 kHz, stereo, float32 shared-mode mix format.
- .NET 10 SDK or a compatible newer SDK to build from source.

## Build and test

Open Windows PowerShell in this repository and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
```

The first build restores Microsoft.ML.OnnxRuntime 1.22.0 and its dependencies from NuGet. Subsequent builds can use `-Offline`, provided the packages and targeting packs are cached.

The script verifies the model hashes, publishes a Windows x64 application, generates deterministic synthetic audio for numerical parity tests, runs lifecycle and settings tests, and creates `artifacts/CLEARCAST-Audio`. Test audio is generated locally and is not microphone audio.

Double-click `Start CLEARCAST.cmd` inside the built application folder. To create a transfer ZIP as well, pass `-Zip` when building. The Windows Desktop runtime and VB-CABLE are not bundled.

## Use

1. Select your microphone and **CABLE Input** in CLEARCAST.
2. Click **Start**.
3. In OBS or your meeting application, choose **CABLE Output** as the microphone.
4. Keep CLEARCAST open while using the enhanced microphone. Click **Stop** or close the window when finished.

In OBS, add an Audio Input Capture source for CABLE Output. Avoid simultaneously routing the original microphone to the same mix.

Device choices are saved beside the application in `profile/devices.json`. Audio never starts automatically on launch. If a saved device is unavailable, reconnect and refresh, or select another device yourself. The app retries endpoint failures up to three times, with a fresh processing state on each attempt.

## Processing

- 48 kHz audio, two 10 ms frames per processing block.
- Encoder convolution history and explicit GRU input injection.
- Carried recurrent states and bounded DF decoder checkpoints.
- ERB masking, five-tap deep filtering, and delayed overlap-add synthesis.
- Dedicated bounded DSP worker with timing and queue statistics.

## Validation and limitations

Development validation included whole-sequence ONNX numerical parity, recorded speech comparison, a ten-minute live host test, WPF Start/Stop/restart checks, settings persistence, and manual OBS testing on one PC. Automated tests simulate device failures; they do not prove physical unplug/reconnect reliability on all hardware. GitHub Actions repeats build and synthetic numerical/lifecycle tests, without microphone access.

Reported processing time is DSP block time, not end-to-end microphone latency. The final incomplete capture block may remain unprocessed at shutdown. Scheduling, device formats, and perceptual results vary by hardware and input. Cross-PC testing remains limited. Packages are not code-signed installers.

## Repository contents

- `src/`: WPF application, audio engine, and local verification modes.
- `models/`: original and modified ONNX models with pinned SHA256 hashes.
- `scripts/Build.ps1`: reproducible build, test, and packaging entry point.
- `third-party/`: upstream license texts and model attribution.

Personal recordings, device IDs/settings, local logs, and old experimental builds are excluded.

## Credits and licensing

DeepFilterNet3 is by [Hendrik Schröter and contributors](https://github.com/Rikorose/DeepFilterNet). The included state/trace/injection variants modify the upstream ONNX graph interfaces to expose recurrent state and temporal inputs; weights are preserved. See [third-party/README.md](third-party/README.md).

ONNX Runtime is from [Microsoft](https://github.com/microsoft/onnxruntime). VB-CABLE is an external prerequisite and is not redistributed.

No open-source license has been selected for CLEARCAST's original application code. Third-party components remain subject to their own licenses. Do not interpret this repository's visibility as a grant of rights to the original application code.
