# Phase 6 実装報告書

状態：**Phase 6完了・ユーザーレビュー待ち**

実施日：2026-09-27。Phase 7は開始していません。

## 1. 実装概要

Phase 6指示書およびVer1.10基本設計§8に従い、MainForm終了を既存StopAsyncへ接続しました。現在のCSVを強制キャンセルせず、セッション完了・Cleanup成功・Stoppedを確認してからCloseを再実行します。

主要変更はPresentationと追加テストです。ApplicationにはCleanup成功を読み取る状態情報だけを追加しました。Atomic、Journal、Order、Recovery、FileStore、Migration、Database本体は未変更です。

基本設計の設定保存記述にある終了時未保存確認も、Phase 5の保存／破棄／取消処理を再利用して接続しました。基本設計§8の30秒経過説明は既存250ms UI Timerで表示し、タイムアウト停止は設けていません。

## 2. FormClosingの実装方式

MainForm.Closing.vbにFormClosingイベント処理を分離しています。

```text
Running中にClose
→ 初回FormClosingをキャンセル
→ 終了要求を1回だけ受理、StopAsync
→ 新しいCSVの開始禁止
→ 現在のCSVのAtomic／Journal／Moveを既存契約で完了
→ Cleanup、owner.lock解放
→ セッションCompletion完了＋Stopped＋Cleanup成功を確認
→ UIスレッドでBeginInvoke(Close)
→ 次のFormClosingが通過
→ Form／通知先をDispose
```

Stopが同期的に完了した場合でも、Closeは必ずBeginInvokeで投稿します。初回FormClosingの呼出しスタック内でCloseを再帰呼出ししません。Wait／ResultでUIを停止させません。

## 3. 終了要求フラグ／再入防止方式

PeriodicImportExitControllerがUIスレッド上で次の状態を保持します。

| 状態 | 意味 |
|---|---|
| Requested=False | 通常状態 |
| Requested=True、AllowFinalClose=False | 安全停止待機、または安全停止を確認できず終了保留 |
| AllowFinalClose=True | 安全停止を確認し、最終Closeを許可 |
| Failed=True | 停止例外またはCleanup未完了。自動Closeしない |

追加Closeは既存の終了処理を待つだけです。未保存確認ダイアログ中の再入も_checkingCloseSettingsでキャンセルします。

## 4. StopAsync多重実行防止方式

Starting／Runningの最初の終了要求だけがStopAsyncを呼びます。StoppingであればStopAsyncを追加呼出しせず、Scheduler.Completionを待ちます。終了要求後の開始／停止Buttonイベントも入口で拒否します。

テスト実測：Starting／RunningでClose要求を10回発行しても、StopAsync呼出しは**1回**。停止Button相当ですでにStopを1回呼んだStoppingケースでは、Close10回による追加Stopは**0回**、合計**1回**です。各ケースのCleanupは**1回**でした。

## 5. Starting／Running／Stopping別の終了動作

| Lifecycle | 終了動作 |
|---|---|
| Stopped、安全停止済み | StopAsyncを呼ばずそのままClose |
| Stopped、Completionがまだ未完了 | 初回をキャンセルし、残りのCompletionを待つ |
| Starting | StopAsyncで既存の準備停止契約を適用。準備終了とCleanupを待ち、CSVは開始しない |
| Running | StopAsyncで新規開始を禁止し、現在CSVとCleanupを待つ |
| Stopping | すでに進行しているCompletionに合流。独立したStop／Cleanupを起動しない |
| StoppedだがCleanup失敗 | Stoppedだけで安全と判断せず、終了を保留 |

終了要求中はUI操作判定をStopping相当のまま維持します。セッションがStoppedになっても、モード・設定・詳細編集・削除・マスタ・手動Import・開始Buttonを再有効化しません。検索・ログ閲覧・CSV出力の既存方針は変更していません。

## 6. CSV処理中Closeの動作

一時DB／実Cycle／実Journal／実FileStoreを使用し、以下の6段階をTaskCompletionSourceで保持して実MainForm.Closeを呼びました。

