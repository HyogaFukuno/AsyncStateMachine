namespace AsyncStateMachine.UnitTests;

public class StateFactoryTests
{
    [Fact]
    public void Register_同じ型を二度登録すると例外になる()
    {
        var factory = new StateFactory<TestContext>();
        factory.Register(() => new NoopState());

        Assert.Throws<InvalidOperationException>(() => factory.Register(() => new NoopState()));
    }

    [Fact]
    public void Replace_登録済みの型を差し替えられる()
    {
        var factory = new StateFactory<TestContext>();
        factory.Register(() => new NoopState());

        var expected = new NoopState();
        factory.Replace(() => expected);

        using var machine = StateMachine.Create(new TestContext(), factory);
        Assert.Same(expected, machine.At<NoopState>());
    }

    [Fact]
    public void Replace_未登録の型でも登録できる()
    {
        var factory = new StateFactory<TestContext>();

        var expected = new NoopState();
        factory.Replace(() => expected);

        using var machine = StateMachine.Create(new TestContext(), factory);
        Assert.Same(expected, machine.At<NoopState>());
    }

    /// <summary>
    /// 回帰: 以前は StateMachine がファクトリへの参照を保持していたため、
    /// 生成後にファクトリが破棄・変更されると遅延生成が壊れていた。
    /// </summary>
    [Fact]
    public async Task Create_後にファクトリを変更してもステートマシンは影響を受けない()
    {
        var factory = new StateFactory<TestContext>();
        factory.Register(() => new TransitionToNoopState());
        factory.Register(() => new NoopState());

        var context = new TestContext();
        var machine = StateMachine.Create(context, factory);
        machine.SetInitialState<TransitionToNoopState>();

        // 生成後の上書きは、すでに生成済みのステートマシンへ波及してはならない。
        factory.Replace<NoopState>(() => throw new InvalidOperationException("使われてはいけない"));

        await machine.RunAsync();

        Assert.Equal(["TransitionToNoopState.Execute", "NoopState.Execute"], context.Log);
    }
}
