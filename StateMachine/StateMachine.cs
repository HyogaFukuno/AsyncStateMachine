namespace StateMachine;

public sealed class StateMachine<TContext> : IStateMachine<TContext>
{
    readonly TContext context;
    readonly StateFactory<TContext> factory;
    readonly Dictionary<Type, State<TContext>> states = [];
    State<TContext>? currentState;
    State<TContext>? nextState;

    internal StateMachine(TContext context, StateFactory<TContext> factory)
    {
        this.context = context;
        this.factory = factory;
    }

    public TState At<TState>() where TState : State<TContext>
    {
        if (states.ContainsKey(typeof(TState)))
        {
            return (TState)states[typeof(TState)];
        }

        var state = factory.factories[typeof(TState)].Invoke();
        state.stateMachine = this;
        states[typeof(TState)] = state;
        return (TState)state;
    }

    public void Transition<TState>() where TState : State<TContext>
    {
        if (exitCancellationTokenSource == null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine running.");
        }

        var to = At<TState>();
        if (!to.CanBeTransition(context))
        {
            return;
        }

        nextState = to;
    }

    public void SetInitialState<TState>() where TState : State<TContext>
    {
        if (exitCancellationTokenSource != null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine is not running.");
        }

        nextState = At<TState>();
    }

    public void Update()
    {
        if (nextState != null)
        {
            currentState
        }

    }

    public static IStateMachine<TContext> Create(TContext context, StateFactory<TContext> factory)
        => new StateMachine<TContext>(context, factory);
}