| 段階 | 保持位置と確認 |
|---|---|
| Atomic | Executor入口で保持。Close後も通常のImportAndSaveAtomicへ進みAを完了 |
| Validation | 実AtomicのDB既存重複確認Search境界で保持。その後の検証・登録を完了 |
| DB Transaction | 実MeasurementDataRepository.AddRangeが行をINSERTした後、Commit前の列挙継続を保持。別接続から登録数0を確認し、解放後Commit |
| Journal | ResultKnown保存の直前で保持。Close後も保存、Move、Completed、Order更新を完了 |
| Move | 実FileStore.Moveの直前で保持。Close後も移動とJournal更新を完了 |
| Cleanup | 実Cycle.CleanupAsyncの直前で保持。owner保持中はFormを閉じず、解放後にClose |

これらは境界ゲートによる決定論的試験です。OSのファイル書込みシステムコール内部やCommitシステムコール内部を停止させる試験ではありません。既存同期APIを途中キャンセルするコードは追加していません。

書込み待ちは実FileStreamの共有制約で再現しました。先頭Aと後続Bを直下に保持、Atomic呼出し0、DB登録0、error移動0のままCloseします。

## 7. Close要求後に次CSVを開始しないことの確認

上記6ケースはA.csv／B.csvを確定順に置き、終了時に次を確認しました。

- Executor呼出しはA.csvのみ、1回。
- Aは登録1件、Attempt=Completed、success移動済み。
- Bは監視直下に残り、Order.Cursor=1を維持。
- 次の明示的な新セッションでのみBを処理。Aの再登録なし、最終DB登録数2。

CleanupケースはAのExecutor結果確定時にStopを要求してからCleanupを保持し、Stopping中Closeがその停止へ合流することを検証しています。

## 8. Cleanup／owner.lock解放確認

処理保持中に同じRootのAcquireOwnerを呼ぶとIOExceptionになり、排他所有が継続することを確認しました。正常Close後は同じRootのAcquireOwnerが成功します。続いて同じRootで新しいSchedulerを開始し、残ったBの処理を確認しました。

Applicationの変更理由：既存SchedulerはCleanup例外をLastErrorへ記録しても最後にStoppedとなります。また先行エラーがあるとLastErrorは先行エラーを保持するため、LastErrorだけではCleanup失敗を識別できません。

このためStatusDtoにCleanupCompletedを追加しました。未起動時True、開始時False、CleanupAsyncの正常完了後にだけTrueとし、既存Lifecycle・例外処理・停止順序は維持しています。終了側はさらにセッションCompletionの正常完了を待ちます。Cleanup後の残処理が失敗した場合も、Completion例外でCloseを保留します。

## 9. CorrectionPending／RecoveryRequiredでの終了動作

安全停止済みなら通常のStopped Closeを許可します。終了のためだけにCorrection、Atomic、Recoveryを自動実行しません。

実Journalにそれぞれの状態を作り、Close前後のDBスナップショットと全JSONファイル本文が不変であること、Executor追加呼出しなし、Binding追加なしを確認しました。

RecoveryRequiredをClose可能とするのは、指示書§13に従い、現在の実行Taskが終わりCleanup済みのケースです。結果不明のCSVを成功とみなしたり、未完了Taskを放棄して終了したりする意味ではありません。未解決記録をそのまま残します。

## 10. StopAsync／Cleanup異常時の動作

- Stopの同期例外／非同期例外、Completion異常、安全停止条件不成立ではAllowFinalCloseを立てません。
- 既存Presenterと状態Labelで「安全な停止完了を確認できないため、終了を中止しています」と通知し、画面を保持します。例外詳細やSQLはUIに出しません。
- Closeを繰り返してもStop／Cleanupを自動再試行しません。処理結果や解放済み範囲が不明なまま再実行する危険を避けるためです。
- 終了失敗後も編集・開始系操作はロックし、既存ログ閲覧は残します。
- 既存Schedulerの異常停止通知／OperationLogの方針を維持し、終了用の重複DBログや自動Recoveryは追加していません。

これは指示書§14の「安全性優先・既存通知・強制終了しない」に基づく最小限の採用挙動です。失敗後の復旧／終了再試行専用UIは未追加です。Environment.Exit、Application.ExitThread、Thread.Abort、CSV途中Cancellationはありません。

## 11. 変更ファイル一覧

開始時SHA-256と終了時SHA-256を比較しました。元のcheckoutはGit上でソース一式が未追跡のため、git diffのみには依存していません。bin／obj／.gitは比較対象外です。

