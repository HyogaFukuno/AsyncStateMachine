using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncStateMachine;

/// <summary>
/// <see cref="IStateMachine{TContext}"/> のインスタンスを生成するエントリポイント。
/// </summary>
public static class StateMachine
{
    /// <summary>
    /// ステートマシンを生成する。
    /// </summary>
    /// <remarks>
    /// <paramref name="factory"/> の登録内容はこの時点でコピーされるため、
    /// 生成後にファクトリを破棄・変更しても、返されたステートマシンには影響しない。
    /// </remarks>
    /// <typeparam name="TContext">ステート間で共有するコンテキストの型。</typeparam>
    /// <param name="context">全ステートへ渡される共有コンテキスト。</param>
    /// <param name="factory">使用するステートの生成方法を登録したファクトリ。</param>
    public static IStateMachine<TContext> Create<TContext>(TContext context, StateFactory<TContext> factory)
        => new StateMachine<TContext>(context, factory);
}

internal sealed class StateMachine<TContext> : IStateMachine<TContext>
{
    readonly TContext context;
    readonly Dictionary<Type, Func<State<TContext>>> factories;
    readonly Dictionary<Type, State<TContext>> states = [];
    CancellationTokenSource? exitCancellationTokenSource;
    TaskCompletionSource<bool>? runCompletion;
    State<TContext>? nextState;
    bool disposed;

    internal StateMachine(TContext context, StateFactory<TContext> factory)
    {
        this.context = context;
        // 登録内容をコピーし、生成後は StateFactory の寿命や変更に依存しないようにする。
        factories = new Dictionary<Type, Func<State<TContext>>>(factory.factories);
    }

    // 遷移先の有無をそのまま公開する。RunAsync のループ先頭で nextState が消費されるため、
    // 遷移が始まった時点で false へ戻る。後始末中のループ条件から読まれるため破棄済みでも例外は投げない。
    public bool IsTransitionRequested => nextState != null;

    public TState At<TState>() where TState : State<TContext>
    {
        ThrowIfDisposed();

        if (states.TryGetValue(typeof(TState), out var cached))
        {
            return (TState)cached;
        }

        if (!factories.TryGetValue(typeof(TState), out var stateFactory))
        {
            throw new InvalidOperationException($"Type {typeof(TState).Name} is not registered in the factory. Did you forget to add it to StateFactory?");
        }

        var state = stateFactory.Invoke();
        state.Initialize(this, context);
        states[typeof(TState)] = state;
        return (TState)state;
    }

    public State<TContext> At(Type stateType)
    {
        ThrowIfDisposed();

        if (states.TryGetValue(stateType, out var state))
        {
            return state;
        }
        
        if (!factories.TryGetValue(stateType, out var stateFactory))
        {
            throw new InvalidOperationException($"Type {stateType.Name} is not registered in the factory. Did you forget to add it to StateFactory?");
        }
        
        state = stateFactory.Invoke();
        state.Initialize(this, context);
        states[stateType] = state;
        return state;
    }

    public void ForceTransition<TState>() where TState : State<TContext>
    {
        ThrowIfDisposed();

        if (exitCancellationTokenSource == null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine is running.");
        }

        var to = At<TState>();
        nextState = to;
    }
    
    public void ForceTransition(Type stateType)
    {
        ThrowIfDisposed();

        if (exitCancellationTokenSource == null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine is running.");
        }

        var to = At(stateType);
        nextState = to;
    }

    public bool TryTransition<TState>() where TState : State<TContext>
    {
        ThrowIfDisposed();

        if (exitCancellationTokenSource == null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine is running.");
        }

        var to = At<TState>();
        if (!to.CanBeTransition(context))
        {
            return false;
        }

        nextState = to;
        return true;
    }
    
    public bool TryTransition(Type stateType)
    {
        ThrowIfDisposed();

        if (exitCancellationTokenSource == null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine is running.");
        }

        var to = At(stateType);
        if (!to.CanBeTransition(context))
        {
            return false;
        }

        nextState = to;
        return true;
    }

    public void Initialize()
    {
        ThrowIfDisposed();

        if (exitCancellationTokenSource != null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine is not running.");
        }

        // キーは必ず Register 時の型を使う。ファクトリが派生型を返しても At<TState>() と食い違わないようにするため。
        foreach (var pair in factories)
        {
            if (states.ContainsKey(pair.Key))
            {
                // At<TState>() で生成済みのインスタンスを破棄せず再利用する。
                continue;
            }

            var state = pair.Value.Invoke();
            state.Initialize(this, context);
            states[pair.Key] = state;
        }
    }

