using System;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncStateMachine;

/// <summary>
/// ステートマシン上で一つの状態として動作する基底クラス。
/// </summary>
/// <remarks>
/// 状態の開始から終了までを <see cref="OnExecuteAsync"/> 一つの中に記述する。
/// いつ処理を終えるか、例外発生時に何をするかは、すべてこのクラスの実装側が制御する。
/// </remarks>
/// <typeparam name="TContext">ステート間で共有するコンテキストの型。</typeparam>
public abstract class State<TContext> : IDisposable
{
    /// <summary>
    /// 自身を保持しているステートマシン。<see cref="OnInitialize"/> が呼ばれる前は <c>null</c>。
    /// </summary>
    protected IStateHost<TContext>? stateMachine;

    bool disposed;

    /// <summary>
    /// このステートへ遷移してよいかを返す。
    /// </summary>
    /// <remarks>
    /// <see cref="IStateHost{TContext}.TryTransition{TState}"/> から遷移先の判定に使われる。
    /// <see cref="IStateHost{TContext}.ForceTransition{TState}"/> では呼ばれない。
    /// </remarks>
    /// <param name="context">共有コンテキスト。</param>
    /// <returns>遷移を許可する場合は <c>true</c>。既定では常に <c>true</c>。</returns>
    protected internal virtual bool CanBeTransition(TContext context) => true;

    /// <summary>
    /// ステートのインスタンスが生成された直後に一度だけ呼ばれる。
    /// </summary>
    /// <param name="context">共有コンテキスト。</param>
    protected virtual void OnInitialize(TContext context) { }

    /// <summary>
    /// このステートの処理本体。ここから戻った時点でステートは終了する。
    /// </summary>
    /// <remarks>
    /// <paramref name="ct"/> は、ステートマシンの停止時と、次のステートへ遷移して本メソッドから戻った時にキャンセルされる。
    /// 破棄処理を正しく完了させるため、実装側では必ずこのトークンを尊重すること。
    /// </remarks>
    /// <param name="context">共有コンテキスト。</param>
    /// <param name="ct">このステートの実行に紐づくキャンセルトークン。</param>
    protected virtual ValueTask OnExecuteAsync(TContext context, CancellationToken ct) => default;

    /// <summary>
    /// ステートが破棄されるときに一度だけ呼ばれる。
    /// </summary>
    /// <remarks>
    /// ステートマシンが実行中に破棄された場合でも、<see cref="OnExecuteAsync"/> から巻き戻ったあとに呼ばれる。
    /// 一つのステートの失敗で他のステートの破棄が止まらないよう、
    /// このメソッドが送出した例外はステートマシン側で握り潰される点に注意すること。
    /// </remarks>
    protected virtual void OnDispose() { }

    internal void Initialize(IStateHost<TContext> machine, TContext context)
    {
        stateMachine = machine;
        OnInitialize(context);
    }

    // async でラップせずそのまま返すことで、遷移ごとの非同期ステートマシン生成を1つ分減らす。
    internal ValueTask ExecuteAsync(TContext context, CancellationToken ct) => OnExecuteAsync(context, ct);

    /// <summary>
    /// このステートを破棄し、<see cref="OnDispose"/> を呼ぶ。二度目以降の呼び出しは何もしない。
    /// </summary>
    public void Dispose()
    {
        if (disposed) { return; }

        disposed = true;
        OnDispose();
        GC.SuppressFinalize(this);
    }
}
