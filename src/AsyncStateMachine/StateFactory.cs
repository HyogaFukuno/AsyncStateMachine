using System;
using System.Collections.Generic;

namespace AsyncStateMachine;

/// <summary>
/// ステートマシンが使用するステートの生成方法を登録するファクトリ。
/// </summary>
/// <remarks>
/// 登録内容は <see cref="StateMachine.Create{TContext}"/> の時点でコピーされる。
/// 生成後にこのインスタンスへ加えた変更は、既に生成されたステートマシンには影響しない。
/// </remarks>
/// <typeparam name="TContext">ステート間で共有するコンテキストの型。</typeparam>
public sealed class StateFactory<TContext>
{
    internal readonly Dictionary<Type, Func<State<TContext>>> factories = [];

    /// <summary>
    /// ステートの生成方法を登録する。同じ型が登録済みの場合は例外になる。
    /// </summary>
    /// <remarks>
    /// 登録のキーには <typeparamref name="TState"/> が使われる。
    /// デリゲート経由で生成するため、DIコンテナから解決したインスタンスを返すこともできる。
    /// 既存の登録を意図的に差し替える場合は <see cref="Replace{TState}"/> を使うこと。
    /// </remarks>
    /// <typeparam name="TState">登録するステートの型。</typeparam>
    /// <param name="factory">ステートのインスタンスを返すデリゲート。</param>
    /// <exception cref="InvalidOperationException">同じ型が既に登録されている場合。</exception>
    public void Register<TState>(Func<TState> factory) where TState : State<TContext>
    {
        if (factories.ContainsKey(typeof(TState)))
        {
            throw new InvalidOperationException($"{typeof(TState).Name} is already registered. Use Replace if this is intentional.");
        }

        factories[typeof(TState)] = factory;
    }

    /// <summary>
    /// ステートの生成方法を登録する。同じ型が登録済みであれば差し替える。
    /// </summary>
    /// <remarks>
    /// 未登録の型に対して呼んだ場合は、<see cref="Register{TState}"/> と同じく単に登録される。
    /// </remarks>
    /// <typeparam name="TState">登録するステートの型。</typeparam>
    /// <param name="factory">ステートのインスタンスを返すデリゲート。</param>
    public void Replace<TState>(Func<TState> factory) where TState : State<TContext>
        => factories[typeof(TState)] = factory;
}
