namespace AsyncStateMachine.UnitTests;

public class TransitionRequestTests
{
    static IStateMachine<TestContext> CreateMachine(TestContext context, Action<StateFactory<TestContext>> configure)
    {
        var factory = new StateFactory<TestContext>();
        configure(factory);
        return StateMachine.Create(context, factory);
    }

    [Fact]
    public void IsTransitionRequested_生成直後はfalse()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new NoopState()));

        Assert.False(machine.IsTransitionRequested);
    }

    [Fact]
    public void IsTransitionRequested_SetInitialStateで初期ステートを指定するとtrue()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new NoopState()));

        machine.SetInitialState<NoopState>();

        Assert.True(machine.IsTransitionRequested);
    }

    [Fact]
    public async Task IsTransitionRequested_遷移先の実行が始まるとfalseへ戻る()
    {
        var context = new TestContext();
        var recorder = new RecordsTransitionRequestState();

        // 前のステートが要求した遷移が消費されていることを、遷移先の入口で確認する。
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => new ForceTransitionToRecorderState());
            f.Register(() => recorder);
        });
        machine.SetInitialState<ForceTransitionToRecorderState>();

        await machine.RunAsync();

        Assert.False(recorder.OnEntry);
    }

    [Fact]
    public async Task IsTransitionRequested_ForceTransitionの直後はtrue()
    {
        var context = new TestContext();
        var recorder = new RecordsTransitionRequestState { Target = typeof(NoopState) };
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => recorder);
            f.Register(() => new NoopState());
        });
        machine.SetInitialState<RecordsTransitionRequestState>();

        await machine.RunAsync();

        Assert.False(recorder.OnEntry);
        Assert.True(recorder.AfterForceTransition);
    }

    [Fact]
    public async Task IsTransitionRequested_TryTransitionが拒否された場合はfalseのまま()
    {
        var context = new TestContext();
        var blocked = new TryTransitionState { Target = typeof(BlockedState) };
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => blocked);
            f.Register(() => new BlockedState());
        });
        machine.SetInitialState<TryTransitionState>();

        await machine.RunAsync();

        Assert.False(blocked.Result);
        Assert.False(machine.IsTransitionRequested);
    }

    [Fact]
    public async Task IsTransitionRequested_外部からの遷移要求を実行中のステートが検知できる()
    {
        var context = new TestContext();
        var waiter = new WaitsForTransitionRequestState();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => waiter);
            f.Register(() => new NoopState());
        });
        machine.SetInitialState<WaitsForTransitionRequestState>();

        var run = machine.RunAsync();
        await waiter.Started;

        // ステート自身ではなく、外部（メインのステートマシン相当）から遷移を要求する。
        Assert.True(machine.TryTransition<NoopState>());

        await run;

        Assert.Equal(
            [
                "WaitsForTransitionRequestState.Execute",
                "WaitsForTransitionRequestState.Exit",
                "NoopState.Execute",
            ],
            context.Log);
    }

    [Fact]
    public async Task IsTransitionRequested_外部からの遷移要求が拒否された場合はループを抜けない()
    {
        var context = new TestContext();
        var waiter = new WaitsForTransitionRequestState();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => waiter);
            f.Register(() => new BlockedState());
        });
        machine.SetInitialState<WaitsForTransitionRequestState>();

        using var cts = new CancellationTokenSource();
        var run = machine.RunAsync(cts.Token);
        await waiter.Started;

        Assert.False(machine.TryTransition<BlockedState>());
        Assert.False(machine.IsTransitionRequested);

        await cts.CancelAsync();
        await run;

        Assert.DoesNotContain("BlockedState.Execute", context.Log);
    }

    [Fact]
    public async Task IsTransitionRequested_RunAsyncの終了後はfalse()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => new TransitionToNoopState());
            f.Register(() => new NoopState());
        });
        machine.SetInitialState<TransitionToNoopState>();

        await machine.RunAsync();

        Assert.False(machine.IsTransitionRequested);
    }

    [Fact]
    public void IsTransitionRequested_破棄済みでも例外を送出せずfalseを返す()
    {
        var context = new TestContext();
        var machine = CreateMachine(context, f => f.Register(() => new NoopState()));
        machine.SetInitialState<NoopState>();

        machine.Dispose();

        Assert.False(machine.IsTransitionRequested);
    }
}

/// <summary><see cref="RecordsTransitionRequestState"/> へ強制遷移してから終了するステート。</summary>
public sealed class ForceTransitionToRecorderState : State<TestContext>
{
    protected override ValueTask OnExecuteAsync(TestContext context, CancellationToken ct)
    {
        stateMachine?.ForceTransition<RecordsTransitionRequestState>();
        return default;
    }
}
