namespace StateMachine;

public interface IReadOnlyStateMachine<TContext>
{
    TState At<TState>() where TState : State<TContext>;
    void Transition<TState>() where TState : State<TContext>;
}

public interface IStateMachine<TContext> : IReadOnlyStateMachine<TContext>
{
    void SetInitialState<TState>() where TState : State<TContext>;
    void Update();
}