### 変更した既存ファイル（4）

| ファイル | 変更内容 |
|---|---|
| src/ManufacturingDataApp/Forms/MainForm.vb | 終了イベント初期化の呼出しを追加 |
| src/ManufacturingDataApp/Forms/MainForm.Periodic.vb | 終了中の操作ガード、完了後の不要なUI更新抑止、停止例外観測、終了状態表示 |
| src/ManufacturingDataApp.Application/DTOs/PeriodicImportStatusDto.vb | CleanupCompleted情報 |
| src/ManufacturingDataApp.Application/Services/PeriodicImportService.vb | Cleanup成功の記録・スナップショットへの反映のみ |

### 新規追加ファイル（7）

- src/ManufacturingDataApp/Forms/MainForm.Closing.vb
- src/ManufacturingDataApp/Presentation/PeriodicImportExitController.vb
- tests/ManufacturingDataApp.Tests/Presentation/Phase6ExitControllerTests.vb
- tests/ManufacturingDataApp.Tests/Presentation/Phase6MainFormTests.vb
- tests/ManufacturingDataApp.Tests/Presentation/Phase6ClosingIntegrationTests.vb
- tests/ManufacturingDataApp.Tests/TestSupport/Phase6Support.vb
- docs/V1_10_PHASE6_IMPLEMENTATION_REPORT.md（本書）

削除ファイル：0。既存テストファイルの変更：0。既存305テストの期待値・Assertion・Skipは変更していません。

## 12. 追加テスト一覧

| ファイル／テスト | 件数 | 主な検証 | 結果 |
|---|---:|---|---|
| ExitController / StoppedDoesNotCallStop | 1 | StoppedでStop不要 | Pass |
| ExitController / TenCloseRequestsJoinOneStop | 3 | Starting／Running／Stopping、Close10回、Stop合計1回、Cleanup1回 | Pass |
| ExitController / ThirtySecondsOnlyChangesExplanationNeverTimesOut | 1 | 仮想時間30秒・1日経過でも強制終了なし | Pass |
| ExitController / StopExceptionOrFaultNeverPermitsClose | 2 | 同期・非同期Stop例外、Close許可なし、再実行なし | Pass |
| ExitController / CleanupFailureIsVisibleEvenIfFirstErrorWasDifferent | 2 | Cleanup単独失敗と先行エラー＋Cleanup失敗を識別 | Pass |
| ExitController / StoppedPublicationStillWaitsForLifetimeCompletion | 1 | Stopped公開直後のCompletion未完了競合 | Pass |
| MainForm / StoppedActualClosePassesOnceWithoutCleanup | 1 | 実FormClosing1回、Cleanup0回 | Pass |
| MainForm / ActualFormClosingCancelsThenPostsExactlyOneUiClose | 3 | Starting／Running／Stopping、UIスレッド、初回Cancel、Cleanup待機、最終Close | Pass |
| MainForm / CleanupFailureKeepsFormAndMutationGuardsAfterStopped | 1 | 失敗通知、Stopped後もフォーム保持・操作ガード | Pass |
| MainForm / DisposedFormDoesNotReceiveDeferredCloseOrNotification | 1 | 外部Dispose競合後の安全確認。通常終了経路としてDisposeする試験ではない | Pass |
| Integration / CloseDuringRealPipelineCompletesAOnlyReleasesOwnerAndCanRestart | 6 | Atomic／Validation／Transaction／Journal／Move／Cleanup、A完了・B未開始、owner解放、再Start | Pass |
| Integration / WritingHeadClosePreservesBothFilesAndDoesNotImport | 1 | 書込み待ちの保持、登録／error移動なし | Pass |
| Integration / PendingStoppedCloseNeverRunsCorrectionOrRecovery | 2 | CorrectionPending／RecoveryRequired、不意の処理なし | Pass |
| **合計** | **25** | 指示書§21の最低20項目を上記で網羅 | **全Pass** |

実MainFormのRunning連打試験では、10回のキャンセルされたFormClosing＋最後の通過1回＝11回を確認しました。Starting／Stoppingは初回キャンセル＋再Close通過の2回です。UIスレッドIDも一致し、無限再帰はありません。

テストはSTAとメッセージ処理、一時DB、TaskCompletionSource、TimeProvider、既存Fixtureを使用しています。Thread.Sleepによる処理順の推測、テスト専用の本番分岐、既存Assertionの弱体化はありません。

