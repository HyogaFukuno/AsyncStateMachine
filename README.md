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

同じ型を二度 `Register()` すると例外になります。テスト用のモックに差し替えるなど、意図的に既存の登録を上書きしたい場合は `StateFactory.Replace()` を使ってください。

必要なStateのデリゲートをStateFactoryに登録し終えたら、`StateMachine.Create(context, factory)` を用いてステートマシンを生成します。

あとは最初に動作するStateクラスを指定したら、`StateMachine.RunAsync()` を呼ぶだけです。
```cs
using AsyncStateMachine;

// Stateクラス内で使用したいコンテキストクラス。
class Foo {}

// 一つの状態として動作するStateクラス。
// State<T>に使用するコンテキストクラスの型を指定することでメソッドの引数からインスタンスを参照できます。
class BarState : State<Foo>
{
	protected override async ValueTask OnExecuteAsync(Foo context, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			Console.WriteLine("BarState.OnExecuteAsync");
			await Task.Delay(TimeSpan.FromSeconds(1), ct);
		}
	}
}


// 使用するコンテキストクラスの生成
var context = new Foo();

// StateFactoryを生成する
var factory = new StateFactory<Foo>();

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
protected override async ValueTask OnExecuteAsync(Foo context, CancellationToken ct)
{
	while (!ct.IsCancellationRequested)
	{
		// 親のステートマシンには stateMachine (IStateHost<T>) からアクセスできる
		// nullableなので ?. で呼び出す
		// 指定したStateのCanBeTransitionがTrueなら遷移する
		if (stateMachine?.TryTransition<BazState>() == true)
		{
			break;
		}
		
		// こちらは遷移先のCanBeTransitionは呼ばない
		stateMachine?.ForceTransition<BarState>();
		break;
	}
}
```

ここで注意なのが、ForceTransition、TryTransitionメソッドを呼んだからといって、即そのフレームに遷移するわけではありません。
次のStateにいつ切り替わるかも全て、子であるStateが制御します。そのため、基本的にはTransitionメソッドを呼び出したあとは上記のようなwhileループを抜けるためにbreakを忘れないでください。

### ステートの生成タイミング

ステートのインスタンスは、そのステートへ初めて遷移した時点で生成され、`OnInitialize` が呼ばれます。特に何もしなくてもこの遅延生成で動作します。

`Initialize()` を呼ぶと、登録済みの全ステートをその場でまとめて生成できます。呼び出しは任意ですが、**生成と `OnInitialize` の実行タイミングを初回遷移時から任意の時点へ前倒しできる**ため、ロード画面などで事前に呼んでおくと、ゲームプレイ中に発生するアロケーションを避けられます。

```cs
// ロード画面など、多少のコストを払ってよい場所で呼んでおく
stateMachine.Initialize();

// 以降の遷移ではステートの生成が発生しない
await stateMachine.RunAsync(ct);
```

### ライフサイクル（実行・キャンセル・破棄）

`RunAsync` は「遷移先のStateがなくなったとき」「CancellationTokenがキャンセルされたとき」「State内で例外が送出されたとき」のいずれかで終了します。

いずれの場合も終了時点で実行状態は解除されるため、同じインスタンスに対して再度 `SetInitialState` と `RunAsync` を呼び直すことができます。

キャンセル時に `RunAsync` が `OperationCanceledException` を送出するかどうかは、**State側の実装次第**である点に注意してください。本ライブラリはStateが投げた例外に手を加えず、そのまま `RunAsync` の呼び出し元へ伝播させます。したがって、State内で `catch (OperationCanceledException)` して握り潰していれば `RunAsync` は正常終了し、握り潰していなければ例外が飛びます。

`Cancel()` はキャンセルを要求するだけで、実行状態の解除は `RunAsync` 側が行います。そのため、`Cancel()` を呼んだあとに次の実行を組み立てる場合は、必ず `RunAsync` の完了を待ってください。

```cs
var run = stateMachine.RunAsync(ct);

stateMachine.Cancel();
await run;                              // ここまで待ってから次を組む

stateMachine.SetInitialState<BarState>();
await stateMachine.RunAsync(ct);
```

破棄には `Dispose()` と `DisposeAsync()` の二つがあり、どちらも生成済みの全Stateの `OnDispose()` を呼びます。破棄後のステートマシンは再利用できず、以降の操作は `ObjectDisposedException` になります。

両者の違いは **実行中に呼んだ場合の待ち方** です。

Stateの破棄は、必ず実行中のStateが `ExecuteAsync` から巻き戻ってから行われます。これは、まだ動いているStateに対して `OnDispose()` が先に走り、後始末で解放したはずのリソースを本体が触ってしまう事故を避けるためです。

`DisposeAsync()` はこの巻き戻りの完了までを待ちます。破棄が終わったことを保証したい場合はこちらを使ってください。

