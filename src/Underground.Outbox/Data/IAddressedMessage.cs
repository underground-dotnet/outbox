namespace Underground.Outbox.Data;

/// <summary>
/// A message addressed to one Handler, its Recipient, so a message type may have several Handlers. Only the
/// inbox addresses its messages; on the outbox a message's type alone selects its one Handler.
/// </summary>
internal interface IAddressedMessage
{
    /// <summary>The <see cref="System.Type.FullName"/> of the one Handler this message is for.</summary>
    string Recipient { get; }
}
