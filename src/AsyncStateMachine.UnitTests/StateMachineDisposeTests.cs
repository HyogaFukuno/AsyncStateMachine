namespace AsyncStateMachine.UnitTests;

public class StateMachineDisposeTests
{
    static IStateMachine<TestContext> CreateMachine(Action<StateFactory<TestContext>> configure)
    {
        var factory = new StateFactory<TestContext>();
        configure(factory);
        return StateMachine.Create(new TestContext(), factory);
    }

    /// <summary>回帰: 以前は Dispose() が生成済みの State をまったく破棄していなかった。</summary>
    [Fact]
    public void Dispose_生成済みの全ステートのOnDisposeを呼ぶ()
    {
        var machine = CreateMachine(f =>
        {
            f.Register(() => new NoopState());
            f.Register(() => new BlockedState());
        });
        machine.Initialize();

        var noop = machine.At<NoopState>();
        var blocked = machine.At<BlockedState>();

        machine.Dispose();

        Assert.Equal(1, noop.DisposedCount);
        Assert.Equal(1, blocked.DisposedCount);
    }

    [Fact]
    public void Dispose_二重に呼んでもOnDisposeは一度しか呼ばれない()
    {
        var machine = CreateMachine(f => f.Register(() => new NoopState()));
        var state = machine.At<NoopState>();

        machine.Dispose();
        machine.Dispose();

        Assert.Equal(1, state.DisposedCount);
    }

    [Fact]
    public async Task DisposeAsync_二重に呼んでも安全()
    {
        var machine = CreateMachine(f => f.Register(() => new NoopState()));
        var state = machine.At<NoopState>();

        await machine.DisposeAsync();
        await machine.DisposeAsync();
        machine.Dispose();

        Assert.Equal(1, state.DisposedCount);
    }

