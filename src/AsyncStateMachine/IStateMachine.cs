using System;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncStateMachine;

/// <summary>
/// 非同期に動作するステートマシン。
/// </summary>
/// <remarks>
/// このインターフェースはスレッドセーフではない。一つのインスタンスに対する操作は同一スレッドから行うこと。
/// </remarks>
/// <typeparam name="TContext">ステート間で共有するコンテキストの型。</typeparam>
public interface IStateMachine<TContext> : IStateHost<TContext>, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// 現在実行中のステートの型を返す。実行中でない場合は <c>null</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 返すのは <see cref="StateFactory{TContext}"/> に登録した型であり、ファクトリが派生型のインスタンスを返した場合でも
    /// インスタンスの実際の型ではなく登録時の型になる。そのため、返された型はそのまま
    /// <see cref="At(Type)"/> や <see cref="IStateHost{TContext}.TryTransition(Type)"/> に渡せる。
    /// </para>
    /// <para>
    /// 遷移先のステートの実行が始まった時点で切り替わる。遷移を要求しただけでは変わらないため、
    /// 要求済みの遷移先の有無は <see cref="IStateHost{TContext}.IsTransitionRequested"/> で確認すること。
    /// </para>
    /// <para>
    /// <see cref="SetInitialState{TState}"/> を呼んだだけでは <c>null</c> のままで、<see cref="RunAsync"/> の終了時にも <c>null</c> へ戻る。
    /// 破棄済みでも例外は送出しない。実行中に破棄した場合は、実行中のステートが巻き戻った時点で <c>null</c> になる。
    /// </para>
    /// </remarks>
    Type? CurrentStateType { get; }

    /// <summary>
    /// 登録済みの全ステートを事前に生成する。呼び出しは任意で、未生成のステートは遷移時に生成される。
    /// </summary>
    /// <remarks>
    /// ステートの生成と <see cref="State{TContext}.OnInitialize"/> の実行タイミングを、
    /// 初回の遷移時から任意の時点へ前倒しするためのメソッド。
    /// ロード画面などで事前に呼んでおくことで、ゲームプレイ中に発生するアロケーションを避けられる。
    /// </remarks>
    /// <exception cref="InvalidOperationException">ステートマシンが実行中の場合。</exception>
    /// <exception cref="ObjectDisposedException">ステートマシンが破棄済みの場合。</exception>
    void Initialize();

    /// <summary>
    /// <see cref="RunAsync"/> で最初に実行するステートを指定する。
    /// </summary>
    /// <typeparam name="TState">最初に実行するステートの型。</typeparam>
    /// <exception cref="InvalidOperationException">ステートマシンが実行中の場合、または指定した型が未登録の場合。</exception>
    /// <exception cref="ObjectDisposedException">ステートマシンが破棄済みの場合。</exception>
    void SetInitialState<TState>() where TState : State<TContext>;

    /// <summary>
    /// 指定した型のステートのインスタンスを取得する。未生成であればこの時点で生成する。
    /// </summary>
    /// <typeparam name="TState">取得するステートの型。</typeparam>
    /// <exception cref="InvalidOperationException">指定した型が未登録の場合。</exception>
    /// <exception cref="ObjectDisposedException">ステートマシンが破棄済みの場合。</exception>
    TState At<TState>() where TState : State<TContext>;

    /// <inheritdoc cref="At{TState}"/>
    /// <param name="stateType">取得するステートの型。</param>
    State<TContext> At(Type stateType);

    /// <summary>
    /// ステートマシンを開始し、遷移先がなくなるまで実行し続ける。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「遷移先のステートがなくなったとき」「<paramref name="ct"/> がキャンセルされたとき」
    /// 「ステート内で例外が送出されたとき」のいずれかで終了する。
    /// </para>
    /// <para>
    /// ステートが送出した例外には手を加えず、そのまま呼び出し元へ伝播させる。
    /// そのためキャンセル時に <see cref="OperationCanceledException"/> が送出されるかどうかは、
    /// ステート側がそれを捕捉しているかに依存する。
    /// </para>
    /// <para>
    /// 終了時にはいずれの経路でも実行状態が解除されるため、同じインスタンスで再度実行できる。
    /// </para>
    /// </remarks>
    /// <param name="ct">ステートマシン全体を停止させるためのキャンセルトークン。</param>
    /// <exception cref="InvalidOperationException">すでに実行中の場合。</exception>
    /// <exception cref="ObjectDisposedException">ステートマシンが破棄済みの場合。</exception>
    ValueTask RunAsync(CancellationToken ct = default);

    /// <summary>
    /// 実行中のステートマシンにキャンセルを要求する。
    /// </summary>
    /// <remarks>
    /// キャンセルを要求するだけで完了は待たない。実行状態の解除は <see cref="RunAsync"/> 側が行うため、
    /// 続けて次の実行を組み立てる場合は <see cref="RunAsync"/> の完了を待つこと。
    /// </remarks>
    /// <exception cref="ObjectDisposedException">ステートマシンが破棄済みの場合。</exception>
    void Cancel();
}
