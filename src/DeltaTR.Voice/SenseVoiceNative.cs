using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DeltaTR.Voice;

// Native layouts mirror the pinned sherpa-onnx v2.0.1 c-api.h (Apache-2.0).
// Keep unused model-family fields: omitting them shifts the recognizer ABI.
// https://github.com/k2-fsa/sherpa-onnx/blob/v2.0.1/sherpa-onnx/c-api/c-api.h
internal static class SenseVoiceNative
{
    private const string Dll = "sherpa-onnx-c-api";
    [StructLayout(LayoutKind.Sequential)]
    internal struct FeatureConfig
    {
        public int sample_rate;
        public int feature_dim;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineTransducerModelConfig
    {
        public IntPtr encoder;
        public IntPtr decoder;
        public IntPtr joiner;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineParaformerModelConfig
    {
        public IntPtr model;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineNemoEncDecCtcModelConfig
    {
        public IntPtr model;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineWhisperModelConfig
    {
        public IntPtr encoder;
        public IntPtr decoder;
        public IntPtr language;
        public IntPtr task;
        public int tail_paddings;
        public int enable_token_timestamps;
        public int enable_segment_timestamps;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineTdnnModelConfig
    {
        public IntPtr model;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineSenseVoiceModelConfig
    {
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string model;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string language;
        public int use_itn;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineMoonshineModelConfig
    {
        public IntPtr preprocessor;
        public IntPtr encoder;
        public IntPtr uncached_decoder;
        public IntPtr cached_decoder;
        public IntPtr merged_decoder;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineFireRedAsrModelConfig
    {
        public IntPtr encoder;
        public IntPtr decoder;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineDolphinModelConfig
    {
        public IntPtr model;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineZipformerCtcModelConfig
    {
        public IntPtr model;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineCanaryModelConfig
    {
        public IntPtr encoder;
        public IntPtr decoder;
        public IntPtr src_lang;
        public IntPtr tgt_lang;
        public int use_pnc;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineWenetCtcModelConfig
    {
        public IntPtr model;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineOmnilingualAsrCtcModelConfig
    {
        public IntPtr model;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineMedAsrCtcModelConfig
    {
        public IntPtr model;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineFunASRNanoModelConfig
    {
        public IntPtr encoder_adaptor;
        public IntPtr llm;
        public IntPtr embedding;
        public IntPtr tokenizer;
        public IntPtr system_prompt;
        public IntPtr user_prompt;
        public int max_new_tokens;
        public float temperature;
        public float top_p;
        public int seed;
        public IntPtr language;
        public int itn;
        public IntPtr hotwords;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineFireRedAsrCtcModelConfig
    {
        public IntPtr model;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineQwen3ASRModelConfig
    {
        public IntPtr conv_frontend;
        public IntPtr encoder;
        public IntPtr decoder;
        public IntPtr tokenizer;
        public int max_total_len;
        public int max_new_tokens;
        public float temperature;
        public float top_p;
        public int seed;
        public IntPtr hotwords;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineCohereTranscribeModelConfig
    {
        public IntPtr encoder;
        public IntPtr decoder;
        public IntPtr language;
        public int use_punct;
        public int use_itn;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineModelConfig
    {
        public OfflineTransducerModelConfig transducer;
        public OfflineParaformerModelConfig paraformer;
        public OfflineNemoEncDecCtcModelConfig nemo_ctc;
        public OfflineWhisperModelConfig whisper;
        public OfflineTdnnModelConfig tdnn;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string tokens;
        public int num_threads;
        public int debug;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string provider;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string model_type;
        public IntPtr modeling_unit;
        public IntPtr bpe_vocab;
        public IntPtr telespeech_ctc;
        public OfflineSenseVoiceModelConfig sense_voice;
        public OfflineMoonshineModelConfig moonshine;
        public OfflineFireRedAsrModelConfig fire_red_asr;
        public OfflineDolphinModelConfig dolphin;
        public OfflineZipformerCtcModelConfig zipformer_ctc;
        public OfflineCanaryModelConfig canary;
        public OfflineWenetCtcModelConfig wenet_ctc;
        public OfflineOmnilingualAsrCtcModelConfig omnilingual;
        public OfflineMedAsrCtcModelConfig medasr;
        public OfflineFunASRNanoModelConfig funasr_nano;
        public OfflineFireRedAsrCtcModelConfig fire_red_asr_ctc;
        public OfflineQwen3ASRModelConfig qwen3_asr;
        public OfflineCohereTranscribeModelConfig cohere_transcribe;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineLMConfig
    {
        public IntPtr model;
        public float scale;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct HomophoneReplacerConfig
    {
        public IntPtr dict_dir;
        public IntPtr lexicon;
        public IntPtr rule_fsts;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineCtcFstDecoderConfig
    {
        public IntPtr graph;
        public int max_active;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct OfflineRecognizerConfig
    {
        public FeatureConfig feat_config;
        public OfflineModelConfig model_config;
        public OfflineLMConfig lm_config;
        [MarshalAs(UnmanagedType.LPUTF8Str)] public string decoding_method;
        public int max_active_paths;
        public IntPtr hotwords_file;
        public float hotwords_score;
        public IntPtr rule_fsts;
        public IntPtr rule_fars;
        public float blank_penalty;
        public HomophoneReplacerConfig hr;
        public OfflineCtcFstDecoderConfig ctc_fst_decoder_config;
    }
    internal sealed class Model : SafeHandleZeroOrMinusOneIsInvalid
    {
        public Model() : base(true) { }
        protected override bool ReleaseHandle() { SherpaOnnxDestroyOfflineRecognizer(handle); return true; }
    }
    internal sealed class Stream : SafeHandleZeroOrMinusOneIsInvalid
    {
        public Stream() : base(true) { }
        protected override bool ReleaseHandle() { SherpaOnnxDestroyOfflineStream(handle); return true; }
    }
    [DllImport(Dll)] internal static extern Model SherpaOnnxCreateOfflineRecognizer(ref OfflineRecognizerConfig config);
    [DllImport(Dll)] private static extern void SherpaOnnxDestroyOfflineRecognizer(IntPtr model);
    [DllImport(Dll)] internal static extern Stream SherpaOnnxCreateOfflineStream(Model model);
    [DllImport(Dll)] private static extern void SherpaOnnxDestroyOfflineStream(IntPtr stream);
    [DllImport(Dll)] internal static extern void SherpaOnnxAcceptWaveformOffline(Stream stream, int sampleRate, float[] samples, int length);
    [DllImport(Dll)] internal static extern void SherpaOnnxDecodeOfflineStream(Model model, Stream stream);
    [DllImport(Dll)] internal static extern IntPtr SherpaOnnxGetOfflineStreamResultAsJson(Stream stream);
    [DllImport(Dll)] internal static extern void SherpaOnnxDestroyOfflineStreamResultJson(IntPtr json);
}
