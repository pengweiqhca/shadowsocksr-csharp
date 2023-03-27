using System.Runtime.InteropServices;

namespace Shadowsocks.Encryption;

public class Sodium
{
    private const string DLLNAME = "libsscrypto";

    static Sodium() => MbedTLS.MD5(Array.Empty<byte>());

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void crypto_stream_salsa20_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, ulong ic, byte[] k);

    [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void crypto_stream_xsalsa20_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, ulong ic, byte[] k);

    [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void crypto_stream_chacha20_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, ulong ic, byte[] k);

    [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern void crypto_stream_xchacha20_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, ulong ic, byte[] k);

    [DllImport(DLLNAME, CallingConvention = CallingConvention.Cdecl)]
    public static extern int crypto_stream_chacha20_ietf_xor_ic(byte[] c, byte[] m, ulong mlen, byte[] n, uint ic, byte[] k);
}