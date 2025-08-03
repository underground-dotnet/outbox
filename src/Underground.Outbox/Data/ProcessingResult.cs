namespace Underground.Outbox.Data;

public enum ProcessingResult
{
    Success,
    FailureAndStop,
    FailureAndContinue
}
