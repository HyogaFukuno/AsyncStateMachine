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
