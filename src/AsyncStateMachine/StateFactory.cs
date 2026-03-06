using System;
using System.Collections.Generic;

namespace AsyncStateMachine;

public sealed class StateFactory<TContext> : IDisposable
{
    internal readonly Dictionary<Type, Func<State<TContext>>> factories = [];
    
    public void Register<TState>(Func<TState> factory, bool isOverride = false) where TState : State<TContext>
    {
        if (factories.ContainsKey(typeof(TState)) && !isOverride)
        {
            throw new InvalidOperationException($"{typeof(TState).Name} is already registered.");
        }

        factories[typeof(TState)] = factory;
    }
    
    public void Dispose()
    {
        factories.Clear();
    }
}