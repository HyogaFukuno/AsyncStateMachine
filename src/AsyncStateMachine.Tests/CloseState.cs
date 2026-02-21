namespace AsyncStateMachine.Tests;

public class CloseState : State<DummyContext>
{
    protected override bool CanBeTransition(DummyContext context)
    {
        return false;
    }

    protected override ValueTask OnExecuteAsync(DummyContext context, CancellationToken ct)
    {
        Console.WriteLine("CloseState finished.");
        return ValueTask.CompletedTask;
    }
}