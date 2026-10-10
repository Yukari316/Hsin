namespace Hsin.Media.Control;

public sealed record RoomSnapshot
{
    public string Status     { get; init; } = "initializing";
    public string Generation { get; init; } = "";
}
