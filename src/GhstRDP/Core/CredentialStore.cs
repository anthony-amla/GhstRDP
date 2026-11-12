using System.Runtime.InteropServices;
using ComFileTime = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace GhstRDP.Core;

/// <summary>
/// Saves RDP logins to Windows Credential Manager under TERMSRV/host, where mstsc looks for them.
/// Calls the API directly so the password never shows up on a command line.
/// </summary>
public static class CredentialStore
{
    const int CRED_TYPE_GENERIC = 1;
    const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public ComFileTime LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredDelete(string target, int type, int flags);

    public static string TargetFor(string host) => "TERMSRV/" + host.Trim();

    /// <exception cref="InvalidOperationException">Windows refused to save the credential.</exception>
    public static void WriteRdp(string host, string userName, string password)
    {
        var blob = Marshal.StringToCoTaskMemUni(password);
        try
        {
            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = TargetFor(host),
                Comment = "Ghst RDP",
                CredentialBlobSize = password.Length * 2,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = userName,
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"Falha ao salvar credencial no Windows (erro {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public static void DeleteRdp(string host)
    {
        try { CredDelete(TargetFor(host), CRED_TYPE_GENERIC, 0); }
        catch (Exception ex) { Log.Warn("Failed to delete credential", ex); }
    }
}
