using System;

namespace AsyncStateMachine;

/// <summary>
/// ステートから見た、自身をホストしているステートマシン。遷移操作のみを公開する。
/// </summary>
/// <remarks>
/// 遷移メソッドは呼び出した時点では切り替えを行わない。
/// 現在のステートが <see cref="State{TContext}.OnExecuteAsync"/> から戻った時点で、要求された遷移先が実行される。
/// </remarks>
/// <typeparam name="TContext">ステート間で共有するコンテキストの型。</typeparam>
public interface IStateHost<TContext>
{
    /// <summary>
    /// 次に実行するステートが確定しているかどうかを返す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ForceTransition{TState}"/> または <see cref="TryTransition{TState}"/> が成功すると <c>true</c> になり、
    /// 現在のステートが <see cref="State{TContext}.OnExecuteAsync"/> から戻って遷移先が実行され始めた時点で <c>false</c> に戻る。
    /// </para>
    /// <para>
    /// 遷移を要求したのが自分自身か外部かを問わないため、実行中のステートは
    /// このプロパティを見るだけで自身のループを抜けられる。
    /// メインのステートマシンからサブのステートマシンへ遷移を要求する場合など、
    /// 遷移の要求元とループの制御元が別になるケースで使う。
    /// </para>
    /// <para>
    /// <see cref="IStateMachine{TContext}.SetInitialState{TState}"/> で指定した初期ステートも遷移先として扱われるため、
    /// 実行開始前は <c>true</c> になる。破棄済みの場合は例外を送出せず <c>false</c> を返す。
    /// </para>
    /// </remarks>
    bool IsTransitionRequested { get; }

    /// <summary>
    /// 遷移先の <see cref="State{TContext}.CanBeTransition"/> を問わず、次のステートを指定する。
    /// </summary>
    /// <typeparam name="TState">遷移先のステートの型。</typeparam>
    /// <exception cref="InvalidOperationException">ステートマシンが実行中でない場合、または遷移先が未登録の場合。</exception>
    /// <exception cref="ObjectDisposedException">ステートマシンが破棄済みの場合。</exception>
    void ForceTransition<TState>() where TState : State<TContext>;

    /// <inheritdoc cref="ForceTransition{TState}"/>
    /// <param name="stateType">遷移先のステートの型。</param>
    void ForceTransition(Type stateType);

    /// <summary>
    /// 遷移先の <see cref="State{TContext}.CanBeTransition"/> が <c>true</c> の場合のみ、次のステートを指定する。
    /// </summary>
    /// <typeparam name="TState">遷移先のステートの型。</typeparam>
    /// <returns>遷移先を設定した場合は <c>true</c>、拒否された場合は <c>false</c>。</returns>
    /// <exception cref="InvalidOperationException">ステートマシンが実行中でない場合、または遷移先が未登録の場合。</exception>
    /// <exception cref="ObjectDisposedException">ステートマシンが破棄済みの場合。</exception>
    bool TryTransition<TState>() where TState : State<TContext>;

    /// <inheritdoc cref="TryTransition{TState}"/>
    /// <param name="stateType">遷移先のステートの型。</param>
    bool TryTransition(Type stateType);
}