```cs
await using var stateMachine = StateMachine.Create(context, factory);
```

いっぽう `Dispose()` はキャンセルを要求するだけで待たずに戻ります。実際のStateの破棄は `RunAsync` が巻き戻った時点で行われるため、`Dispose()` から戻った直後にはまだ `OnDispose()` が呼ばれていません。Unityの `MonoBehaviour.OnDestroy()` のように非同期に待てない場所ではこちらを使いますが、破棄の完了を待ちたい場合は `DisposeAsync()` を選んでください。

なお、Stateが `CancellationToken` を無視して動き続ける実装になっていると、巻き戻りが起きないため破棄も完了しません。ステート内では必ずトークンを尊重してください。

### スレッド安全性

本ライブラリはスレッドセーフではありません。単一のステートマシンに対する操作（`RunAsync` / `Cancel` / 遷移メソッド / `Dispose`）は、すべて同一のスレッドから行ってください。Unityであればメインスレッドから扱うことを想定しています。

なお `StateFactory` の登録内容は `StateMachine.Create()` の時点でコピーされます。そのためステートマシンの生成後は、ファクトリを破棄しても、`Replace()` で登録を差し替えても、生成済みのステートマシンには影響しません。

## Unity

UnityではNuGetForUnityを用いて`async-state-machine`をインストールしてください。動作には .NET Standard 2.1 が必要です。

### UniTaskとの併用

本ライブラリのAPIは `ValueTask` に統一されていますが、UniTaskと問題なく併用できます。

まず、`OnExecuteAsync` の中では `await UniTask.Yield()` や `await UniTask.Delay()` をそのまま書けます。`async ValueTask` メソッドであっても、UniTaskのawaiterはそのまま機能します。

このとき発生するステートマシンのボックス化は **最初のawaitの1回だけ** で、ループの反復ごとには発生しません。またこの境界はステート遷移ごとにしか通らないため、毎フレームのコストにはなりません。

```cs
class BarState : State<Foo>
{
	protected override async ValueTask OnExecuteAsync(Foo context, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			await UniTask.Yield();
		}
	}
}
```

そのうえでステート本体までUniTaskのアロケーションフリーな実行基盤に載せたい場合は、`UniTask.AsValueTask()` を用いて境界だけを変換してください。Unity 2022.3以降であればこの変換にコストはかかりません（UniTask v2.5.1以降）。

```cs
class BarState : State<Foo>
{
	protected override ValueTask OnExecuteAsync(Foo context, CancellationToken ct)
		=> ExecuteCoreAsync(context, ct).AsValueTask();

	async UniTask ExecuteCoreAsync(Foo context, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			await UniTask.Yield();
		}
	}
}
```

`RunAsync` の戻り値をUniTaskとして扱いたい場合は `ValueTask.AsUniTask()` が利用できます。

```cs
stateMachine.RunAsync(ct).AsUniTask().Forget();
```

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

子のExecuteAsyncがいつ終わるのかは、子自身が制御します。Enter,Update、Exitのように処理が分かれてもいません。

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

async/awaitとtry-catch-finallyを用いることで、子のStateのExecuteAsyncの中身にそのStateの全ての動作が記述されており、処理順も明確になりシンプルになったのがお分かりでしょうか。

実際の処理の中はasync/awaitによって手続き的に記述することができ、処理の流れもハッキリするのが本設計の強みです。

また、例外発生時も自身で行うため、この例外が発生した場合は処理する、この例外が発生した場合は無視する、といったことも全て自身で制御できます。

このことから、**本ライブラリはState主導のStateMachineである** と言えます。

## 本ライブラリで解決できない問題

本ライブラリでは、async/awaitを用いて非同期に状態ロジックを実装・制御することができますが、それゆえに対応できない問題があります。

それは、TickベースのStateMachineに比べて **状態の保存・復元が難しい** 点です。

TickベースのStateMachineでは毎フレームUpdate関数などが呼ばれ、引数にはDeltaTimeが渡されるため、特定のタイミングのSnapshotを保存することが可能です。

しかし、async/awaitを用いたパターンでは完全に特定のタイミングのSnapshotを保存することはできません。async/awaitで記述された非同期処理がどこまで進んだかを把握する術がなく、また非同期処理を任意のタイミングから復元し実行する術も用意されていません。そのため、本ライブラリでそのような処理を実装する場合は非同期処理の進捗状況を除いたStateクラスのメンバ変数の値を保存・復元することしかできません。

非同期処理の進捗状況を保存・復元するための変数を用意することでその値を用いてそれまでの非同期処理をスキップし、それ以降の非同期処理を続行する...ということもできますが、多くの場合、TickベースのStateMachineを使用することをお勧めします。

また、StateMachineを用いて解決したい仕様がState駆動出ない場合も、本ライブラリでは解決するのが難しい問題です。
