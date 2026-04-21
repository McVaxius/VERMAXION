namespace VERMAXION.Models;

public sealed class DadTimeWindow
{
    public string StartLocal { get; set; } = "00:00";
    public string EndLocal { get; set; } = "23:59";

    public string Describe() => $"{StartLocal}-{EndLocal}";
}