    /// <summary>
    /// 回帰: 以前は破棄済みフラグがなく登録情報も残っていたため、
    /// Dispose 後に SetInitialState → RunAsync を呼ぶと State が再生成されて動作していた。
    /// </summary>
    [Theory]
    [InlineData("At")]
    [InlineData("AtType")]
    [InlineData("Initialize")]
    [InlineData("SetInitialState")]
    [InlineData("Cancel")]
    [InlineData("ForceTransition")]
    [InlineData("TryTransition")]
    public void Dispose_後の操作はObjectDisposedExceptionになる(string operation)
    {
        var machine = CreateMachine(f => f.Register(() => new NoopState()));
        machine.Dispose();

        Action act = operation switch
        {
            "At" => () => machine.At<NoopState>(),
            "AtType" => () => machine.At(typeof(NoopState)),
            "Initialize" => machine.Initialize,
            "SetInitialState" => machine.SetInitialState<NoopState>,
            "Cancel" => machine.Cancel,
            "ForceTransition" => machine.ForceTransition<NoopState>,
            "TryTransition" => () => machine.TryTransition<NoopState>(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        Assert.Throws<ObjectDisposedException>(act);
    }

    [Fact]
    public async Task Dispose_後のRunAsyncはObjectDisposedExceptionになる()
    {
        var machine = CreateMachine(f => f.Register(() => new NoopState()));
        machine.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await machine.RunAsync());
    }

    /// <summary>
    /// 回帰: 以前は実行中に Dispose すると、走行中の State が ExecuteAsync から
    /// 巻き戻る前に OnDispose() が呼ばれていた。
    /// </summary>
    [Fact]
    public async Task DisposeAsync_実行中はStateの巻き戻りを待ってから破棄する()
    {
        var machine = CreateMachine(f => f.Register(() => new BlockingState()));
        machine.SetInitialState<BlockingState>();

        var state = machine.At<BlockingState>();
        var run = machine.RunAsync();
        await state.Started;

        await machine.DisposeAsync();

        Assert.True(state.UnwindCompleted);
        Assert.True(state.DisposeCalled);
        Assert.False(state.DisposedBeforeUnwind);

        await run;
    }

    /// <summary>
    /// 同期 Dispose はキャンセルを要求するだけで待たずに戻る。
    /// State の破棄は RunAsync が巻き戻った時点で行われる。
    /// </summary>
    [Fact]
    public async Task Dispose_実行中は巻き戻り後にOnDisposeが呼ばれる()
    {
        var machine = CreateMachine(f => f.Register(() => new BlockingState()));
        machine.SetInitialState<BlockingState>();

        var state = machine.At<BlockingState>();
        var run = machine.RunAsync();
        await state.Started;

        machine.Dispose();
        Assert.False(state.DisposeCalled);   // まだ巻き戻っていない

        await run;

        Assert.True(state.UnwindCompleted);
        Assert.True(state.DisposeCalled);
        Assert.False(state.DisposedBeforeUnwind);
    }

    [Fact]
    public async Task DisposeAsync_実行していないときもステートを破棄する()
    {
        var machine = CreateMachine(f => f.Register(() => new NoopState()));
        var state = machine.At<NoopState>();

        await machine.DisposeAsync();

        Assert.Equal(1, state.DisposedCount);
    }

    /// <summary>
    /// 回帰: 以前は State が登録したキャンセルコールバックの例外が DisposeAsync から漏れ、
    /// 巻き戻りの待機（await completion.Task）へ到達しないまま戻っていた。
    /// </summary>
    [Fact]
    public async Task DisposeAsync_キャンセルコールバックが例外を投げても巻き戻りを待つ()
    {
        var machine = CreateMachine(f => f.Register(() => new ThrowingCancellationCallbackState()));
        machine.SetInitialState<ThrowingCancellationCallbackState>();

        var state = machine.At<ThrowingCancellationCallbackState>();
        var run = machine.RunAsync();
        await state.Started;

        await machine.DisposeAsync();

        // DisposeAsync から戻った時点で、巻き戻りと破棄の両方が終わっていること。
        Assert.True(state.UnwindCompleted);
        Assert.True(state.DisposeCalled);

        await run;
    }

    /// <summary>回帰: 以前は State が登録したキャンセルコールバックの例外が Dispose() から漏れていた。</summary>
    [Fact]
    public async Task Dispose_キャンセルコールバックの例外を呼び出し元へ伝播させない()
    {
        var machine = CreateMachine(f => f.Register(() => new ThrowingCancellationCallbackState()));
        machine.SetInitialState<ThrowingCancellationCallbackState>();

        var state = machine.At<ThrowingCancellationCallbackState>();
        var run = machine.RunAsync();
        await state.Started;

        Assert.Null(Record.Exception(() => machine.Dispose()));

        await run;

        // 例外を握り潰しても、破棄自体は最後まで進むこと。
        Assert.True(state.UnwindCompleted);
        Assert.True(state.DisposeCalled);
    }

    /// <summary>回帰: 以前は State が登録したキャンセルコールバックの例外が Cancel() から漏れていた。</summary>
    [Fact]
    public async Task Cancel_キャンセルコールバックの例外を呼び出し元へ伝播させない()
    {
        using var machine = CreateMachine(f => f.Register(() => new ThrowingCancellationCallbackState()));
        machine.SetInitialState<ThrowingCancellationCallbackState>();

        var state = machine.At<ThrowingCancellationCallbackState>();
        var run = machine.RunAsync();
        await state.Started;

        Assert.Null(Record.Exception(() => machine.Cancel()));

        await run;

        Assert.True(state.UnwindCompleted);
    }

    /// <summary>
    /// 回帰: 以前は一つの State の OnDispose() が例外を投げると、後続の State が破棄されなかった。
    /// </summary>
    [Fact]
    public void Dispose_OnDisposeが例外を投げても後続のステートを破棄する()
    {
        var machine = CreateMachine(f =>
        {
            f.Register(() => new ThrowOnDisposeState());
            f.Register(() => new NoopState());
        });
        machine.Initialize();

        var noop = machine.At<NoopState>();

        Assert.Null(Record.Exception(() => machine.Dispose()));
        Assert.Equal(1, noop.DisposedCount);
    }

    [Fact]
    public async Task AwaitUsing_でステートが破棄される()
    {
        NoopState state;
        await using (var machine = CreateMachine(f => f.Register(() => new NoopState())))
        {
            state = machine.At<NoopState>();
            machine.SetInitialState<NoopState>();
            await machine.RunAsync();
        }

        Assert.Equal(1, state.DisposedCount);
    }
}
