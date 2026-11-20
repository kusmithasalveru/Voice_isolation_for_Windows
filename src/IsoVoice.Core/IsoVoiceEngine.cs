using NAudio.Wave;
using NAudio.CoreAudioApi;
using System.IO;
using System.Runtime.InteropServices;

namespace IsoVoice.Core
{
    public sealed class IsoVoiceEngine : IDisposable
    {
        private const int FrameSize = 480;
        private const int SampleRate = 48000;
        private const int Channels = 1;
        private const int BitsPerSample = 16;

        public bool UseRNNoise { get; set; } = true;
        public bool IsGateActive { get; set; } = true;
        public float GateThreshold { get; set; } = 0.15f;
        public float GateAttack { get; set; } = 0.01f;
        public float GateRelease { get; set; } = 0.1f;

        public event Action<double, double>? FrameProcessed;
        public event Action<string>? Message;

        private IntPtr _rnnoiseState = IntPtr.Zero;
        private WaveIn? _capture;
        private WasapiOut? _renderer;
        private BufferedWaveProvider? _waveProvider;
        private MMDevice? _renderDevice;
        private float _gateState;
        private long _frameCount;
        private double _totalInputRms;
        private double _totalOutputRms;
        private bool _isStarted;
        private bool _disposed;

        public void Start(string? captureDeviceId = null, string? renderDeviceId = null, int bufferMs = 10)
        {
            Log("DIAG: Start() called, _isStarted=" + _isStarted);
            if (_isStarted)
                throw new InvalidOperationException("Engine already started.");
            Log("DIAG: Calling InitRNNoise()");
            InitRNNoise();
            Log("DIAG: InitRNNoise() returned OK");
            Log("DIAG: Calling SetupAudio(...)");
            SetupAudio(captureDeviceId, renderDeviceId, bufferMs);
            Log("DIAG: SetupAudio() returned OK");
            _isStarted = true;
            Log("DIAG: _isStarted=true, calling StartRecording()");
            try
            {
                _capture?.StartRecording();
                Log("DIAG: StartRecording() returned OK, _isStarted=" + _isStarted);
                Log("Microphone capture started");
            }
            catch (Exception ex)
            {
                Log("DIAG: StartRecording() FAILED: " + ex.GetType().FullName + ": " + ex.Message, "Error");
                Log("Failed to start capture: " + ex.Message, "Error");
                _isStarted = false;
                Dispose();
                throw;
            }
        }

        public void Stop()
        {
            if (!_isStarted) return;
            _isStarted = false;
            try { _capture?.StopRecording(); } catch { }
            Log("Stopped. Processed " + _frameCount + " frames.");
        }

        public void Dispose()
        {
            Stop();
            _capture?.Dispose();
            _capture = null;
            _renderer?.Dispose();
            _renderer = null;
            _waveProvider = null;
            _renderDevice = null;
            DestroyRNNoise();
            GC.SuppressFinalize(this);
            _disposed = true;
        }

        public long FramesProcessed => _frameCount;
        public double AverageInputRms => _frameCount > 0 ? _totalInputRms / _frameCount : 0;
        public double AverageOutputRms => _frameCount > 0 ? _totalOutputRms / _frameCount : 0;
        public bool IsRunning => _isStarted;

        private void InitRNNoise()
        {
            Log("DIAG: InitRNNoise() entered, UseRNNoise=" + UseRNNoise);
            if (!UseRNNoise)
            {
                Log("RNNoise disabled -- passthrough mode");
                return;
            }
            bool loaded = TryLoadDll();
            Log("DIAG: TryLoadDll() returned " + loaded);
            if (!loaded)
            {
                var ex = new InvalidOperationException("RNNoise DLL not found.");
                Log(ex.Message, "Error");
                throw ex;
            }
            int frameSize = RNNoiseApi.rnnoise_get_frame_size();
            Log("DIAG: rnnoise_get_frame_size() returned " + frameSize);
            Log("RNNoise initialized -- frame size: " + frameSize);
            _rnnoiseState = RNNoiseApi.rnnoise_create(IntPtr.Zero);
            Log("DIAG: rnnoise_create() returned 0x" + _rnnoiseState.ToString("X"));
            if (_rnnoiseState == IntPtr.Zero)
                throw new InvalidOperationException("rnnoise_create returned NULL");
            Log("RNNoise state created");
        }