## 13. 全テスト数

- 既存：305
- 追加：25
- 合計：330
- Pass：330
- Fail：0
- Skip：0

## 14. Release Build結果

最終確認は以下の順で実行しました。

```text
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --no-restore
```

restore：終了コード0。到達失敗確認のため制限外でも再実行し、終了コード0・NU1900継続。

Release Build：成功、Errors 0、Warnings 3（NU1900のみ）、終了コード0。

## 15. Release Test結果

Phase 6単独：25 Pass／0 Fail／0 Skip。

最終全体：330 Pass／0 Fail／0 Skip、終了コード0。

検証経過：変更前の初回305件検証ではPhase5SettingsTests.DirtySwitchPreservesOriginalTarget(Save)の後片付け時、test.db削除がファイル共有IOExceptionで失敗し304 Pass／1 Failでした。同じコードで再実行して305 Pass／0 Fail、今回の実装後も既存305件の段階で全Passでした。ファイルを保持していた主体は未特定です。原因を断定せず、既存テストや削除処理を変更して隠していません。最終330件は全Passです。

## 16. Warning

NU1900がPresentation／Infrastructure／Testsの3 Projectで継続しています。NuGet脆弱性サービスのサービスインデックスへ到達できない既知警告です。

Package更新や警告抑制は行っていません。**脆弱性監査が正常完了したとは報告しません。** Build／Testの成功とは別の確認事項です。

## 17. Project／Package差分

| 項目 | 結果 |
|---|---|
| Project追加／削除／変更 | なし（既存5 Project） |
| TargetFramework | 変更なし（net8.0-windows） |
| Package追加／削除／Version変更 | なし |
| ProjectReference | 変更なし |
| DI | 既存手動DIを維持。Program.vb・Composition未変更 |
| Phase 2 Migration | 変更なし |
| Phase 3 Atomic | 変更なし |
| Phase 4 Journal／Order／Recovery／FileStore | 変更なし |
| Infrastructure／Domain | 変更なし |
| 固定周期／CSV順序／途中Cancellation契約 | 変更なし |
| USER_GUIDE／配布ZIP／Publish／DataGridView ReadOnly | 対象外、変更・実行なし |

要件・DB・画面全体の設計変更はありません。Cleanup成功情報の追加と異常終了時の保留挙動は本書§8・§10に明記した安全終了のための実装詳細です。

## 18. 実ユーザーDB未使用確認

Program.Mainおよびアプリ本体の本番起動は行っていません。テストはPhase2Database／Phase4Fixtureの明示的な一時test.db・一時Watch Root・一時Journalのみ使用しています。LocalApplicationData/ManufacturingDataApp/ManufacturingDataApp.dbへ接続していません。

## 19. 残課題・既知リスク

- 実MainFormイベント・Control・UIスレッドでの自動検証と、実デスクトップの目視／画面キャプチャは別です。後者は未実施です。
- 未保存確認は既存保存／破棄／取消処理へ接続し、そのControllerテストは既存テストで維持しました。今回、実MessageBoxのクリック操作試験は行っていません。
- OS強制終了・電源断・OSシャットダウンの強制打切りをアプリ側で防げるとは保証しません。通常のFormClosing経路の安全停止が対象です。
- 同期I/O等が永久に戻らない場合、安全性のため終了を待ち続けます。30秒は説明追加の目安であり、停止期限ではありません。
- Cleanup失敗後の自動リトライや強制終了ボタンはありません。画面を保持し、調査・復旧判断が必要になります。
- 既存テストの一時DB削除に一過性の共有エラーを1回観測しました。再発時の調査候補ですが、このPhaseでは既存テスト基盤を変更していません。
- NU1900により脆弱性監査は未確認です。
- Phase 7全結合試験、配布作成、性能改善、一覧ReadOnlyハードニングは未着手です。

おすすめ：Phase 6の独立ソースレビューを先に実施してください。

理由：自動テストでは安全停止と競合条件を確認しましたが、実機終了操作と異常時運用のレビューは別途必要なためです。

今すぐやるべきこと：ユーザー側でレビュー用ZIPを作成し、本書と変更ファイルをレビューしてください。Phase 7は追加指示を受けるまで開始しません。
