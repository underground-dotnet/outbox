namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// An inbox message with two Handlers, <see cref="SharedMessageLedgerHandler"/> and
/// <see cref="SharedMessageAuditHandler"/>, so each is stored once per Recipient.
/// </summary>
/// <param name="Id">Identifies the message to the Handlers.</param>
/// <param name="AuditFails">Makes the audit Handler throw, leaving the ledger Handler to succeed.</param>
public record SharedMessage(int Id, bool AuditFails = false);
