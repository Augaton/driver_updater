using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace DriverUpdater.Common;

/// <summary>
/// Who signed a file, as Windows checks it before running a download: WinVerifyTrust on the
/// file's open handle (chain to a trusted root, code signing, revocation), and the signer taken
/// from that same verification, not read again from the file.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Authenticode
{
    static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2

    /// <summary>The certificate that signed the file, or null if Windows does not accept its signature.</summary>
    public static X509Certificate2? Signer(SafeFileHandle file, string path)
    {
        const uint NoUi = 2, RevokeWholeChain = 1, ChoiceFile = 1, Verify = 1, Close = 2, ExcludeRootRevocation = 0x80;
        var pathPointer = Marshal.StringToHGlobalUni(path);
        var fileInfo = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), FilePath = pathPointer, File = file.DangerousGetHandle() };
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        bool added = false;
        try
        {
            file.DangerousAddRef(ref added);
            Marshal.StructureToPtr(fileInfo, filePointer, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = NoUi, RevocationChecks = RevokeWholeChain,
                UnionChoice = ChoiceFile, File = filePointer, StateAction = Verify, ProviderFlags = ExcludeRootRevocation,
            };
            var action = GenericVerifyV2;
            int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            try
            {
                if (result != 0) return null;
                var provider = WTHelperProvDataFromStateData(data.StateData);
                var signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                var certificate = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
                if (certificate == IntPtr.Zero) return null;
                // CRYPT_PROVIDER_CERT: cbStruct, then the certificate context.
                var context = Marshal.ReadIntPtr(certificate, IntPtr.Size);
                return context == IntPtr.Zero ? null : new X509Certificate2(context); // a copy, valid after Close
            }
            finally
            {
                data.StateAction = Close;
                WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            }
        }
        finally
        {
            if (added) file.DangerousRelease();
            Marshal.FreeHGlobal(filePointer);
            Marshal.FreeHGlobal(pathPointer);
        }
    }

    /// <summary>Whether the signer is this company: its certificate's common name and organization.</summary>
    public static bool IsSignedBy(X509Certificate2 signer, params string[] companies)
    {
        string? organization = signer.SubjectName.EnumerateRelativeDistinguishedNames()
            .Where(name => name.GetSingleElementType().Value == "2.5.4.10") // O=
            .Select(name => name.GetSingleElementValue()).FirstOrDefault();
        string common = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return companies.Any(c => string.Equals(common, c, StringComparison.Ordinal) && string.Equals(organization, c, StringComparison.Ordinal));
    }

    // WINTRUST_FILE_INFO
    [StructLayout(LayoutKind.Sequential)]
    struct FileInfo
    {
        public uint Size;
        public IntPtr FilePath, File, KnownSubject;
    }

    // WINTRUST_DATA
    [StructLayout(LayoutKind.Sequential)]
    struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData, SipClientData;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData, UrlReference;
        public uint ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    [DllImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr providerData, uint signerIndex,
        [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);

    [DllImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificateIndex);
}
