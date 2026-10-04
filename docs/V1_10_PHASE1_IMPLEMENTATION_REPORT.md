# Ver1.10 Phase 1 実装報告書

状態：Phase 1完了・ユーザーレビュー待ち（Phase 2未着手）
検証日：2026-09-26

## 結論

Application層の「一定周期で安全に1回の処理を呼び出す骨格」を実装した。Release build成功、全92ケース成功（既存71＋新規21、Fail 0、Skip 0）。CSV自動取込そのものはまだ実装していない。

既存ファイルを編集せず、下記9ファイルのみ追加。Phase 0要件定義書・基本設計書は変更していない。過去の範囲限定方針も確認し、今回指示のApplication基礎とFake試験のみに限定した。

## 1. 追加ファイル

パスはリポジトリルート基準。

| ファイル | 内容 |
|---|---|
| src/ManufacturingDataApp.Application/Services/PeriodicImportService.vb | 開始・固定周期・排他・停止・Task監督 |
| src/ManufacturingDataApp.Application/DTOs/PeriodicImportOptionsDto.vb | WatchIntervalSecondsのみ、初期60秒 |
| src/ManufacturingDataApp.Application/DTOs/PeriodicImportState.vb | 4状態 |
| src/ManufacturingDataApp.Application/DTOs/PeriodicImportStatusDto.vb | 不変の状態スナップショット |
| src/ManufacturingDataApp.Application/Interfaces/IPeriodicImportCycle.vb | 準備と1周期実行の最小抽象 |
| tests/ManufacturingDataApp.Tests/Application/PeriodicImportServiceTests.vb | 新規21ケース |
| tests/ManufacturingDataApp.Tests/TestSupport/ManualTimeProvider.vb | 手動単調時刻・壁時計・タイマー |
| tests/ManufacturingDataApp.Tests/TestSupport/PeriodicImportCycleFake.vb | 準備／実行の制御・実行数・最大並行数 |
| docs/V1_10_PHASE1_IMPLEMENTATION_REPORT.md | 本報告書 |

既存ファイルはSHA-256で変更前後を照合。比較対象から.git・bin・objを除外し、既存ファイルの変更・削除なしを確認した。ビルド生成物bin/objは検証によって更新される。Gitでは開始前から既存一式が未追跡であり、git diffだけを変更範囲の証拠にはしていない。

## 2. 実装契約（指示書の報告項目2～19）

| 項目 | 実装 |
|---|---|
| 2 実装範囲 | Application DTO／Interface、TimeProvider、状態、即時実行、固定周期、排他、スキップ、安全停止、例外観測 |
| 3 未実装 | Phase 2のDB設定・Migration、Phase 3のAtomic／CsvImportService、Phase 4の実ファイル・Journal／Order／修正版・移動、Phase 5のUI／本番DI／ログ。Publishなし |
| 4 状態 | Stopped／Starting／Running／Stoppingのみ。生成時Stopped。IsProcessingは独立フラグ |
| 5 StartAsync | Integer周期を1～604800で検証。不正値はArgumentOutOfRangeException、NothingはArgumentNullException。Stopped以外からの開始はInvalidOperationException。既存Serviceと同じ明示例外方式 |
| 6 t0 | PrepareAsync正常完了後のTimeProvider.GetTimestamp。呼出し時刻ではない |
| 7 即時実行 | Running遷移後、時刻を進めず最初のCycleを受け付ける。Taskはバックグラウンド実行のため、StartAsyncはCycle完了までは待たない |
| 8 固定周期 | 経過単調時刻eと周期P（TimeSpan.Ticks）から次の未来期日を(floor(e/P)+1)×Pと計算。完了時刻＋Pではない |
| 9 時刻抽象 | 注入TimeProviderのGetTimestamp／GetElapsedTimeとTask.Delay(delay, provider, token)。省略時TimeProvider.System。壁時計に依存しない |
| 10 ManualTimeProvider | TimestampFrequencyをTicksPerSecondに固定し手動Advance。壁時計は別操作で変更可能。CreateTimer／ITimerを実装し、遅延した複数期日はコールバック1回に合流。タイマー登録待ちとTaskCompletionSourceで同期 |
| 11 二重実行防止 | セッションごとのSemaphoreSlim(1,1)。状態ロック内でWait(0)し、最大同時実行数1 |
| 12 Skip | 取得不能ならカウンタ増加のみ。Cycle Taskや待ち行列を作らない。CycleSkippedイベントで「前回処理中のため周期スキップ」を通知可能 |
| 13 Catch-up防止 | 遅延した起床につき実行判定は1回。次はt0基準の未来境界へ進む |
| 14 Task追跡 | SessionがLifetime（監督）、Preparation、Scheduler、ActiveCycle、CancelTaskを保持。監督が準備・Scheduler・現在Cycle・キャンセルコールバックを観測する。履歴配列なし |
| 15 StopAsync | 同じ状態ロックでStoppingを確定し、新規開始禁止。待機・準備Tokenをキャンセルし、準備／Scheduler終了と受理済みCycle完了を待ってCTS／Semaphoreを解放しStopped |
| 16 冪等性 | Stopping中の全呼出しは同じLifetimeを返す。Stoppedでは完了Task。解放は監督1か所 |
| 17 競合 | tick／Stopが同じ短いロックで判定。Cycle Task参照を登録後、ロック外で起動ゲートを開く。Stopが先なら起動不可、Cycle受理が先なら停止は当該Taskを待つ |
| 18 Cancellation | 準備とScheduler待機だけに停止Tokenを使用。ExecuteAsyncにはTokenを渡さない。準備がTokenを無視する場合も完了まで安全に待つ |
| 19 実行抽象 | IPeriodicImportCycle.PrepareAsync(token)／ExecuteAsync()。実ファイルやDBを持たずに準備時間・長時間Cycle・失敗を注入するため追加。後続Phaseのファイル処理を先行実装せず独立試験できる |

