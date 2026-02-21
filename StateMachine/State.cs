namespace StateMachine;

public abstract class State<TContext> : IDisposable
{
    protected internal IReadOnlyStateMachine<TContext>? stateMachine;

    protected internal bool CanBeTransition(TContext context) => true;
    protected virtual void OnUpdate(TContext context) { }
    protected virtual void OnDispose() { }

    public void Update(TContext context) => OnUpdate(context);

    public void Dispose()
    {
        OnDispose();
        GC.SuppressFinalize(this);
    }
}