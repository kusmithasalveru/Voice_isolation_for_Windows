using System.Runtime.InteropServices;

namespace IsoVoice.Core
{
    /// <summary>
    /// P/Invoke bindings for the native RNNoise C library.
    /// Mirrors the exact signatures used by IsoVoice.Proto so the same
    /// rnnoise.dll is loaded and called identically.
    /// </summary>
    public static class RNNoiseApi
    {
        private const string DllName = "rnnoise.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int rnnoise_get_frame_size();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int rnnoise_get_size();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr rnnoise_create(IntPtr model);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void rnnoise_destroy(IntPtr state);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float rnnoise_process_frame(
            IntPtr state, [Out] float[] output, [In] float[] input);
    }
}