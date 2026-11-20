using NAudio.Wave;
using NAudio.CoreAudioApi;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using IsoVoice.Core;

namespace IsoVoice.Proto
{
    class Program
    {
        private static IsoVoiceEngine? _engine;
        private static bool _isRunning = false;
        private static double _durationSeconds = 0;
        private static ConsoleLogLevel _logLevel = ConsoleLogLevel.Info;
        private static DateTime? _startTime = null;
        private static int _exitCode = 0;
        private static WaveFileWriter? _originalRecorder;
        private static WaveFileWriter? _rnnoiseOnlyRecorder;
        private static WaveFileWriter? _processedRecorder;

        enum ConsoleLogLevel
        {
            Debug = 0,
            Info = 1,
            Warn = 2,
            Error = 3
        }

        struct CliArgs
        {
            public string CaptureDevice;
            public string RenderDevice;
            public bool UseRNNoise;
            public int BufferMs;
            public double DurationSeconds;
            public ConsoleLogLevel LogLevel;
            public bool AutoStart;
        }

        static void Log(string message, ConsoleLogLevel level = ConsoleLogLevel.Info)
        {
            if (level >= _logLevel)
                Console.WriteLine(message);
        }

        static void Engine_Message(string msg)
        {
            Console.WriteLine(msg);
        }

        static void Engine_FrameProcessed(double inputRms, double outputRms)
        {
            // Track diagnostics from Core's processed frames
        }

        static int Main(string[] args)
        {
            CliArgs parsedArgs = ParseArgs(args);
            string captureDeviceId = parsedArgs.CaptureDevice;
            string renderDeviceId = parsedArgs.RenderDevice;
            int bufferMs = parsedArgs.BufferMs;
            _durationSeconds = parsedArgs.DurationSeconds;
            _logLevel = parsedArgs.LogLevel;

            _engine = new IsoVoiceEngine
            {
                UseRNNoise = parsedArgs.UseRNNoise,
                IsGateActive = true,
                GateThreshold = 0.15f,
                GateAttack = 0.01f,
                GateRelease = 0.1f
            };

            _engine.Message += Engine_Message;
            _engine.FrameProcessed += Engine_FrameProcessed;

            Console.OutputEncoding = Encoding.UTF8;
            string engineLabel = parsedArgs.UseRNNoise ? "RNNoise" : "Passthrough";
            Console.Title = $"IsoVoice v1 [{engineLabel}]";

            Console.WriteLine("========================================");
            Console.WriteLine("IsoVoice Live - Noise Suppression v1.0");
            Console.WriteLine("Engine: " + engineLabel);
            Console.WriteLine("Capture device: " + (string.IsNullOrEmpty(captureDeviceId) ? "default" : captureDeviceId));
            Console.WriteLine("Render device: " + (string.IsNullOrEmpty(renderDeviceId) ? "default" : renderDeviceId));
            Console.WriteLine("Buffer: " + bufferMs + " ms");
            Console.WriteLine("Duration: " + (_durationSeconds > 0 ? _durationSeconds + "s" : "infinite"));
            Console.WriteLine("Log level: " + _logLevel);
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("SAFETY: HEADPHONES REQUIRED - No speakers!");
            Console.WriteLine("       Acoustic feedback will occur with speakers.");
            Console.WriteLine();

            if (!parsedArgs.AutoStart)
            {
                Console.WriteLine("Commands:");
                Console.WriteLine("  [s] - Start/stop processing");
                Console.WriteLine("  [r] - Start/stop diagnostic recording");
                Console.WriteLine("  [g] - Toggle gate ON/OFF");
                Console.WriteLine("  [l] - List audio devices");
                Console.WriteLine("  [q] - Quit");
                Console.WriteLine();
            }

            try
            {
                // --list-devices: enumerate and exit without touching audio
                if (args.Contains("--list-devices", StringComparer.OrdinalIgnoreCase))
                {
                    _engine.ListDevices();
                    return 0;
                }

                _engine.Start(captureDeviceId, renderDeviceId, bufferMs);
                _isRunning = parsedArgs.AutoStart;

                if (_durationSeconds > 0 && _isRunning)
                {
                    _startTime = DateTime.UtcNow;
                }

                while (true)
                {
                    // Check duration timeout
                    if (_durationSeconds > 0 && _isRunning)
                    {
                        var elapsed = _startTime.HasValue 
                            ? (DateTime.UtcNow - _startTime.Value).TotalSeconds 
                            : 0;
                        if (elapsed >= _durationSeconds)
                        {
                            Log($"Duration limit reached ({_durationSeconds}s). Stopping...", ConsoleLogLevel.Info);
                            break;
                        }
                        else
                        {
                            Log($"Running... {elapsed:F1}/{_durationSeconds:F1}s", ConsoleLogLevel.Debug);
                        }
                    }

                    // Non-interactive mode or manual control
                    if (!parsedArgs.AutoStart)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Q)
                            break;
                        if (key.Key == ConsoleKey.S)
                            ToggleProcessing();
                        if (key.Key == ConsoleKey.R)
                            ToggleRecording();
                        if (key.Key == ConsoleKey.G)
                            ToggleGate();
                        if (key.Key == ConsoleKey.L)
                            _engine.ListDevices();
                    }
                    else
                    {
                        // Auto mode: sleep and check duration
                        System.Threading.Thread.Sleep(100);
                    }
                }