    public void SetInitialState<TState>() where TState : State<TContext>
    {
        ThrowIfDisposed();

        if (exitCancellationTokenSource != null)
        {
            throw new InvalidOperationException("The method can only be called when the StateMachine is not running.");
        }

        nextState = At<TState>();
    }

    public async ValueTask RunAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        
        if (exitCancellationTokenSource != null)
        {
            throw new InvalidOperationException("StateMachine is already running.");
        }

        // Cancel/Dispose によってフィールドが差し替わってもループ条件が壊れないようローカルに退避する。
        var exitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        exitCancellationTokenSource = exitCts;

        // DisposeAsync が「巻き戻りの完了」を待つための通知口。
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runCompletion = completion;

        try
        {
            while (!exitCts.IsCancellationRequested)
            {
                if (nextState == null)
                {
                    break;
                }

                var stateCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(exitCts.Token);
                try
                {
                    var current = nextState;
                    nextState = null;

                    await current.ExecuteAsync(context, stateCancellationTokenSource.Token);

                    // 本体が正常終了した場合のみ、コールバックの例外を呼び出し元へ伝達する
                    await CancelAsync(stateCancellationTokenSource);
                }
                catch
                {
                    try
                    {
                        await CancelAsync(stateCancellationTokenSource);
                    }
                    catch
                    {
                        // 本体の例外を呼び出し元へ届けるため、後始末側の例外は握り潰す
                    }

                    throw;
                }
                finally
                {
                    stateCancellationTokenSource.Dispose();
                }
            }
        }
        finally
        {
            // 正常終了・キャンセル・例外のいずれでも実行状態を解除し、再度 RunAsync できるようにする
            if (ReferenceEquals(exitCancellationTokenSource, exitCts))
            {
                exitCancellationTokenSource = null;
            }

            exitCts.Dispose();
            nextState = null;

            if (ReferenceEquals(runCompletion, completion))
            {
                runCompletion = null;
            }

            try
            {
                completion.TrySetResult(true);
            }
            finally
            {
                // 実行中に Dispose された場合、State の破棄はここまで巻き戻ってから行う。
                if (disposed)
                {
                    DisposeStates();
                }
            }
        }
    }

    public void Cancel()
    {
        ThrowIfDisposed();

        // 実行状態の解除と Dispose は RunAsync の finally が行う
        try
        {
            exitCancellationTokenSource?.Cancel();
        }
        catch
        {
            // State が登録したコールバックの例外は、キャンセル要求元へ伝播させない
        }
    }

    /// <summary>
    /// 実行中の場合、State の破棄は <see cref="RunAsync"/> が巻き戻ってから行われるため、
    /// このメソッドの完了時点では終わっていない。破棄の完了まで待つには <see cref="DisposeAsync"/> を使う。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        var exitCts = exitCancellationTokenSource;
        if (exitCts == null)
        {
            DisposeStates();
            return;
        }

        try
        {
            exitCts.Cancel();
        }
        catch
        {
            // State が登録したコールバックの例外は、破棄の呼び出し元へ伝播させない
        }
    }

    /// <summary>
    /// キャンセルを要求し、実行中の State が巻き戻るのを待ってから全 State を破棄する。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        var exitCts = exitCancellationTokenSource;
        var completion = runCompletion;
        if (exitCts == null || completion == null)
        {
            DisposeStates();
            return;
        }

        try
        {
            await CancelAsync(exitCts);
        }
        catch
        {
            // State が登録したコールバックの例外は、破棄の呼び出し元へ伝播させない。
            // ここで抜けると下の待機に到達できず、DisposeAsync の「完了を待つ」保証が失われる。
        }

        // State 側の例外は RunAsync の呼び出し元が受け取る。ここでは巻き戻りの完了だけを待つ。
        await completion.Task.ConfigureAwait(false);
    }

    void DisposeStates()
    {
        exitCancellationTokenSource?.Dispose();
        exitCancellationTokenSource = null;
        runCompletion = null;

        foreach (var state in states.Values)
        {
            try
            {
                state.Dispose();
            }
            catch
            {
                // 破棄する最中に出た例外は無視する
            }
        }

        states.Clear();
        nextState = null;
    }
    
    void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(StateMachine<>));
        }
    }

    static ValueTask CancelAsync(CancellationTokenSource cts)
    {
#if NET8_0_OR_GREATER
        return new ValueTask(cts.CancelAsync());
#else
        cts.Cancel();
        return default;
#endif
    }
}