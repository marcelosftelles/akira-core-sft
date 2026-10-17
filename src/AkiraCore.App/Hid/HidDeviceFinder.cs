using System.Runtime.InteropServices;
using AkiraCore.Core.Links;
using static AkiraCore.App.Hid.NativeMethods;

namespace AkiraCore.App.Hid;

public sealed record DeviceProfile(string Name, ushort VendorId, ushort ProductId)
{
    // Só VID e PID: a interface certa é escolhida pelos tamanhos de report
    public bool Matches(string path) =>
        path.Contains($"vid_{VendorId:x4}&pid_{ProductId:x4}", StringComparison.OrdinalIgnoreCase);
}

public sealed record HidCollection(DeviceProfile Profile, string Path, ushort UsagePage, ushort Usage,
    int InputReportLength, int OutputReportLength, int FeatureReportLength)
{
    public ChannelKind Kind =>
        ChannelClassifier.Classify(InputReportLength, OutputReportLength, FeatureReportLength);
}

/// <summary>
/// Entre as coleções com VID/PID conhecidos, a de comandos é a de feature
/// report de 65 bytes (cabo) ou a de 33/33 (dongle). O cabo tem prioridade.
/// </summary>
public static class HidDeviceFinder
{
    // Mesmos filtros do config.xml oficial (hid_interface)
    public static IReadOnlyList<DeviceProfile> KnownDevices { get; } =
    [
        new("AKIRA CORE USB", 0x0C45, 0x8006),
        new("AKIRA CORE Dongle 2.4G", 0x05AC, 0x024F),
        new("AKIRA CORE USB (KX-NARU-PRO-KB)", 0x05AC, 0x0250),
    ];

    // VIDs que aparecem no config.xml oficial; usados só no diagnóstico
    private static readonly string[] DiagnosticVendorTags = ["vid_0c45", "vid_05ac"];

    public static HidCollection? FindCommandInterface()
    {
        var collections = ListCollections();
        return collections.FirstOrDefault(c => c.Kind == ChannelKind.Feature)
               ?? collections.FirstOrDefault(c => c.Kind == ChannelKind.Dongle);
    }

    public static IReadOnlyList<HidCollection> ListCollections()
    {
        var result = new List<HidCollection>();
        foreach (var path in EnumerateHidPaths())
        {
            var profile = KnownDevices.FirstOrDefault(p => p.Matches(path));
            if (profile is null)
            {
                continue;
            }
            var caps = ReadCaps(path);
            if (caps is { } c)
            {
                result.Add(new HidCollection(profile, path, c.UsagePage, c.Usage,
                    c.InputReportByteLength, c.OutputReportByteLength, c.FeatureReportByteLength));
            }
        }
        return result;
    }

    /// <summary>
    /// Lista toda coleção HID dos VIDs conhecidos, mesmo com PID ou interface
    /// diferentes do esperado. Serve para descobrir por que a busca falhou.
    /// </summary>
    public static IReadOnlyList<string> Diagnose()
    {
        var lines = new List<string>();
        foreach (var path in EnumerateHidPaths())
        {
            if (!DiagnosticVendorTags.Any(tag => path.Contains(tag, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var caps = ReadCaps(path);
            var profile = KnownDevices.FirstOrDefault(p => p.Matches(path));
            lines.Add(caps is { } c
                ? $"{DescribeRole(profile, c)} | page=0x{c.UsagePage:X4} usage=0x{c.Usage:X4} in={c.InputReportByteLength} " +
                  $"out={c.OutputReportByteLength} feature={c.FeatureReportByteLength} | {path}"
                : $"desconhecido | sem acesso aos metadados | {path}");
        }
        if (lines.Count == 0)
        {
            lines.Add("Nenhuma coleção HID com VID 0C45 ou 05AC. Teclado está no modo Bluetooth ou desconectado?");
        }
        return lines;
    }

    private static string DescribeRole(DeviceProfile? profile, HidpCaps caps)
    {
        if (profile is null)
        {
            return "ignorado ";
        }
        var collection = new HidCollection(profile, "", caps.UsagePage, caps.Usage,
            caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength);
        return collection.Kind switch
        {
            ChannelKind.Feature => "CANAL USB",
            ChannelKind.Dongle => "CANAL 2.4G",
            _ => "conhecido",
        };
    }

    private static HidpCaps? ReadCaps(string path)
    {
        // Acesso 0: só metadados. Funciona até em coleções que o Windows trava (teclado)
        using var handle = CreateFile(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var preparsed))
        {
            return null;
        }
        try
        {
            return HidP_GetCaps(preparsed, out var caps) == HidpStatusSuccess ? caps : null;
        }
        finally
        {
            HidD_FreePreparsedData(preparsed);
        }
    }

    private static IEnumerable<string> EnumerateHidPaths()
    {
        HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            yield break;
        }

        try
        {
            var interfaceData = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint index = 0;
                 SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData);
                 index++)
            {
                var path = ReadDevicePath(deviceInfoSet, ref interfaceData);
                if (path is not null)
                {
                    yield return path;
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static string? ReadDevicePath(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData)
    {
        SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0,
            out var requiredSize, IntPtr.Zero);
        var buffer = Marshal.AllocHGlobal(requiredSize);
        try
        {
            // cbSize do SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 em 64 bits, 6 em 32 bits
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            return SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, buffer, requiredSize,
                out _, IntPtr.Zero)
                ? Marshal.PtrToStringUni(buffer + 4)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
