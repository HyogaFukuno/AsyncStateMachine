namespace AsyncStateMachine.UnitTests;

public class CurrentStateTypeTests
{
    static IStateMachine<TestContext> CreateMachine(TestContext context, Action<StateFactory<TestContext>> configure)
    {
        var factory = new StateFactory<TestContext>();
        configure(factory);
        return StateMachine.Create(context, factory);
    }

    [Fact]
    public void CurrentStateType_生成直後はnull()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new NoopState()));

        Assert.Null(machine.CurrentStateType);
    }

    [Fact]
    public void CurrentStateType_SetInitialStateだけではnullのまま()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new NoopState()));

        machine.SetInitialState<NoopState>();

        Assert.Null(machine.CurrentStateType);
    }

    [Fact]
    public async Task CurrentStateType_実行中のステートの型を返す()
    {
        var context = new TestContext();
        var blocking = new BlockingState();
        using var machine = CreateMachine(context, f => f.Register(() => blocking));
        machine.SetInitialState<BlockingState>();

        var run = machine.RunAsync();
        await blocking.Started;

        Assert.Equal(typeof(BlockingState), machine.CurrentStateType);

        machine.Cancel();
        await run;
    }

    [Fact]
    public async Task CurrentStateType_遷移を要求しただけでは切り替わらない()
    {
        var context = new TestContext();
        var waiter = new WaitsForTransitionRequestState();
        var recorder = new RecordsCurrentStateTypeState();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => waiter);
            f.Register(() => recorder);
        });
        recorder.Machine = machine;
        machine.SetInitialState<WaitsForTransitionRequestState>();

        var run = machine.RunAsync();
        await waiter.Started;

        // ループが抜ける前に読むため、遷移が確定する前に同期的に確認する。
        machine.ForceTransition<RecordsCurrentStateTypeState>();
        Assert.Equal(typeof(WaitsForTransitionRequestState), machine.CurrentStateType);

        await run;

        Assert.Equal(typeof(RecordsCurrentStateTypeState), recorder.OnEntry);
    }

    [Fact]
    public async Task CurrentStateType_ファクトリが派生型を返しても登録時の型を返す()
    {
        var context = new TestContext();
        var recorder = new DerivedRecordsCurrentStateTypeState();
        using var machine = CreateMachine(context, f => f.Register<RecordsCurrentStateTypeState>(() => recorder));
        recorder.Machine = machine;
        machine.SetInitialState<RecordsCurrentStateTypeState>();

        await machine.RunAsync();

        Assert.Equal(typeof(RecordsCurrentStateTypeState), recorder.OnEntry);
    }

    [Fact]
    public async Task CurrentStateType_RunAsyncの終了後はnull()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f =>
        {
            f.Register(() => new TransitionToNoopState());
            f.Register(() => new NoopState());
        });
        machine.SetInitialState<TransitionToNoopState>();

        await machine.RunAsync();

        Assert.Null(machine.CurrentStateType);
    }

    [Fact]
    public async Task CurrentStateType_ステートが例外を送出した後もnull()
    {
        var context = new TestContext();
        using var machine = CreateMachine(context, f => f.Register(() => new FaultingState()));
        machine.SetInitialState<FaultingState>();

        await Assert.ThrowsAsync<FaultingState.BoomException>(() => machine.RunAsync().AsTask());

        Assert.Null(machine.CurrentStateType);
    }

    [Fact]
    public void CurrentStateType_破棄済みでも例外を送出せずnullを返す()
    {
        var context = new TestContext();
        var machine = CreateMachine(context, f => f.Register(() => new NoopState()));
        machine.SetInitialState<NoopState>();

        machine.Dispose();

        Assert.Null(machine.CurrentStateType);
    }
}
