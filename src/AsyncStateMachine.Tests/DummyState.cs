namespace AsyncStateMachine.Tests;

public class DummyState : State<DummyContext>
{
    int loop;
    
    protected override async ValueTask OnExecuteAsync(DummyContext context, CancellationToken ct)
    {
        try
        {
            foreach (var x in Enumerable.Range(0, 5))
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                Console.WriteLine($"DummyState: {x}");
            }

            if (1 > loop++)
            {
                stateMachine?.ForceTransition<DummyState>();
            }
            else
            {
                stateMachine?.ForceTransition<CloseState>();
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        finally
        {
            Console.WriteLine($"Called Finally in DummyState");
        }
    }
}