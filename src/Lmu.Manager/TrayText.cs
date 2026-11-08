using Lmu.Localization;
namespace Lmu.Manager;

public static class TrayText
{
    public static string Tooltip(bool running,bool lan,string? language)
    {
        var text=running?"LMU Setup Manager – "+UiText.T(lan?"manager.homeNetworkActive":"manager.localOnly",language):UiText.T("manager.lmuSetupManagerServerUnavailable",language);
        return FitTooltip(text);
    }
    public static string FitTooltip(string text)
    {
        if(text.Length<=63)return text;
        var length=char.IsHighSurrogate(text[61])?61:62;
        return text[..length]+"…";
    }
}
