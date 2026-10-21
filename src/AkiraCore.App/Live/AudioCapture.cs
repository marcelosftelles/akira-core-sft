using System.Runtime.InteropServices;

namespace AkiraCore.App.Live;

/// <summary>
/// Captura o que o PC está tocando (WASAPI em modo loopback), como o oficial faz no ritmo musical
/// (CLSID do MMDeviceEnumerator em 0x574de4). Não grava nem guarda nada: as amostras viram mono e
/// vão direto para o analisador de espectro. Sem biblioteca externa, só COM do Windows.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    private const int ClsCtxAll = 0x17;
    private const int SharedMode = 0;
    private const int StreamFlagsLoopback = 0x00020000;
    private const long BufferDuration = 2_000_000; // 200 ms, em unidades de 100 ns
    private const uint BufferFlagsSilent = 0x2;
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;
    private static readonly Guid SubtypeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly Action<float[]> _onSamples;
    private Thread? _thread;
    private volatile bool _running;

    /// <param name="onSamples">Recebe blocos de amostras mono, de -1 a 1, na thread da captura.</param>
    public AudioCapture(Action<float[]> onSamples) => _onSamples = onSamples;

    /// <summary>Taxa de amostragem da saída padrão; vale depois do primeiro bloco.</summary>
    public int SampleRate { get; private set; } = 48000;

    /// <summary>Último erro (ex.: sem saída de áudio); null quando capturando.</summary>
    public string? Error { get; private set; }

    public event Action<int>? SampleRateChanged;

    public void Start()
    {
        if (_running)
        {
            return;
        }
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "AkiraCore áudio" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        _thread = null;
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                Capture();
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
            {
                // Saída trocada ou desligada (AUDCLNT_E_DEVICE_INVALIDATED): tenta de novo
                Error = $"Sem áudio para captar ({ex.Message})";
                Thread.Sleep(1000);
            }
        }
    }

    private void Capture()
    {
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        var format = IntPtr.Zero;
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device));
            var audioClientId = typeof(IAudioClient).GUID;
            Check(device.Activate(ref audioClientId, ClsCtxAll, IntPtr.Zero, out var clientObject));
            client = (IAudioClient)clientObject;
            Check(client.GetMixFormat(out format));
            var wave = Marshal.PtrToStructure<WaveFormatEx>(format);
            var isFloat = wave.FormatTag == FormatFloat
                          || (wave.FormatTag == FormatExtensible && Marshal.PtrToStructure<WaveFormatExtensible>(format).SubFormat == SubtypeFloat);
            if (!isFloat && !(wave.BitsPerSample == 16 && wave.FormatTag is FormatPcm or FormatExtensible))
            {
                throw new InvalidOperationException($"formato de áudio sem suporte ({wave.BitsPerSample} bits)");
            }
            Check(client.Initialize(SharedMode, StreamFlagsLoopback, BufferDuration, 0, format, IntPtr.Zero));
            var captureId = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref captureId, out var captureObject));
            capture = (IAudioCaptureClient)captureObject;
            if (SampleRate != (int)wave.SamplesPerSec)
            {
                SampleRate = (int)wave.SamplesPerSec;
                SampleRateChanged?.Invoke(SampleRate);
            }
            Check(client.Start());
            Error = null;

            var channels = Math.Max((int)wave.Channels, 1);
            while (_running)
            {
                Thread.Sleep(10);
                Check(capture.GetNextPacketSize(out var packet));
                while (packet > 0)
                {
                    Check(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _));
                    var mono = new float[frames];
                    if ((flags & BufferFlagsSilent) == 0)
                    {
                        ToMono(data, (int)frames, channels, isFloat, mono);
                    }
                    Check(capture.ReleaseBuffer(frames));
                    _onSamples(mono);
                    Check(capture.GetNextPacketSize(out packet));
                }
            }
            client.Stop();
        }
        finally
        {
            if (format != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(format);
            }
            Release(capture);
            Release(client);
            Release(device);
            Release(enumerator);
        }
    }

    private static unsafe void ToMono(IntPtr data, int frames, int channels, bool isFloat, float[] mono)
    {
        if (isFloat)
        {
            var samples = (float*)data;
            for (var f = 0; f < frames; f++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++)
                {
                    sum += samples[f * channels + c];
                }
                mono[f] = sum / channels;
            }
            return;
        }
        var pcm = (short*)data;
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                sum += pcm[f * channels + c] / 32768f;
            }
            mono[f] = sum / channels;
        }
    }

    private static void Check(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    public void Dispose() => Stop();

    // --- Interfaces COM do Core Audio (só os métodos usados, na ordem da vtable) -----------------

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatExtensible
    {
        public WaveFormatEx Format;
        public ushort ValidBitsPerSample;
        public uint ChannelMask;
        public Guid SubFormat;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format,
            IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition,
            out ulong counterPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