### APIの補足

- StartAsyncの返すTaskは、準備完了・初回Cycle受付・Scheduler起動後に完了する。スレッドの実行順によってCycleの実処理開始は直後になる。開始失敗は呼出し元へ伝え、Starting中のStopでは開始Taskをキャンセル完了する。
- 実行時のCycle／Scheduler障害はLastErrorに保持し、自動的に安全停止する。Completionは異常も含めた停止処理の完了を待つTask。障害の有無はStatus.LastErrorを確認する。Cycle自身がStopAsyncをawaitする構造は採用していない。
- CurrentCycleCompletionは現在受理済みCycleのTaskスナップショット。新たな周期を開始するAPIではなく、停止・診断・テストで確定済み処理を待つためのもの。完全停止にはStopAsync／Completionを使う。
- 状態ロック内は状態変更・Task参照登録・非待機Semaphore操作のみ。注入した準備／Cycle、イベント通知、キャンセルコールバック、awaitはロック外で行う。
- CycleSkippedイベントはSchedulerスレッドから同期通知する。購読側は短時間で戻り、StopAsyncを同期Waitしない。UIへの有界通知・履歴・マーシャリングはPhase 5。購読処理が例外を出した場合もScheduler障害として監督が観測・停止する。
- Statusは最新スナップショットのみで無制限履歴を持たない。スキップ回数はLong.MaxValueで飽和する。
- タイマー分解能による早期実行を避け、待機後に単調時刻を再確認する。残り1ms未満は1ms待つ。リアルタイム実行の厳密な期限保証はしない。
- Optionsの周期はStartAsync受付時に値コピーし、呼出し後のDTO変更で実行周期を変更しない。
- 終了後の再Startは新しいセッション／CTS／Semaphoreを使用する。

## 3. 新規テストと結果（報告項目20～28）

21ケースすべて成功。TheoryのInlineDataごとに1ケースとして数える。

| 設計ID／追加区分 | テスト内容 | 件数 | 結果 |
|---|---|---:|---|
| T01 | 生成時Stopped、60秒周期で即時1回、時刻未進行で追加なし、Running二重Start拒否 | 1 | Pass |
| T02 | 1秒／604800秒の境界直前と到達時 | 2 | Pass |
| T03 | 0／-1／604801を準備前に拒否 | 3 | Pass |
| T04 | 0秒開始→10秒Skip通知→15秒完了→20秒開始、最大並行1 | 1 | Pass |
| T05 | 35秒一括進行で1回のみ、次40秒、壁時計の前進／後退に非依存 | 1 | Pass |
| T06 | NoWorkを1秒×1000周期、タイマー常時1、終了時0、処理数一致、例外なし | 1 | Pass |
| T20 A | Stopが先にStopping、tickが来ても新Cycleなし | 1 | Pass |
| T20 B | tickがCycleを受理してからStop、Cycle解放までStop未完了 | 1 | Pass |
| Stop追加 | Stoppedで繰返しStop | 1 | Pass |
| Stop追加 | 待機中Stop、7日進めても追加なし、再Start可 | 1 | Pass |
| Stop追加 | 実行中に重複Stopで同じTask、StoppingでStart拒否 | 1 | Pass |
| Stop追加 | Starting中Stop、停止Token通知、非協調的準備の完了待ち、Cycleなし | 1 | Pass |
| 基準時刻 | 35秒準備、完了から10秒境界、Options変更は反映しない | 1 | Pass |
| 障害追加 | 準備例外の伝播・記録・停止・再Start | 1 | Pass |
| 障害追加 | Cycle例外の観測、自分自身をawaitせず自動停止 | 1 | Pass |
| Stop追加 | 別Taskから同時Stop、双方が1つの停止完了を待つ | 1 | Pass |
| Stop追加 | 協調的準備をキャンセル、タスク残留なし | 1 | Pass |
| Scheduler障害 | 次タイマー生成の例外を注入、受理済みCycle完了を待ち停止 | 1 | Pass |

