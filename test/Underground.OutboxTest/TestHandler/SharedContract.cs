namespace Underground.OutboxTest.TestHandler;

/// <summary>A contract type both modules handle, which is what a neighbour's Handler must not be given.</summary>
public record SharedContract(int Id);
