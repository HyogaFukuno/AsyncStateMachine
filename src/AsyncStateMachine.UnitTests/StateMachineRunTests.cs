namespace AsyncStateMachine.UnitTests;

public class StateMachineRunTests
{
    static IStateMachine<TestContext> CreateMachine(TestContext context, Action<StateFactory<TestContext>> configure)
    {
        var factory = new StateFactory<TestContext>();
        configure(factory);
        return StateMachine.Create(context, factory);
    }

    [Fact]
    public async Task RunAsync_初期ステートを実行して遷移先がなければ終了する()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new NoopState()));
        machine.SetInitialState<NoopState>();

        await machine.RunAsync();

        Assert.Equal(["NoopState.Execute"], context.Log);
    }

    [Fact]
    public async Task RunAsync_初期ステートが未設定なら何もせずに終了する()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new NoopState()));

        await machine.RunAsync();

        Assert.Empty(context.Log);
    }

    [Fact]
    public async Task RunAsync_ForceTransitionで指定したステートへ遷移する()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => new TransitionToNoopState());
            f.Register(() => new NoopState());
        });
        machine.SetInitialState<TransitionToNoopState>();

        await machine.RunAsync();

        Assert.Equal(["TransitionToNoopState.Execute", "NoopState.Execute"], context.Log);
    }

    [Fact]
    public async Task RunAsync_事前にキャンセル済みのトークンなら何も実行しない()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new NoopState()));
        machine.SetInitialState<NoopState>();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await machine.RunAsync(cts.Token);

        Assert.Empty(context.Log);
    }

    [Fact]
    public async Task RunAsync_実行中に再度呼ぶと例外になる()
    {
        using var machine = CreateMachine(new TestContext(), f => f.Register(() => new BlockingState()));
        machine.SetInitialState<BlockingState>();

        var run = machine.RunAsync();
        await machine.At<BlockingState>().Started;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await machine.RunAsync());

        machine.Cancel();
        await run;
    }

    /// <summary>
    /// 回帰: 以前は正常終了しても実行状態が解除されず、2回目の RunAsync が
    /// "StateMachine is already running." で失敗していた。
    /// </summary>
    [Fact]
    public async Task RunAsync_完了後は再度実行できる()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new NoopState()));

        machine.SetInitialState<NoopState>();
        await machine.RunAsync();

        machine.SetInitialState<NoopState>();
        await machine.RunAsync();

        Assert.Equal(["NoopState.Execute", "NoopState.Execute"], context.Log);
    }

    /// <summary>回帰: State が例外で抜けた場合も実行状態が解除されなければならない。</summary>
    [Fact]
    public async Task RunAsync_State内の例外は伝播し実行状態は解除される()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => new FaultingState());
            f.Register(() => new NoopState());
        });

        machine.SetInitialState<FaultingState>();
        await Assert.ThrowsAsync<FaultingState.BoomException>(async () => await machine.RunAsync());

        machine.SetInitialState<NoopState>();
        await machine.RunAsync();

        Assert.Equal(["NoopState.Execute"], context.Log);
    }

    /// <summary>
    /// 回帰: 以前は Cancel() が CancellationTokenSource のフィールドを null にしていたため、
    /// await から復帰した RunAsync がループ条件でそれを参照して NullReferenceException を起こしていた。
    /// </summary>
    [Fact]
    public async Task Cancel_実行中に呼んでもNullReferenceExceptionにならない()
    {
        using var machine = CreateMachine(new TestContext(), f => f.Register(() => new BlockingState()));
        machine.SetInitialState<BlockingState>();

        var run = machine.RunAsync();
        await machine.At<BlockingState>().Started;

        machine.Cancel();
        await run;

        Assert.True(machine.At<BlockingState>().UnwindCompleted);
    }

    [Fact]
    public async Task Cancel_後に再度実行できる()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => new BlockingState());
            f.Register(() => new NoopState());
        });

        machine.SetInitialState<BlockingState>();
        var run = machine.RunAsync();
        await machine.At<BlockingState>().Started;
        machine.Cancel();
        await run;

        machine.SetInitialState<NoopState>();
        await machine.RunAsync();

        Assert.Contains("NoopState.Execute", context.Log);
    }

    [Fact]
    public async Task RunAsync_StateがOperationCanceledExceptionを握り潰せば正常終了する()
    {
        using var machine = CreateMachine(new TestContext(), f => f.Register(() => new BlockingState()));
        machine.SetInitialState<BlockingState>();

        using var cts = new CancellationTokenSource();
        var run = machine.RunAsync(cts.Token);
        await machine.At<BlockingState>().Started;
        await cts.CancelAsync();

        await run;   // 例外にならないこと
    }

    [Fact]
    public async Task RunAsync_StateがOperationCanceledExceptionを送出すればそのまま伝播する()
    {
        using var machine = CreateMachine(new TestContext(), f => f.Register(() => new ThrowOnCancelState()));
        machine.SetInitialState<ThrowOnCancelState>();

        using var cts = new CancellationTokenSource();
        var run = machine.RunAsync(cts.Token);
        await machine.At<ThrowOnCancelState>().Started;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run);
    }

    [Fact]
    public async Task TryTransition_遷移先のCanBeTransitionがfalseなら遷移しない()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => new TryTransitionState());
            f.Register(() => new BlockedState());
        });
        machine.At<TryTransitionState>().Target = typeof(BlockedState);
        machine.SetInitialState<TryTransitionState>();

        await machine.RunAsync();

        Assert.False(machine.At<TryTransitionState>().Result);
        Assert.Equal(0, machine.At<BlockedState>().ExecutedCount);
    }

    [Fact]
    public async Task TryTransition_遷移先のCanBeTransitionがtrueなら遷移する()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => new TryTransitionState());
            f.Register(() => new NoopState());
        });
        machine.At<TryTransitionState>().Target = typeof(NoopState);
        machine.SetInitialState<TryTransitionState>();

        await machine.RunAsync();

        Assert.True(machine.At<TryTransitionState>().Result);
        Assert.Equal(["NoopState.Execute"], context.Log);
    }

    /// <summary>
    /// 回帰: 以前は finally 内の Cancel() が送出するコールバック例外が、
    /// State 本体の例外を上書きして呼び出し元へ届いていた。
    /// </summary>
    [Fact]
    public async Task RunAsync_State本体の例外がキャンセルコールバックの例外に上書きされない()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new FaultingWithThrowingCallbackState()));
        machine.SetInitialState<FaultingWithThrowingCallbackState>();

        await Assert.ThrowsAsync<FaultingState.BoomException>(async () => await machine.RunAsync());
    }

    /// <summary>
    /// State 本体が正常終了した場合は、後始末のコールバック例外を黙って消さずに呼び出し元へ伝える。
    /// </summary>
    [Fact]
    public async Task RunAsync_本体が正常終了ならキャンセルコールバックの例外は伝播する()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new RegistersThrowingCallbackState()));
        machine.SetInitialState<RegistersThrowingCallbackState>();

        await Assert.ThrowsAsync<AggregateException>(async () => await machine.RunAsync());
    }
}
