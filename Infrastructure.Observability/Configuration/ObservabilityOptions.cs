namespace Infrastructure.Observability.Configuration;

public sealed class ObservabilityOptions
{
    public string ServiceName { get; set; } = "PlatformFoundation.Api";
    public int RecentSampleCapacity { get; set; } = 5000;
    public bool EnableFrontendReadApi { get; set; }
    public OtlpOptions Otlp { get; set; } = new();

    public sealed class OtlpOptions
    {
        public bool Enabled { get; set; }
        // OTEL_EXPORTER_OTLP_ENDPOINT environment variable is supported by the SDK.
        // Credentials and collector URLs are never embedded in source control.
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServiceName) || ServiceName.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(ServiceName));
        if (RecentSampleCapacity is < 100 or > 50000)
            throw new ArgumentOutOfRangeException(nameof(RecentSampleCapacity));
    }
}