新規試験は実時間のThread.Sleep／Task.Delayに依存しない。TaskCompletionSource、Fakeの実行開始通知、タイマー登録通知、追跡Taskの完了で順序を固定する。T20ではロック競合の2つの確定順序を制御して試験し、無作為なタイミングに頼らない。

T03の小数・空文字はInteger DTOでは表現しないためUI Phaseへ保留。T06のDBログ増加はログ連携Phaseへ保留し、Fake DB Loggerは作成していない。新規テストには実CSV・SQLiteなし。既存回帰テストが元から使用する一時CSV／一時SQLiteは変更せず実行した。本番DBへの接続・Migrationは行っていない。

T06は1000周期におけるTask／タイマーの有界性と結果を試験したものであり、ヒープ使用量を測定した長期メモリプロファイルではない。未観測例外対策はTask追跡・例外注入試験とコード確認によるもので、GCタイミングに依存する試験は採用していない。

## 4. コマンド検証（報告項目29～37）

実装前と最終実装後に、次の順で実行した。

```text
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --no-restore
```

| 項目 | 変更前 | 最終実装後 |
|---|---:|---:|
| 29 既存ケース | 71成功 | 71成功、既存テストファイル無変更 |
| 30 総数 | 71 | 92 |
| 31 Pass | 71 | 92 |
| 32 Fail | 0 | 0 |
| 33 Skip | 0 | 0 |
| 34 restore | 終了コード0 | 終了コード0 |
| 35 Release build | 0エラー | 0エラー |
| 36 Release test | 終了コード0 | 終了コード0 |
| 37 Project／Package／Reference | 基準確認 | 追加・変更なし |

NU1900警告が3プロジェクトに発生。NuGetの脆弱性情報取得元へ到達できず、制限外のrestore再確認でも同警告が残った。既存依存の復元・コンパイル・テストは成功しているが、オンライン脆弱性監査の成功は確認できていない。警告を隠す設定変更やNuGet更新は行っていない。

途中のビルドでテストFakeのdelegate呼出し構文に1件のコンパイルエラーがあり、明示Invokeに修正した。最終結果は上表のとおり成功。途中の成功結果だけで完了扱いにはしていない。

## 5. 設計差分・残リスク・次工程（報告項目38～40）

### 38 設計との差分

要求・状態数・固定周期・安全停止仕様の変更なし。指示書§13で許可された最小Interface IPeriodicImportCycleを追加した。Phase 0の最終DTO／Interfaceをすべて空Stubで作るのではなく、Phase 1の試験に必要な型だけを実装した。Order／CorrectionBinding DTOは追加していない。

状態スナップショット、CycleSkipped通知、追跡TaskスナップショットはPhase 1用のAPI具体化。時刻丸めの再確認とキャンセルコールバックTask追跡は安全な実装詳細であり、設計書の改訂を要する仕様変更ではない。

### 39 残リスク・未確認

- Cycleまたは準備が永久に完了しない場合、Stopは安全のため待ち続ける。強制中断／タイムアウト終了は追加していない。
- 実ファイル・DB・Journal・順序復旧・ログ・UI・SCADA・長期性能は未実装／未検証。Phase 1成功はCSV自動取込完成を意味しない。
- TimeProvider.Systemの実OSスケジューリングには遅延がある。Fake試験は論理契約を確認するもので、リアルタイム性能を保証しない。
- 周期Tick計算のLong範囲を超えるような数万年の連続運転は対象外。万一の算術例外も監督が観測して停止する。
- NU1900によりオンライン脆弱性情報の確認は未完了。
- ApplicationにはWinForms／SQLite／CsvHelper参照を追加していない。既存TargetFrameworkや依存方向も変更していない。

### 40 Phase 2へ進めてよいか

**Phase 1完了条件を満たしており、ユーザーレビュー後にPhase 2へ進めてよい状態。Phase 2は開始していない。**

おすすめ：まず本報告書とPeriodicImportService／新規試験をレビューする。
理由：停止と周期の基礎を確定したうえで、次工程のDB互換性を独立して検証できる。
今すぐ行うこと：レビュー結果またはPhase 2着手指示を受け取る。自動的には次工程へ進まない。
