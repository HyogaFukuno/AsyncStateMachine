namespace AsyncStateMachine.UnitTests;

public sealed class TestContext
{
    public List<string> Log { get; } = [];
}

/// <summary>何もせずに即座に終了するステート。</summary>
public class NoopState : State<TestContext>
{
    public int ExecutedCount { get; private set; }
    public int DisposedCount { get; private set; }
    public int InitializedCount { get; private set; }

    protected override void OnInitialize(TestContext context) => InitializedCount++;

    protected override ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        ExecutedCount++;
        context.Log.Add($"{GetType().Name}.Execute");
        return default;
    }

    protected override void OnDispose()
    {
        DisposedCount++;
        // Dispose 時点のコンテキストは参照できないため、静的な記録には Log を使わない。
    }
}

/// <summary><see cref="CanBeTransition"/> が常に false を返すステート。</summary>
public sealed class BlockedState : NoopState
{
    protected override bool CanBeTransition(TestContext context) => false;
}

/// <summary><see cref="NoopState"/> の派生。ファクトリが派生型を返すケースの検証に使う。</summary>
public sealed class DerivedNoopState : NoopState;

/// <summary>キャンセルされるまで待機し続けるステート。</summary>
public class BlockingState : State<TestContext>
{
    readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => started.Task;
    public bool DisposeCalled { get; private set; }
    public bool UnwindCompleted { get; private set; }

    /// <summary>キャンセル後に <see cref="OnDispose"/> が先行していないかを検証するためのフック。</summary>
    public bool DisposedBeforeUnwind { get; private set; }

    protected override async ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        started.TrySetResult(true);
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // 後始末に時間がかかるステートを模す。
            await Task.Delay(50, CancellationToken.None);
            DisposedBeforeUnwind = DisposeCalled;
            UnwindCompleted = true;
            context.Log.Add("BlockingState.Unwound");
        }
    }

    protected override void OnDispose() => DisposeCalled = true;
}

/// <summary>キャンセルされると <see cref="OperationCanceledException"/> をそのまま送出するステート。</summary>
public sealed class ThrowOnCancelState : State<TestContext>
{
    readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => started.Task;

    protected override async ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        started.TrySetResult(true);
        await Task.Delay(Timeout.Infinite, ct);
    }
}

/// <summary>実行時に必ず例外を送出するステート。</summary>
public sealed class FaultingState : State<TestContext>
{
    public sealed class BoomException : Exception;

    protected override ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
        => throw new BoomException();
}

/// <summary><see cref="NoopState"/> へ遷移してから終了するステート。</summary>
public sealed class TransitionToNoopState : State<TestContext>
{
    protected override ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        context.Log.Add("TransitionToNoopState.Execute");
        stateMachine?.ForceTransition<NoopState>();
        return default;
    }
}

/// <summary><see cref="IStateHost{TContext}.TryTransition(Type)"/> の結果を記録するステート。</summary>
public sealed class TryTransitionState : State<TestContext>
{
    public Type Target { get; set; } = typeof(NoopState);
    public bool? Result { get; private set; }

    protected override ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        Result = stateMachine?.TryTransition(Target);
        return default;
    }
}

/// <summary>
/// 外部から遷移が要求されるまでループし続けるステート。
/// <see cref="IStateHost{TContext}.IsTransitionRequested"/> だけでループを抜けられることの検証に使う。
/// </summary>
public sealed class WaitsForTransitionRequestState : State<TestContext>
{
    readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => started.Task;
    public int LoopCount { get; private set; }

    protected override async ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        context.Log.Add("WaitsForTransitionRequestState.Execute");
        started.TrySetResult(true);

        while (!ct.IsCancellationRequested && stateMachine?.IsTransitionRequested != true)
        {
            LoopCount++;
            await Task.Delay(10, CancellationToken.None);
        }

        context.Log.Add("WaitsForTransitionRequestState.Exit");
    }
}

/// <summary>実行開始時点の <see cref="IStateHost{TContext}.IsTransitionRequested"/> を記録するステート。</summary>
public sealed class RecordsTransitionRequestState : State<TestContext>
{
    public bool? OnEntry { get; private set; }
    public bool? AfterForceTransition { get; private set; }

    /// <summary>非 <c>null</c> の場合、記録後にこの型へ遷移する。</summary>
    public Type? Target { get; set; }

    protected override ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        context.Log.Add("RecordsTransitionRequestState.Execute");
        OnEntry = stateMachine?.IsTransitionRequested;

        if (Target != null)
        {
            stateMachine?.ForceTransition(Target);
            AfterForceTransition = stateMachine?.IsTransitionRequested;
        }

        return default;
    }
}

/// <summary>
/// キャンセルトークンへ例外を投げるコールバックを登録するステート。
/// 利用者側の後始末が失敗しても、ステートマシンの破棄・キャンセル経路が壊れないことの検証に使う。
/// </summary>
public sealed class ThrowingCancellationCallbackState : State<TestContext>
{
    public sealed class CallbackException : Exception;

    readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => started.Task;
    public bool DisposeCalled { get; private set; }
    public bool UnwindCompleted { get; private set; }

    protected override async ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        ct.Register(() => throw new CallbackException());
        started.TrySetResult(true);

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // 後始末に時間がかかるステートを模す。
            // 巻き戻りを待たずに DisposeAsync が戻ると、この遅延によって検出できる。
            await Task.Delay(50, CancellationToken.None);
            UnwindCompleted = true;
        }
    }

    protected override void OnDispose() => DisposeCalled = true;
}

/// <summary>本体で例外を送出し、かつキャンセルトークンへ例外を投げるコールバックを登録するステート。</summary>
public sealed class FaultingWithThrowingCallbackState : State<TestContext>
{
    protected override ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        ct.Register(() => throw new ThrowingCancellationCallbackState.CallbackException());
        throw new FaultingState.BoomException();
    }
}

/// <summary>キャンセルトークンへ例外を投げるコールバックを登録し、本体は正常に終了するステート。</summary>
public sealed class RegistersThrowingCallbackState : State<TestContext>
{
    protected override ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        ct.Register(() => throw new ThrowingCancellationCallbackState.CallbackException());
        return default;
    }
}

/// <summary><see cref="OnDispose"/> で必ず例外を送出するステート。</summary>
public sealed class ThrowOnDisposeState : State<TestContext>
{
    protected override void OnDispose() => throw new InvalidOperationException("OnDispose失敗");
}

/// <summary>State が CancellationToken を無視して即座に終了するステート。</summary>
public sealed class IgnoresCancellationState : State<TestContext>
{
    public int ExecutedCount { get; private set; }

    protected override ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        ExecutedCount++;
        return default;
    }
}
