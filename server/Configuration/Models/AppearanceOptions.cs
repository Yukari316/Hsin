namespace Hsin.Configuration.Models;

public sealed record AppearanceOptions
{
    public string Title             { get; init; } = "Hsin";
    public string LogoFile          { get; init; } = "";
    public string ShowcaseImageFile { get; init; } = "";
    public string IdleIconFile      { get; init; } = "";
    public string IdleMessage       { get; init; } = "直播尚未开始";
    public string PasswordPrompt    { get; init; } = "请输入房间密码";
    public string AnnouncementFile  { get; init; } = "";
}