        private static bool TryLoadDll()
        {
            try
            {
                string dllPath = Path.Combine(AppContext.BaseDirectory, "rnnoise.dll");

                // Write diagnostic info to a file in BaseDirectory
                try
                {
                    File.WriteAllText(
                        Path.Combine(AppContext.BaseDirectory, "tryload_debug.txt"),
                        "CWD=" + Environment.CurrentDirectory + "\r\n" +
                        "BaseDir=" + AppContext.BaseDirectory + "\r\n" +
                        "dllPath=" + dllPath + "\r\n" +
                        "FileExists=" + File.Exists(dllPath) + "\r\n" +
                        "EntryAssembly=" + (System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "null") + "\r\n" +
                        "ProcessModule=" + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName + "\r\n");
                }
                catch { }

                if (!File.Exists(dllPath))
                    return false;

                var handle = NativeLibrary.Load(dllPath);
                NativeLibrary.Free(handle);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void DestroyRNNoise()
        {
            if (_rnnoiseState != IntPtr.Zero)
            {
                RNNoiseApi.rnnoise_destroy(_rnnoiseState);
                _rnnoiseState = IntPtr.Zero;
                Log("RNNoise state freed");
            }
        }

        private void SetupAudio(string? captureDeviceId, string? renderDeviceId, int bufferMs)
        {
            Log("DIAG: SetupAudio() entered");
            _capture = new WaveIn();
            _capture.BufferMilliseconds = bufferMs;
            _capture.WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels);
            _capture.DataAvailable += OnDataAvailable;
            Log("DIAG: WaveIn created, DeviceCount=" + WaveIn.DeviceCount);
            Log("Capture initialized (" + bufferMs + "ms, " + SampleRate + "Hz mono)");

            if (!string.IsNullOrEmpty(captureDeviceId))
            {
                Log("DIAG: Selecting capture device: " + captureDeviceId);
                if (!SelectCaptureDevice(captureDeviceId))
                {
                    var ex = new InvalidOperationException("Capture device not found.");
                    Log(ex.Message, "Error");
                    ListDevices();
                    throw ex;
                }
            }

            var format = new WaveFormat(SampleRate, BitsPerSample, Channels);
            _waveProvider = new BufferedWaveProvider(format) { DiscardOnBufferOverflow = true };
            Log("DIAG: Calling ResolveRenderDevice(...)");
            _renderDevice = ResolveRenderDevice(renderDeviceId);
            Log("DIAG: ResolveRenderDevice() returned: " + (_renderDevice?.FriendlyName ?? "NULL"));
            if (_renderDevice == null)
                throw new InvalidOperationException("No suitable render device found. Install VB-CABLE.");
            Log("DIAG: Creating WasapiOut...");
            _renderer = new WasapiOut(_renderDevice, AudioClientShareMode.Shared, true, bufferMs);
            Log("DIAG: WasapiOut created, calling Init()...");
            _renderer.Init(_waveProvider);
            Log("DIAG: WasapiOut.Init() returned OK");
            _renderer.Play();
            Log("DIAG: WasapiOut.Play() returned OK");
            Log("Render device: " + _renderDevice.FriendlyName);
        }

        private MMDevice? ResolveRenderDevice(string? renderDeviceId)
        {
            if (!string.IsNullOrEmpty(renderDeviceId))
            {
                var found = FindDeviceByIdOrName(renderDeviceId);
                if (found != null) return found;
            }
            var enumerator = new MMDeviceEnumerator();
            Log("DIAG: Enumerating render devices...");
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            int count = 0;
            foreach (var d in devices)
            {
                count++;
                Log("DIAG:   [" + count + "] " + d.FriendlyName);
                if (d.FriendlyName.Contains("CABLE Input"))
                {
                    Log("Auto-detected: " + d.FriendlyName);
                    return d;
                }
            }
            foreach (var d in devices)
            {
                if (d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                {
                    Log("Auto-detected: " + d.FriendlyName);
                    return d;
                }
            }
            Log("DIAG: No VB-CABLE render device found");
            return null;
        }

        private bool SelectCaptureDevice(string deviceIdOrName)
        {
            if (int.TryParse(deviceIdOrName, out int idx) && idx >= 0 && idx < WaveIn.DeviceCount)
            {
                _capture!.DeviceNumber = idx;
                var cap = WaveIn.GetCapabilities(idx);
                Log("Capture by index [" + idx + "]: " + cap.ProductName);
                return true;
            }
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var cap = WaveIn.GetCapabilities(i);
                if (cap.ProductName.IndexOf(deviceIdOrName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _capture!.DeviceNumber = i;
                    Log("Capture by name: " + cap.ProductName);
                    return true;
                }
            }
            return false;
        }

        private MMDevice? FindDeviceByIdOrName(string search)
        {
            var enumerator = new MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (d.ID == search || d.FriendlyName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    return d;
            }
            return null;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isStarted) return;
            int bytes = e.BytesRecorded;
            int samples = Math.Min(bytes / 2, FrameSize);
            if (samples < 1) return;

            float[] input = new float[FrameSize];
            for (int i = 0; i < samples; i++)
                input[i] = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8)) * (1.0f / 32768.0f);
            for (int i = samples; i < FrameSize; i++)
                input[i] = 0.0f;

