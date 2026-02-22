# AsyncStateMachine
async/await based State Machine for Unity and .NET

[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)

## 概要

AsyncStateMachineは非同期ループで動作するUnity/.NET向けのステートマシンライブラリです。
ステートマシンの実行は非同期で行われ、CancellationTokenによる制御をベースに設計されています。

## インストール

AsyncStateMachineは .NET Standard 2.1 以上をサポートしています。パッケージはNuGetから入手することが可能です。

### .NET CLI

```ps1
dotnet add package async-state-machine
```

### Package Manager

```ps1
Install-Package async-state-machine
```

### Unity

NuGetForUnityを使用することでUnityでもインストールすることが可能です。詳細は[Unity](#unity-1)の項目を参照してください。

## クイックスタート

ステートマシンを利用するには、ステート内で参照したいコンテキストのインスタンスと、利用するStateを内部で生成するStateFactoryが必要になります。

StateFactoryのインスタンスを生成したら、遷移可能なステートとして動作させたいStateクラスを渡すデリゲートを `StateFactory.Register()` メソッドを用いて指定します。

必要なStateのデリゲートをStateFactoryに登録し終えたら、`StateMachine.Create(context, factory)` を用いてステートマシンを生成します。

あとは最初に動作するStateクラスを指定したら、`StateMachine.RunAsync()` を呼ぶだけです。
```cs
using AsyncStateMachine;

class Foo {}
class BarState : State
{
	protected override async ValueTask OnExecuteAsync(Foo context, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			Console.WriteLine("BarState.OnExecuteAsync");
			await Task.Delay(TimeSpan.FromSeconds(1));
		}
	}
}


// 使用するコンテキストクラスの生成
var context = new Foo();

// StateFactoryを生成する
var factory = new StateFactory();

// 遷移させたいStateクラスを渡すデリゲートを指定する（DIと連携も可能）
factory.Register(() => new BarState());

// コンテキストとファクトリーを引数に渡してインスタンスを生成する
var stateMachine = StateMachine.Create(context, factory);

// 最初に起動させるStateクラスを指定する
stateMachine.SetInitialState<BarState>();

// 実行
await stateMachine.RunAsync(cancellationToken);
```

### Stateの切り替えについて

AsyncStateMachineでは、ForceTransition、TryTransitionという二つのメソッドを用いて、動作するステートを切り替えることができます。

ForceTransitionはその名の通り、強制的にそのステートに遷移させます。

TryTransitionは指定したStateが持つCanBeTransitionメソッドを呼び出し、遷移可能であればそのステートに遷移させます。

```cs
async ValueTask OnExecuteAsync(CancellationToken ct)
{
	while (!ct.IsCancellationRequested)
	{
		// stateMachineにはnullableな変数でアクセスできる
		// 指定したStateのCanBeTransitionがTrueなら遷移する
		if (stateMachine?.TryTransiton<BazState>() == true)
		{
			break;
		}
		
		// こちらは遷移先のCanBeTransitonは呼ばない
		stateMachine?.ForceTransition<BarState>();
		break;
	}
}
```

ここで注意なのが、ForceTransition、TryTransitionメソッドを呼んだからといって、即そのフレームに遷移するわけではありません。
次のStateにいつ切り替わるかも全て、子であるStateが制御します。そのため、基本的にはTransitionメソッドを呼び出したあとは上記のようなwhileループを抜けるためにbreakを忘れないでください。

## なぜ非同期なのか？

主にUnity環境での動作を目的としたStateMachineは、いわゆるTickベースの同期的なStateMachineがほとんどです。

それらのStateにはほとんどがEnter、Update、Exitなどの状態に合わせたメソッドが提供され、それらが親であるStateMachineから適切に呼ばれます。

しかし、このパターンには問題点がいくつか存在します。

それは、ステートが動作を開始してから終了するまでの寿命をステートが制御できない点と、例外処理の制御フローが親であるステートマシンに支配されてしまう点です。

まず、一つ目のポイントですが、同期的なステートマシンの多くは親であるStateMachineにUpdateメソッドがあり、これを `MonoBehaviour.Update` などの定期的に呼ばれるメソッド内で呼び出します。

これは一見理にかなっているようにも見えますが、子であるStateからするといつ自身の更新処理が呼ばれなくなるのかがわかりません。

親の更新処理がいつ止まるかわからない状況を前提におきながら設計しなければなりません。（この前提は同期的FSMを使う上では当たり前だとは思います）

また、例外処理を正しく制御するのも難しいです。各状態メソッド（Enter、Update、Exit）内で発生したものをそのメソッド内で制御する場合は問題ないですが、

Update内で例外が投げられExit内の処理を読んでほしい場合、Stateクラスがその挙動を制御することはできず、

親であるStateMachine側でUpdate中の例外をcatchしたらfinallyスコープでExitを呼ぶように設計されていないといけません。

```cs

void Update()
{
	try
	{
		currentScene.Update();
	}
	catch ()
	{
	}
	finally
	{
		// 親であるStateMachine側によって子のStateの終了処理を呼ぶ場所が固定されてしまう
		currentScene.Exit();
	}
}

```

また、この逆を別のStateクラスでやりたいと思っても、やはり親の設計に左右されてしまうため不可能となってしまいます。

このAsyncStateMachineは、この問題をasync/awaitとtry-catch-finallyを用いて解決しています。

本設計では、親はRunAsyncの中で、基本的にはただ子のExecuteAsyncを呼ぶだけになっており、

子のExecuteAsyncがいつ終わるのかは、小自身が制御します。Enter,Update、Exitのように処理が分かれてもいません。

子の処理をどう呼ぶかは子自身が制御します。そうすることで先ほどの問題を全て解決することが可能です。

```cs
async ValueTask ExecuteAsync(CancellationToken ct)
{
  // ステート開始時にやりたい処理はここに

  try
  {
    // ループさせたい場合はwhileループを
    while (!ct.IsCancellationRequested)
    {
    }

    // 例外未発生時のみやりたい終了処理はここに
  }
  catch (OperationCanceledException)
  {
  }
  finally
  {
    // 例外が出ても終了時にやりたい処理はここに
  }
}

```

async/awaiとtry-catch-finallyを用いることで、子のStateのExecuteAsyncの中身にそのStateの全ての動作が記述されており、処理順も明確になりシンプルになったのがお分かりでしょうか。

実際の処理の中はasync/awaitによって手続き的に記述することができ、処理の流れもハッキリするのが本設計の強みです。

また、例外発生時も自身で行うため、この例外が発生した場合は処理する、この例外が発生した場合は無視する、といったことも全て自身で制御できます。

