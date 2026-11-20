using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using IsoVoice.Core;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace IsoVoice.Tray
{
    internal class EngineWrapper : IDisposable
    {
        private IsoVoiceEngine? _engine;
        private bool _disposed;
        
        public bool IsRunning => _engine?.IsRunning ?? false;
        public event Action<string>? LogMessage;
        public event Action<double, double>? FrameProcessed;
        
        public void Initialize(AppSettings settings)
        {
            if (_engine != null) return;
            
            _engine = new IsoVoiceEngine
            {
                UseRNNoise = true,
                IsGateActive = true,
                GateThreshold = 0.15f,
                GateAttack = 0.01f,
                GateRelease = 0.1f
            };
            
            _engine.Message += msg => LogMessage?.Invoke(msg);
            _engine.FrameProcessed += (in_rms, out_rms) => FrameProcessed?.Invoke(in_rms, out_rms);
            
            var captureId = string.IsNullOrWhiteSpace(settings.CaptureDeviceId) ? null : settings.CaptureDeviceId;
            var renderId = string.IsNullOrWhiteSpace(settings.RenderDeviceId) ? null : settings.RenderDeviceId;
            
            _engine.Start(captureId, renderId, settings.BufferMs);
        }
        
        public void Stop()
        {
            if (_engine == null || !_engine.IsRunning) return;
            
            try
            {
                _engine.Stop();
            }
            catch { /* Ignore during cleanup */ }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            
            Stop();
            _engine?.Dispose();
            _engine = null;
            _disposed = true;
        }
        
        public static string[] GetCaptureDevices()
        {
            return Enumerable.Range(0, WaveIn.DeviceCount)
                .Select(i => $"[{i}] {WaveIn.GetCapabilities(i).ProductName}")
                .ToArray();
        }
        
        public static string[] GetRenderDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select((d, i) => $"[{i}] {d.FriendlyName} ({d.ID})")
                .ToArray();
        }
    }
}
