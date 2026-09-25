namespace Underground.Outbox.Configuration;

internal static class ServiceConfigurationLimits
{
    internal static readonly TimeSpan MinHandlerTimeout = TimeSpan.FromMilliseconds(10);
    internal static readonly TimeSpan MaxHandlerTimeout = TimeSpan.FromHours(24);

    internal static readonly TimeSpan MaxCompletedMessageRetention = TimeSpan.FromDays(365);

    internal static readonly TimeSpan MinCleanupInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaxCleanupInterval = TimeSpan.FromDays(31);
}