                Console.WriteLine("\nQuitting...");
            }
            catch (Exception ex)
            {
                Log($"Fatal error: {ex.Message}", ConsoleLogLevel.Error);
                Console.WriteLine(ex.ToString());
                _exitCode = 1;
            }
            finally
            {
                Cleanup();
            }

            return _exitCode;
        }

        static CliArgs ParseArgs(string[] args)
        {
            CliArgs result = new CliArgs();
            result.CaptureDevice = "";
            result.RenderDevice = "";
            result.UseRNNoise = true;
            result.BufferMs = 10;
            result.DurationSeconds = 0;
            result.LogLevel = ConsoleLogLevel.Info;
            result.AutoStart = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--capture-device":
                        if (i + 1 < args.Length) result.CaptureDevice = args[++i];
                        break;
                    case "--render-device":
                        if (i + 1 < args.Length) result.RenderDevice = args[++i];
                        break;
                    case "--engine":
                        if (i + 1 < args.Length)
                        {
                            string engine = args[++i].ToLowerInvariant();
                            result.UseRNNoise = engine != "passthrough";
                        }
                        break;
                    case "--buffer-ms":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int bm))
                            result.BufferMs = bm;
                        break;
                    case "--duration":
                        if (i + 1 < args.Length && double.TryParse(args[++i], out double d))
                            result.DurationSeconds = d;
                        break;
                    case "--log-level":
                        if (i + 1 < args.Length)
                        {
                            string lvl = args[++i];
                            if (Enum.TryParse<ConsoleLogLevel>(lvl, true, out ConsoleLogLevel ll))
                                result.LogLevel = ll;
                        }
                        break;
                    case "--list-devices":
                        // Handled separately in calling code
                        break;
                    case "--auto-start":
                        result.AutoStart = true;
                        break;
                }
            }

            return result;
        }

        static void ToggleProcessing()
        {
            _isRunning = !_isRunning;
            Log($"{(_isRunning ? "Starting" : "Stopping")} audio processing", ConsoleLogLevel.Info);
        }

        static void ToggleRecording()
        {
            if (_originalRecorder == null)
            {
                try
                {
                    string dir = @"K:\IsoVoice\test-output";
                    System.IO.Directory.CreateDirectory(dir);

                    var format = new WaveFormat(48000, 16, 1);
                    _originalRecorder = new WaveFileWriter(
                        System.IO.Path.Combine(dir, $"original_{DateTime.Now:yyyyMMdd_HHmmss}.wav"),
                        format);
                    _rnnoiseOnlyRecorder = new WaveFileWriter(
                        System.IO.Path.Combine(dir, $"rnnoise_only_{DateTime.Now:yyyyMMdd_HHmmss}.wav"),
                        format);
                    _processedRecorder = new WaveFileWriter(
                        System.IO.Path.Combine(dir, $"processed_{DateTime.Now:yyyyMMdd_HHmmss}.wav"),
                        format);
                    Log("Diagnostic recording STARTED", ConsoleLogLevel.Info);
                }
                catch (Exception ex)
                {
                    Log($"Failed to start recording: {ex.Message}", ConsoleLogLevel.Error);
                }
            }
            else
            {
                _originalRecorder?.Dispose();
                _rnnoiseOnlyRecorder?.Dispose();
                _processedRecorder?.Dispose();
                _originalRecorder = null;
                _rnnoiseOnlyRecorder = null;
                _processedRecorder = null;
                Log("Diagnostic recording STOPPED", ConsoleLogLevel.Info);
            }
        }

        static void ToggleGate()
        {
            if (_engine != null)
            {
                _engine.IsGateActive = !_engine.IsGateActive;
                Log($"Gate: {(_engine.IsGateActive ? "ON" : "OFF")}", ConsoleLogLevel.Info);
            }
        }

        static void Cleanup()
        {
            _isRunning = false;
            _engine?.Stop();
            _engine?.Dispose();
            _originalRecorder?.Dispose();
            _rnnoiseOnlyRecorder?.Dispose();
            _processedRecorder?.Dispose();
            Log("Cleanup complete - audio devices released", ConsoleLogLevel.Info);
        }
    }
}
