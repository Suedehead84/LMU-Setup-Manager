using System.Runtime.InteropServices;
namespace Lmu.Manager;
// Read-only calls through native dual-interface vtables. GUID retval methods are
// not Automation-compatible on this system; dynamic IDispatch invocation returned E_INVALIDARG.
// Method order/IIDs verified against the local netlistmgr.h. Unused slots are never invoked.
[ComImport,Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"),InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetworkConnectionIdentity
{
 [PreserveSig]int GetNetwork(out IntPtr value);
 [PreserveSig]int GetIsConnectedToInternet(out short value);
 [PreserveSig]int GetIsConnected(out short value);
 [PreserveSig]int GetConnectivity(out int value);
 [PreserveSig]int GetConnectionId(out Guid value);
 [PreserveSig]int GetAdapterId(out Guid value);
}
[ComImport,Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"),InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetworkIdentity
{
 [PreserveSig]int GetName(out IntPtr value);
 [PreserveSig]int UnusedSetName(IntPtr value);
 [PreserveSig]int GetDescription(out IntPtr value);
 [PreserveSig]int UnusedSetDescription(IntPtr value);
 [PreserveSig]int GetNetworkId(out Guid value);
}
