namespace AsyncStateMachine;

public sealed class StateMachine<TContext> : IStateMachine<TContext>
{
    readonly TContext context;
    readonly StateFactory<TContext> factory;
    readonly Dictionary<Type, State<TContext>> states = [];
    CancellationTokenSource? exitCancellationTokenSource;
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

        if (!factory.factories.TryGetValue(typeof(TState), out var stateFactory))
        {
            throw new InvalidOperationException($"Type {typeof(TState).Name} is not registered in the factory. Did you forget to add it to StateFactory?");
        }

        var state = stateFactory.Invoke();
        state.Initialize(this, context);
        states[typeof(TState)] = state;
        return (TState)state;
    }

    public void ForceTransition<TState>() where TState : State<TContext>
    {
        if (exitCancellationTokenSource == null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine running.");
        }

        var to = At<TState>();
        nextState = to;
    }

    public bool TryTransition<TState>() where TState : State<TContext>
    {
        if (exitCancellationTokenSource == null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine running.");
        }

        var to = At<TState>();
        if (!to.CanBeTransition(context))
        {
            return false;
        }

        nextState = to;
        return true;
    }

    public void Initialize()
    {
        foreach (var state in factory.factories.Values.Select(static x => x.Invoke()))
        {
            state.Initialize(this, context);
            states[state.GetType()] = state;
        }
    }

    public void SetInitialState<TState>() where TState : State<TContext>
    {
        if (exitCancellationTokenSource != null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine is not running.");
        }

        nextState = At<TState>();
    }

    public async ValueTask RunAsync(CancellationToken ct = default)
    {
        if (exitCancellationTokenSource != null)
        {
            throw new InvalidOperationException("StateMachine is already running.");
        }
        
        exitCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (!exitCancellationTokenSource.IsCancellationRequested)
        {
            if (nextState == null)
            {
                break;
            }

            var stateCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(exitCancellationTokenSource.Token);
            try
            {
                var current = nextState;
                nextState = null;
                
                await current.ExecuteAsync(context, stateCancellationTokenSource.Token);
            }
            finally
            {
                stateCancellationTokenSource.Cancel();
            }
        }
    }

    public void Cancel()
    {
        exitCancellationTokenSource?.Cancel();
        exitCancellationTokenSource = null;
    }

    public void Dispose()
    {
        exitCancellationTokenSource?.Dispose();
        exitCancellationTokenSource = null;
    }

    public static IStateMachine<TContext> Create(TContext context, StateFactory<TContext> factory)
        => new StateMachine<TContext>(context, factory);
}