            double inputRms = 0;
            for (int i = 0; i < FrameSize; i++) inputRms += (double)input[i] * input[i];
            inputRms = Math.Sqrt(inputRms / FrameSize);
            _totalInputRms += inputRms;
            _frameCount++;

            float[] output = new float[FrameSize];
            if (UseRNNoise && _rnnoiseState != IntPtr.Zero)
            {
                float vadProb = RNNoiseApi.rnnoise_process_frame(_rnnoiseState, output, input);
                if (IsGateActive)
                {
                    if (vadProb > GateThreshold)
                        _gateState = Math.Min(1.0f, _gateState + GateAttack);
                    else
                        _gateState = Math.Max(0.0f, _gateState - GateRelease);
                    for (int i = 0; i < FrameSize; i++)
                        output[i] *= _gateState;
                }
            }
            else
            {
                Array.Copy(input, output, FrameSize);
            }

            double outputRms = 0;
            for (int i = 0; i < FrameSize; i++)
                outputRms += (double)output[i] * output[i];
            outputRms = Math.Sqrt(outputRms / FrameSize);
            _totalOutputRms += outputRms;

            FrameProcessed?.Invoke(inputRms, outputRms);

            short[] pcm = new short[FrameSize];
            for (int i = 0; i < FrameSize; i++)
                pcm[i] = (short)(Math.Clamp(output[i], -1.0f, 1.0f) * 32767.0f);

            byte[] bytesOut = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytesOut, 0, bytesOut.Length);

            if (_isStarted && _waveProvider != null)
            {
                try { _waveProvider.AddSamples(bytesOut, 0, bytesOut.Length); } catch { }
            }
        }

        public void ListDevices()
        {
            Log("\n=== Audio Capture Devices ===");
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var cap = WaveIn.GetCapabilities(i);
                Log("  [" + i + "] " + cap.ProductName);
            }
            Log("\n=== Audio Render Devices ===");
            var enumerator = new MMDeviceEnumerator();
            int idx = 0;
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                Log("  [" + idx + "] " + d.FriendlyName + " (" + d.ID + ")");
                idx++;
            }
        }

        public void PrintDiagnostics()
        {
            if (_frameCount == 0) return;
            Log("=== DIAGNOSTICS ===");
            Log("Frames captured: " + _frameCount);
            Log("Avg input RMS: " + (_totalInputRms / _frameCount).ToString("F6"));
            Log("Avg output RMS: " + (_totalOutputRms / _frameCount).ToString("F6"));
            Log("===================");
        }

        private void Log(string msg, string level = "Info")
        {
            Message?.Invoke(msg);
        }
    }
}
