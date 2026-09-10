# Messages can be staged without a save or a transaction check

`AddMessageAsync` saves the message it adds. A caller that is about to call `SaveChanges` anyway therefore
pays for two saves: two round-trips, and two units of work where it intended one. `StageMessage` and
`StageMessages` put the message into the caller's change tracker and stop there, so the caller's own save
writes it.

The transaction check goes with the save, necessarily rather than incidentally. A caller who has not saved
yet may not have a transaction to observe — EF opens one of its own around `SaveChanges` when the caller
has not — so a check at staging time would reject exactly the callers the method exists for. What the check
bought is not lost but moved: the guarantee that the message and the business change commit together is now
the caller's, which is what the XML docs on both methods say.

Staging is synchronous. Without the save there is no I/O, so a `Task` and a `CancellationToken` would both
be lies, and the missing `Async` suffix is the clearest available signal that the method does not reach the
database.

## Consequences

Push-based processing does not fire for a message staged outside an explicit transaction: the notification
hangs off transaction commit (see `ProcessMessagesOnSaveChangesInterceptor`), and an implicitly committed
save never raises one. Such a message waits for the next processing cycle unless the caller calls
`ProcessMessages()` itself.

A caller that stages and never saves loses the message silently. Nothing in the library can detect this,
which is why `AddMessageAsync` remains the path the README recommends.
