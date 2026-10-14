namespace GoodieBag.Config;

public sealed class NetworkSettings
{
    public string PingHost { get; set; } = "1.1.1.1";

    public int PingIntervalSeconds { get; set; } = 2;
}
