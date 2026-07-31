# 変更履歴

本ファイルは [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に従い、
バージョニングは [セマンティック バージョニング](https://semver.org/lang/ja/) に従います。

## [2.0.0] - 2026-07-31

`1.0.2` に含まれていた複数の不具合修正と、それに伴うAPIの整理を行ったメジャーリリースです。

### 破壊的変更

- **`StateMachine<TContext>` を `internal` に変更しました。** インスタンスの生成は新設した静的クラス `StateMachine` の `StateMachine.Create(context, factory)` を使用してください。従来の `StateMachine<TContext>.Create(...)` は利用できません。
- **`StateFactory<TContext>` から `IDisposable` を削除しました。** 破棄すべきリソースを持たないうえ、`using` で囲むとステートマシンが壊れる導線になっていたためです（詳細は「修正」を参照）。
- **`IStateMachine<TContext>` に `At(Type)` と `IAsyncDisposable` が追加されました。** このインターフェースを独自に実装している場合は対応が必要です。
- **`State<TContext>.Initialize()` と `State<TContext>.ExecuteAsync()` を `internal` に変更しました。** ステートマシンを介さない直接呼び出しを防ぐためです。派生クラスの実装には影響しません。
- **破棄後のステートマシンは再利用できなくなりました。** `Dispose()` / `DisposeAsync()` 後の操作は `ObjectDisposedException` を送出します。
- **`IReadOnlyStateMachine<TContext>` を `IStateHost<TContext>` に改名しました。** 遷移操作しか持たないインターフェースであり、`ReadOnly` という名前が実態と食い違っていたためです。`State<TContext>` の `stateMachine` フィールドの型もこれに合わせて変わります。
- **`StateFactory<TContext>.Register()` の `isOverride` 引数を廃止し、`Register()` と `Replace()` の2つのメソッドに分割しました。** 既存の登録を差し替える場合は `Replace()` を使用してください。`isOverride: true` は「この登録は上書きである」と読めるにもかかわらず、既存の登録がなくても成功するため、名前と実際の意味がずれていました。
- **`Cancel()` の意味が変わりました。** キャンセルを要求するだけになり、実行状態の解除は `RunAsync` 側が行います。`Cancel()` の直後に次の実行を組み立てる場合は、`RunAsync` の完了を待つ必要があります。

### 追加

- **`IAsyncDisposable` を実装しました。** `DisposeAsync()` は、実行中のStateが `ExecuteAsync` から巻き戻るのを待ってから全Stateを破棄します。`await using` が利用できます。
- **`IStateMachine<TContext>` に `At(Type)` を追加しました。** 実装クラスが `internal` になったため、これがないと非ジェネリック版のAPIが外部から到達不能でした。

### 修正

#### 実行制御

- **実行中に `Cancel()` を呼ぶと `NullReferenceException` が発生する問題を修正しました。** `Cancel()` が `CancellationTokenSource` のフィールドを `null` にしていたため、`await` から復帰した `RunAsync` がループ条件でそれを参照して落ちていました。`RunAsync` 内でローカル変数に退避するよう変更しています。
- **`RunAsync` が一度しか実行できない問題を修正しました。** 正常終了しても実行状態が解除されず、2回目の `RunAsync` が `"StateMachine is already running."`、`SetInitialState` が `"The method can only be called when the StateMachine is not running."` で失敗していました。正常終了・キャンセル・例外のいずれの経路でも実行状態を解除するようにしています。
- **`Initialize()` に実行中ガードを追加しました。** `SetInitialState()` にはあったガードが `Initialize()` にはなく、実行中のステートマシンに新しいStateを差し込めてしまっていました。

#### 破棄とリソース管理

- **`Dispose()` が生成済みのStateを破棄していなかった問題を修正しました。** `State<TContext>` は `IDisposable` を実装し `OnDispose()` フックも提供していましたが、ステートマシン側から一度も呼ばれていませんでした。
- **実行中に破棄すると、走行中のStateに `OnDispose()` が入る問題を修正しました。** キャンセル要求後の非同期の巻き戻りを待たずにStateを破棄していたため、Stateが後始末で解放したリソースを、`await` から復帰した本体が触る可能性がありました。Stateの破棄は必ず `ExecuteAsync` から巻き戻ったあとに行うよう変更しています。
- **破棄後のステートマシンが黙って再利用できてしまう問題を修正しました。** `Dispose()` は破棄済みフラグを持たず、登録情報も残っていたため、`SetInitialState` → `RunAsync` を呼ぶとStateが再生成されて何事もなかったように動作していました。
- **Stateがキャンセルトークンへ登録した後始末の例外が、本来の例外を覆い隠す問題を修正しました。** `CancellationTokenSource.Cancel()` は登録済みコールバックを同期実行し、そこで例外が出ると `AggregateException` を送出します。これが `finally` の中にあったため、Stateが送出した本来の例外を上書きしていました。あわせて `Cancel()` / `Dispose()` / `DisposeAsync()` でも同じ例外が呼び出し元へ漏れないようにしています。特に `DisposeAsync()` では、この例外によって巻き戻りの待機へ到達できず、「破棄の完了を待つ」保証が失われていました。
- **`OnDispose()` が例外を投げると、後続のStateが破棄されない問題を修正しました。** 一つのStateの失敗が他のStateの破棄を止めないよう、破棄経路では例外を握り潰します。
- **`CancellationTokenSource` の破棄漏れを修正しました。** `Cancel()` が `Dispose()` せずに参照を捨てていた点と、遷移ごとに生成される State 用のリンクトークンが破棄されず親に紐付いたまま蓄積していた点の2箇所です。
- **`StateFactory` を破棄するとステートマシンが壊れる問題を修正しました。** `StateFactory` が `IDisposable` だったため `using` で囲む書き方を誘発しますが、ステートマシンはファクトリへの参照を保持して遅延生成に使っていたため、まだ生成されていないStateへ初めて遷移した瞬間に「登録しているのに `Type ... is not registered in the factory`」という誤解を招く例外が出ていました。登録内容は `StateMachine.Create()` の時点でコピーするようにし、生成後はファクトリの寿命や変更に依存しません。

#### ステートの解決

- **`Initialize()` が生成するインスタンスのキーが `Register()` と食い違う問題を修正しました。** `Initialize()` は `state.GetType()` を、`Register()` は `typeof(TState)` をキーにしていたため、ファクトリが派生型を返す場合にキーがずれ、`At<TState>()` でインスタンスが二重生成されていました。登録時の型をキーに統一しています。
- **`At<T>()` を先に呼んでから `Initialize()` を呼ぶと、既存のインスタンスが破棄されずに上書きされる問題を修正しました。**

### 変更

- **UniTask対応の `#if ASYNC_STATE_MACHINE_COMPATIBLE_UNITASK` 分岐を削除し、APIを `ValueTask` に統一しました。** NuGetで配布されるのはコンパイル済みDLLのため、Unity側でシンボルを定義しても分岐は効かず、機能していませんでした。UniTask v2.5.1以降はUnity 2022.3以上で `UniTask.AsValueTask()` によるゼロコスト変換が提供されているため、利用者側が境界で変換すれば同等の性能が得られます。詳細はREADMEの「Unity」を参照してください。
- **`State<TContext>.ExecuteAsync()` の `async`/`await` ラッパーを除去しました。** `OnExecuteAsync` の戻り値をそのまま返すことで、遷移ごとの非同期ステートマシン生成が1つ減ります。
- `At<TState>()` の `ContainsKey` + インデクサによる二重ルックアップを `TryGetValue` に変更しました。

### ドキュメント

- READMEに「ライフサイクル（実行・キャンセル・破棄）」を追加しました。`RunAsync` の終了条件、`Cancel()` の契約、`Dispose()` と `DisposeAsync()` の使い分け、キャンセル時に `OperationCanceledException` が伝播するかはState側の実装次第である点を明記しています。
- READMEに「スレッド安全性」を追加しました。本ライブラリはスレッドセーフではなく、単一のステートマシンへの操作は同一スレッドから行う必要があります。
- READMEに「Unity」を追加しました。従来リンクだけが存在して飛び先がない状態でした。UniTaskとの併用方法を記載しています。
- クイックスタートのコード例が実際にはコンパイルできない状態だった点を修正しました（`new StateFactory()` → `new StateFactory<Foo>()`、`CancellationToken` の渡し漏れ）。
- 誤記を修正しました（`TryTransiton` → `TryTransition`、`CanBeTransiton` → `CanBeTransition`、「小自身」→「子自身」、「async/awai」→「async/await」）。

- 公開APIにXMLドキュメントコメントを整備しました。

### テスト

- xUnit によるテストプロジェクト `src/AsyncStateMachine.UnitTests` を追加しました（49ケース）。本リリースで修正した不具合はすべて回帰テストとして残しています。
- 既存の `src/AsyncStateMachine.Tests` は自動テストではなく動作確認用のコンソールサンプルです。

### パッケージング

- 非推奨の `PackageLicenseUrl` を `PackageLicenseExpression` に置き換えました（警告 `NU5125` の解消）。
- `PackageReadmeFile` を設定し、NuGetのパッケージページにREADMEが表示されるようにしました。
- `PackageProjectUrl` と `PackageTags` を設定しました。
- `GenerateDocumentationFile` を有効にし、XMLドキュメントをパッケージに含めるようにしました。
- `.gitignore` に `.DS_Store` と Rider のローカル設定ファイル（`deployment.xml` / `webServers.xml`）を追加しました。
