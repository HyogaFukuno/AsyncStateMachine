namespace AsyncStateMachine;

public interface IReadOnlyStateMachine<TContext>
{
    TState At<TState>() where TState : State<TContext>;
    void ForceTransition<TState>() where TState : State<TContext>;
    bool TryTransition<TState>() where TState : State<TContext>;
}

public interface IStateMachine<TContext> : IReadOnlyStateMachine<TContext>, IDisposable
{
    void Initialize();
    void SetInitialState<TState>() where TState : State<TContext>;
    ValueTask RunAsync(CancellationToken ct = default);
    void Cancel();
}