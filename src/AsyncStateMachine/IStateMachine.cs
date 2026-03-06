using System;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncStateMachine;

public interface IReadOnlyStateMachine<TContext>
{
    void ForceTransition<TState>() where TState : State<TContext>;
    void ForceTransition(Type stateType);
    bool TryTransition<TState>() where TState : State<TContext>;
    bool TryTransition(Type stateType);
}

public interface IStateMachine<TContext> : IReadOnlyStateMachine<TContext>, IDisposable
{
    void Initialize();
    void SetInitialState<TState>() where TState : State<TContext>;
    TState At<TState>() where TState : State<TContext>;
    ValueTask RunAsync(CancellationToken ct = default);
    void Cancel();
}