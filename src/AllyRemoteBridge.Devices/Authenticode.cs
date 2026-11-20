using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace AllyRemoteBridge.Devices;

public static class Authenticode
{
    public static void VerifySony(string path)
    {
        string publisher = VerifyTrusted(path);
        if (publisher is not ("Sony Interactive Entertainment Inc." or "Sony Interactive Entertainment LLC" or "Sony Computer Entertainment Inc."))
            throw new InvalidDataException($"Remote Play publisher is not recognized: {publisher}");
    }
    public static string VerifyTrusted(string path, string? expectedThumbprint = null)
    {
        var file = new FileInfoNative { Size = (uint)Marshal.SizeOf<FileInfoNative>(), Path = Path.GetFullPath(path) };
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoNative>());
        Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UI = 2, Revocation = 1, Choice = 1, File = pointer, StateAction = 1, ProviderFlags = 0x80 };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            int result = WinVerifyTrust(new nint(-1), ref action, ref data);
            if (result != 0) throw new InvalidDataException($"Authenticode verification failed for {Path.GetFileName(path)} (0x{result:X8}).");
            nint provider = WTHelperProvDataFromStateData(data.State);
            if (provider == 0) throw new InvalidDataException("Trust provider data is unavailable.");
            nint signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signer == 0) throw new InvalidDataException("Trust signer is unavailable.");
            nint certificateInfo = WTHelperGetProvCertFromChain(signer, 0);
            if (certificateInfo == 0) throw new InvalidDataException("Verified signer certificate is unavailable.");
            nint context = Marshal.ReadIntPtr(certificateInfo, 8); // CRYPT_PROVIDER_CERT.pCert on Windows x64
            using var certificate = new X509Certificate2(context);
            if (expectedThumbprint is not null && !certificate.Thumbprint.Equals(expectedThumbprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Driver catalog signer does not match the reviewed signer.");
            return certificate.GetNameInfo(X509NameType.SimpleName, false);
        }
        finally
        {
            data.StateAction = 2;
            WinVerifyTrust(new nint(-1), ref action, ref data);
            Marshal.DestroyStructure<FileInfoNative>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileInfoNative
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public nint Handle, Subject;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    {
        public uint Size;
        public nint PolicyCallback, SipClient;
        public uint UI, Revocation, Choice;
        public nint File;
        public uint StateAction;
        public nint State, Url;
        public uint ProviderFlags, UIContext;
        public nint SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern nint WTHelperProvDataFromStateData(nint state);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern nint WTHelperGetProvSignerFromChain(nint provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern nint WTHelperGetProvCertFromChain(nint signer, uint certificate);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
}
