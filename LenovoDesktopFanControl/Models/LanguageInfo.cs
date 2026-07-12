namespace LenovoDesktopFanControl.Models;

public class LanguageInfo
{
    public string Code { get; init; } = "";
    public string DisplayName { get; init; } = "";

    public override string ToString() => DisplayName;
}