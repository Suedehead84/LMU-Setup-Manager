using Lmu.Localization;
namespace Lmu.Diagnostics;
public static class SupportedAbi
{
    // 14000: original community-layout baseline.
    // 14200: installed official headers, SHA-256 and compiler evidence in docs/abi/.
    // Never accept an interval or future versions without repeating that verification.
    public static bool IsSupported(int gameVersion) => gameVersion is 14000 or 14200;
    public const string Versions = "14000 oder 14200";
}
