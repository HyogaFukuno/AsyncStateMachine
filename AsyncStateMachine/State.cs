namespace AsyncStateMachine;

public abstract class State<TContext> : IDisposable
{
    protected IReadOnlyStateMachine<TContext>? stateMachine;

    protected internal virtual bool CanBeTransition(TContext context) => true;
    protected virtual void OnInitialize(TContext context) { }
    protected virtual ValueTask OnExecuteAsync(TContext context, CancellationToken ct) => default;
    protected virtual void OnDispose() { }

    public void Initialize(IReadOnlyStateMachine<TContext> machine, TContext context)
    {
        stateMachine = machine;
        OnInitialize(context);
    }

    public async ValueTask ExecuteAsync(TContext context, CancellationToken ct) 
        => await OnExecuteAsync(context, ct);

    public void Dispose()
    {
        OnDispose();
        GC.SuppressFinalize(this);
    }
}