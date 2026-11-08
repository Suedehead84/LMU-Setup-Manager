using Lmu.Localization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
namespace Lmu.Web;
public static class WindowsSecrets
{
 [StructLayout(LayoutKind.Sequential)]private struct Blob{public int Size;public IntPtr Data;}
 [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
 [DllImport("crypt32.dll",SetLastError=true)]private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
 [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr ptr);
 public static byte[] Protect(byte[] data)=>Transform(data,true);
 public static byte[] Unprotect(byte[] data)=>Transform(data,false);
 private static byte[] Transform(byte[] data,bool protect){var input=new Blob{Size=data.Length,Data=Marshal.AllocHGlobal(data.Length)};try{Marshal.Copy(data,0,input.Data,data.Length);Blob output;bool ok=protect?CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);if(!ok)throw new CryptographicException(UiText.C("api.protectedLocalStorageUnavailable"));try{var bytes=new byte[output.Size];Marshal.Copy(output.Data,bytes,0,bytes.Length);return bytes;}finally{LocalFree(output.Data);}}finally{Marshal.FreeHGlobal(input.Data);}}
}
