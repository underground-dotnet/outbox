namespace Underground.OutboxTest;

/// <summary>The schemas the template database holds, and which registration names.</summary>
internal static class TestSchemas
{
    /// <summary>Where <see cref="TestDbContext"/> and <see cref="InboxOutboxDbContext"/> map their tables.</summary>
    internal const string Default = "public";
}
