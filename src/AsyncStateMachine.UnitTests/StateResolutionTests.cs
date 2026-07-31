namespace AsyncStateMachine.UnitTests;

public class StateResolutionTests
{
    static IStateMachine<TestContext> CreateMachine(TestContext? context = null, Action<StateFactory<TestContext>>? configure = null)
    {
        var factory = new StateFactory<TestContext>();
        configure?.Invoke(factory);
        return StateMachine.Create(context ?? new TestContext(), factory);
    }

    [Fact]
    public void At_未登録の型は例外になる()
    {
        using var machine = CreateMachine();

        Assert.Throws<InvalidOperationException>(() => machine.At<NoopState>());
        Assert.Throws<InvalidOperationException>(() => machine.At(typeof(NoopState)));
    }

    [Fact]
    public void At_同じ型なら同じインスタンスを返す()
    {
        using var machine = CreateMachine(configure: f => f.Register(() => new NoopState()));

        Assert.Same(machine.At<NoopState>(), machine.At<NoopState>());
    }

    [Fact]
    public void At_ジェネリック版とType版は同じインスタンスを返す()
    {
        using var machine = CreateMachine(configure: f => f.Register(() => new NoopState()));

        Assert.Same(machine.At<NoopState>(), machine.At(typeof(NoopState)));
    }

    [Fact]
    public void At_生成時にOnInitializeが一度だけ呼ばれる()
    {
        using var machine = CreateMachine(configure: f => f.Register(() => new NoopState()));

        var state = machine.At<NoopState>();
        machine.At<NoopState>();

        Assert.Equal(1, state.InitializedCount);
    }

    [Fact]
    public void Initialize_登録済みの全ステートを事前生成する()
    {
        using var machine = CreateMachine(configure: f =>
        {
            f.Register(() => new NoopState());
            f.Register(() => new BlockedState());
        });

        machine.Initialize();

        Assert.Equal(1, machine.At<NoopState>().InitializedCount);
        Assert.Equal(1, machine.At<BlockedState>().InitializedCount);
    }

    /// <summary>
    /// 回帰: 以前は Initialize() が state.GetType() を、Register() が typeof(TState) をキーにしていたため、
    /// ファクトリが派生型を返すとキーが食い違い、At&lt;TState&gt;() でインスタンスが二重生成されていた。
    /// </summary>
    [Fact]
    public void Initialize_ファクトリが派生型を返してもインスタンスは二重生成されない()
    {
        var created = 0;
        using var machine = CreateMachine(configure: f => f.Register<NoopState>(() =>
        {
            created++;
            return new DerivedNoopState();
        }));

        machine.Initialize();
        machine.At<NoopState>();

        Assert.Equal(1, created);
    }

    /// <summary>
    /// 回帰: 以前は Initialize() が既存エントリを無条件に上書きしていたため、
    /// At&lt;T&gt;() を先に呼んでいると別インスタンスに差し替わっていた。
    /// </summary>
    [Fact]
    public void Initialize_At呼び出し後でも既存インスタンスを維持する()
    {
        using var machine = CreateMachine(configure: f => f.Register(() => new NoopState()));

        var first = machine.At<NoopState>();
        machine.Initialize();

        Assert.Same(first, machine.At<NoopState>());
        Assert.Equal(1, first.InitializedCount);
    }

    /// <summary>回帰: 以前は Initialize() にだけ実行中ガードがなかった。</summary>
    [Fact]
    public async Task Initialize_実行中は例外になる()
    {
        using var machine = CreateMachine(configure: f => f.Register(() => new BlockingState()));
        machine.SetInitialState<BlockingState>();

        var run = machine.RunAsync();
        await machine.At<BlockingState>().Started;

        Assert.Throws<InvalidOperationException>(machine.Initialize);

        machine.Cancel();
        await run;
    }

    [Fact]
    public async Task SetInitialState_実行中は例外になる()
    {
        using var machine = CreateMachine(configure: f =>
        {
            f.Register(() => new BlockingState());
            f.Register(() => new NoopState());
        });
        machine.SetInitialState<BlockingState>();

        var run = machine.RunAsync();
        await machine.At<BlockingState>().Started;

        Assert.Throws<InvalidOperationException>(machine.SetInitialState<NoopState>);

        machine.Cancel();
        await run;
    }

    [Fact]
    public void ForceTransition_実行していないときは例外になる()
    {
        using var machine = CreateMachine(configure: f => f.Register(() => new NoopState()));

        Assert.Throws<InvalidOperationException>(machine.ForceTransition<NoopState>);
        Assert.Throws<InvalidOperationException>(() => machine.ForceTransition(typeof(NoopState)));
    }

    [Fact]
    public void TryTransition_実行していないときは例外になる()
    {
        using var machine = CreateMachine(configure: f => f.Register(() => new NoopState()));

        Assert.Throws<InvalidOperationException>(() => machine.TryTransition<NoopState>());
        Assert.Throws<InvalidOperationException>(() => machine.TryTransition(typeof(NoopState)));
    }
}
