namespace RealtimeEvents.Domain;

public readonly record struct ProjectionEventId
{
    public ProjectionEventId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Projection event ID is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
