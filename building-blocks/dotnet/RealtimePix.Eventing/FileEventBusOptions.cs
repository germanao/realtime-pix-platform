namespace RealtimePix.Eventing;

public sealed class FileEventBusOptions
{
    public string Directory { get; set; } = Path.Combine(AppContext.BaseDirectory, "local-bus");

    public string ConsumerName { get; set; } = Environment.MachineName;

    public int PollIntervalMilliseconds { get; set; } = 500;
}

