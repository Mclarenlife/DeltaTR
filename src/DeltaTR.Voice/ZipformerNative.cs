using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DeltaTR.Voice;

// sherpa-onnx 2.0.1 C ABI. Paths and per-stream hotwords are UTF-8 even on Windows.
internal static class ZipformerNative
{
    private const string Dll = "sherpa-onnx-c-api";

    [StructLayout(LayoutKind.Sequential)]
    internal struct ModelConfig
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string Encoder;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string Decoder;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string Joiner;
        public IntPtr ParaformerEncoder, ParaformerDecoder, CtcModel;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string Tokens;
        public int Threads;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string Provider;
        public int Debug;
        public IntPtr ModelType;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string ModelingUnit;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string BpeVocab;
        public IntPtr TokensBuffer;
        public int TokensBufferSize;
        public IntPtr NemoCtc, ToneCtc;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct FstConfig { public IntPtr Graph; public int MaxActive; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Config
    {
        public int SampleRate, Features;
        public ModelConfig Model;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string DecodingMethod;
        public int MaxPaths, Endpoint;
        public float Rule1, Rule2, Rule3;
        public IntPtr HotwordsFile;
        public float HotwordsScore;
        public FstConfig Fst;
        public IntPtr RuleFsts, RuleFars;
        public float BlankPenalty;
        public IntPtr HotwordsBuffer;
        public int HotwordsBufferSize;
        public IntPtr HomophoneDir, Lexicon, ReplacementRules;
    }

    internal sealed class Model : SafeHandleZeroOrMinusOneIsInvalid
    {
        public Model() : base(true) { }
        protected override bool ReleaseHandle() { SherpaOnnxDestroyOnlineRecognizer(handle); return true; }
    }
    internal sealed class Stream : SafeHandleZeroOrMinusOneIsInvalid
    {
        public Stream() : base(true) { }
        protected override bool ReleaseHandle() { SherpaOnnxDestroyOnlineStream(handle); return true; }
    }
    [DllImport(Dll)] internal static extern Model SherpaOnnxCreateOnlineRecognizer(ref Config config);
    [DllImport(Dll)] private static extern void SherpaOnnxDestroyOnlineRecognizer(IntPtr model);
    [DllImport(Dll)] internal static extern Stream SherpaOnnxCreateOnlineStreamWithHotwords(Model model, [MarshalAs(UnmanagedType.LPUTF8Str)] string hotwords);
    [DllImport(Dll)] private static extern void SherpaOnnxDestroyOnlineStream(IntPtr stream);
    [DllImport(Dll)] internal static extern void SherpaOnnxOnlineStreamAcceptWaveform(Stream stream, int sampleRate, float[] samples, int length);
    [DllImport(Dll)] internal static extern int SherpaOnnxIsOnlineStreamReady(Model model, Stream stream);
    [DllImport(Dll)] internal static extern void SherpaOnnxDecodeOnlineStream(Model model, Stream stream);
    [DllImport(Dll)] internal static extern void SherpaOnnxOnlineStreamInputFinished(Stream stream);
    [DllImport(Dll)] internal static extern IntPtr SherpaOnnxGetOnlineStreamResultAsJson(Model model, Stream stream);
    [DllImport(Dll)] internal static extern void SherpaOnnxDestroyOnlineStreamResultJson(IntPtr json);
}
