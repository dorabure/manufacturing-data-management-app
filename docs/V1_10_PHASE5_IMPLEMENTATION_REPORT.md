# Ver1.10 Phase 5 実装報告書

実施日: 2026-09-27

## 結論

**Phase 5完了・ユーザーレビュー待ち。Phase 6は開始していません。**

MainFormの監視操作、通知・履歴、修正版確認、手動DI、監視ライフサイクルログを実装しました。最終Release Build成功、Testは既存261件＋追加44件＝305件、Pass 305 / Fail 0 / Skip 0です。

検証は一時SQLite DB・一時監視フォルダ・実Journal、状態モデル、STA上のWinForms Controlを使用しました。Program.Main／アプリ本体は起動しておらず、本番DBは未使用です。実デスクトップ表示・目視・スクリーンショットによる配置確認は未実施です。

## 1. 変更ファイル（8）

- `src/ManufacturingDataApp/Program.vb`
- `src/ManufacturingDataApp/Forms/MainForm.vb`
- `src/ManufacturingDataApp/Forms/OperationLogForm.vb`
- `src/ManufacturingDataApp.Application/DTOs/PeriodicImportStatusDto.vb`
- `src/ManufacturingDataApp.Application/Services/PeriodicImportCycle.vb`
- `src/ManufacturingDataApp.Application/Services/PeriodicImportRecoveryService.vb`
- `src/ManufacturingDataApp.Application/Services/PeriodicImportService.vb`
- `tests/ManufacturingDataApp.Tests/Application/PeriodicImportServiceTests.vb`

既存テスト変更はT04のStatus.Message期待値1箇所だけです。SkippedCycles、CycleSkipped、10秒スキップ→20秒開始、最大同時実行数のAssertionは維持しています。

## 2. 追加ファイル（16、本報告書を含む）

- `src/ManufacturingDataApp/Forms/MainForm.Periodic.vb`
- `src/ManufacturingDataApp/Forms/PeriodicImportRecoveryForm.vb`
- `src/ManufacturingDataApp/Presentation/MonitoringSettingsController.vb`
- `src/ManufacturingDataApp/Presentation/PeriodicImportComposition.vb`
- `src/ManufacturingDataApp/Presentation/PeriodicImportStatusPresenter.vb`
- `src/ManufacturingDataApp.Application/DTOs/PeriodicImportNotificationDto.vb`
- `src/ManufacturingDataApp.Application/DTOs/PeriodicRecoveryContextDto.vb`
- `src/ManufacturingDataApp.Application/Interfaces/IPeriodicImportNotificationSink.vb`
- `src/ManufacturingDataApp.Application/Services/PeriodicNotifications.vb`
- `tests/ManufacturingDataApp.Tests/Application/Phase5NotificationTests.vb`
- `tests/ManufacturingDataApp.Tests/Application/Phase5RecoveryTests.vb`
- `tests/ManufacturingDataApp.Tests/Presentation/Phase5MainFormTests.vb`
- `tests/ManufacturingDataApp.Tests/Presentation/Phase5PresenterTests.vb`
- `tests/ManufacturingDataApp.Tests/Presentation/Phase5SettingsTests.vb`
- `tests/ManufacturingDataApp.Tests/TestSupport/Phase5Support.vb`
- `docs/V1_10_PHASE5_IMPLEMENTATION_REPORT.md`

## 3. Phase 4からの実差分

Phase 4のCycleへ安全な通知を追加し、RecoveryServiceへ表示用Query・候補Preview・未使用Binding再確認を追加しました。Schedulerへ任意の通知SinkとIImportLogWriterを注入しています。MainFormはPartialファイルへ監視画面の処理を分離しました。

Order／Attemptの永続化順、Journalフォーマット・検証、Move、owner.lock、Read Lease、Atomic、Transaction、停止境界は維持しています。Infrastructure・Domain・既存CsvImportService・CsvImportConfigForm・DB Schema／Migrationは変更していません。

開始時の全ファイルSHA-256との比較で既存変更8件・削除0件を確認しました。元からGit未追跡のため、git diffだけを証拠にしていません。.git／bin／objは比較対象外です。

## 4–6. MainFormの追加UI・モード・Panel

- 4: 既存検索行／操作行／DataGridViewを保持し、モード行と周期Panelを追加。2つのRadioButton、監視設定保存、フォルダTextBox＋参照、NumericUpDown、同一開始／停止Button、状態Label、履歴ComboBox、修正版Buttonを配置。
- 5: 手動モードではCSV取込を許可、周期モードではStoppedでも手動CSV取込を無効化。切替で非表示のフォルダ・周期を消去しません。
- 6: 周期Panelは周期モードだけ表示。フォルダ行と周期行を分け、既存検索領域を維持。周期は整数1～604800、既定60秒。「監視履歴（閲覧専用）」LabelとToolTipを追加。

## 7–8. 操作可否とイベント入口

|操作|手動・Stopped|周期・Stopped|Starting|Running|Stopping|
|---|---|---|---|---|---|
|モード、CSV設定選択／編集|可|可|不可|不可|不可|
|CSV手動取込|可|不可|不可|不可|不可|
|監視保存|設定選択時可|設定選択時可|不可|不可|不可|
|フォルダ／周期|非表示、値保持|可|不可|不可|不可|
|開始／停止|非表示|開始、設定選択時可|開始表示・不可|停止・可|停止表示・不可|
|検索、ログ、CSV出力|可|可|可|可|可|
|詳細編集、削除、マスタ|可|可|不可|不可|不可|

PeriodicUiPolicyをEnabled判定に使用します。CSV取込・設定編集・マスタ・削除・GridCellDoubleClickのイベント入口も現在のScheduler状態を確認し、無効Buttonを経由せず直接呼んでも処理しません。状態終了後は復帰し、周期モードの手動取込だけは引き続き無効です。設定未選択なら保存／開始とも不可。

## 9–12. 設定読込・保存・Dirty・切替

|項目|実装|
|---|---|
|9 読込|選択ConfigIdをCsvConfigService.GetByIdで再読込。NULLフォルダは空文字。GetAllの既存順を保持。保存モードが周期でもSchedulerは必ずStopped|
|10 保存|CsvConfigService.SaveMonitoringSettingsだけを呼び、ImportMode／WatchFolderPath／WatchIntervalSecondsの3項目を更新。成功後にDB再読込して表示。形式・MappingのSaveは呼ばない|
|11 Dirty|読込済み3項目と現在値の差分。ラジオ・フォルダ・周期変更を反映。保存成功で解消。失敗時は入力を保持|
|12 切替|保存＝元ConfigIdへ保存成功後に切替、破棄＝DBを変更せず切替、取消＝元選択・入力を保持。保存失敗なら切替しない。設定Dialogを開く前も同じ確認|

CsvImportConfigFormを閉じた後は一覧を再読込し、同じConfigIdが残っていれば再選択、削除済みなら既存一覧の安全な選択へ移ります。既存Dialogの項目・レイアウトは変更していません。

## 13–15. 開始・停止・終了の範囲

13. 開始は周期モード確認→Config選択確認→入力検証→3項目保存→Config再読込→Mapping再読込→新Options DTOとDatabaseIdentity→StartAsync。保存失敗ならStartAsyncに達しません。OptionsはScheduler／Cycleの既存DeepCopy契約へ渡します。Prepare完了前にRunningと表示しません。修正版未確定ならStoppedのまま確認画面へ案内し、自動再開しません。準備失敗は安全な理由を表示します。
14. Runningの同一ButtonからStopAsyncを呼び、直ちに停止処理中を表示し、完了までawaitします。停止中は再開始できません。現在CSVの途中キャンセルは追加していません。
15. FormClosing、e.Cancel、終了要求フラグ、Close再実行、30秒通知は未実装。Phase 6へ残しています。今回の監視停止接続はButton操作のみです。

## 16. Status.Messageの修正

累積SkippedCyclesを理由にスキップ文言を返す分岐を廃止し、Messageは現在のLifecycleを返します。IsProcessing／LastError／SkippedCyclesは既存どおりSnapshotに保持。CycleSkippedイベントを維持し、スキップ通知は履歴へ送ります。旧T04の期待値を「Running」へ正式更新し、新規テストでもスキップ後の正常Cycleが古いイベント表示を返さないことを検証しました。

## 17–23. 通知・Presenter・表示

|項目|実装|
|---|---|
|17 契約|ApplicationにNotification DTO／Kind／Sink。開始準備、開始、CSV検出・取込・成功・拒否・読込失敗、読込待ち、後続保留、修正版待ち／取込、復旧必要、Skip、停止処理／通常停止／異常停止、ログ失敗、復旧完了を表現|
|18 Presenter|Lock下の受信リスト、最新状態1件、履歴リスト、選択EventIdを分離。通知DTOを受信時コピー。WinFormsはApplicationへ参照しない|
|19 Queue|最大100。古い通常通知を優先して除去。すべて重大の場合は最古を除去して最新を保持。行単位Validation通知は積まない|
|20 履歴|最大100、新しい順、DropDownList。選択EventIdを保持し、履歴選択からSchedulerを操作しない。対象が容量制限で消えた場合／DropDownを閉じた場合は最新へ戻る|
|21 Skip|同種連続Skipは同じ項目の回数へ集約。Drainをまたいでも集約し、通常処理の最新状態を上書きしない|
|22 スレッド|通常はWinForms Timer 250msでDrain。重大通知は1件に集約したCriticalAvailable→BeginInvokeでUIへ。ワーカーからControlを書き換えない。Dispose済み／ハンドル未作成時の通知は安全に扱う|
|23 Label|履歴選択とは独立。日本語Lifecycle＋安全な最新メッセージ／ファイル名を表示。CSV成功時は「N件登録しました。一覧は検索で更新してください。」。周期通知からSearchを呼ばない|

表示文字列は最大512文字、CR／LF／Tab／その他制御文字を空白へ正規化。CSV本文、生のDB例外、スタックトレース、認証情報は送信しません。通知Sinkが例外を投げてもcatchし、Atomic結果・Journal・Moveを変更しません。履歴／Queueは永続ログとは別の限定的な表示履歴です。

## 24–29. 復旧Queryと修正版画面

|項目|実装|
|---|---|
|24 Query|ApplicationのGetRecoveryContext。状態、元Attempt、元名、Config、元順序、後続件数、安全な概要、未使用Binding、候補相対パス、確認可否、復旧必要を返す。検証不能なら安全なRecoveryRequired。UIはJSONを読書きしない|
|25 Form|元Attempt／元名／Config／順序／保留件数／概要／既存候補を表示。新候補をユーザーが明示選択し、Application Preview経由でパス・サイズ・mtime・SHA先頭12桁を表示。初期選択なし、チェック確認必須|
|26 初回Binding|停止中のCorrectionPendingだけConfirmCorrectionへ。Phase 4の後続／別Attempt／成功済み／指紋／設定の検証を維持|
|27 再確認|停止中・現在の元Attempt／Position・未使用BoundCorrectionを確認し、旧Binding.Invalidatedを永続化→OrderをCorrectionPendingへ保存→新候補を同じ条件で検証→新Bindingを保存。新候補が不適切なら旧Bindingは無効のまま。使用済みBindingは拒否|
|28 自動Startなし|Query、Preview、確認保存、取消のどれもSchedulerを開始しない。保存後は手動開始を案内。確認表示後に候補が変わった場合は、表示時指紋との照合でも拒否|
|29 表示差|CorrectionPendingは修正版確認待ち、RecoveryRequiredはDB／Journal整合性の復旧確認。修正版Buttonは停止中の確認可能な状態だけ表示し、RecoveryRequiredでは非表示|

元Attemptと現在のCSV形式・Mapping・監視設定が異なる場合は、元の設定へ戻す旨を表示します。候補の同名推測や自動承認はありません。共有違反・開始時再照合のPhase 4契約も維持しました。

## 30. Program手動DI

Programの既存Repository／各Service／CsvHelperAdapter／5引数の手動CsvImportService配線を維持し、同じDatabaseConnectionFactoryからPeriodicImportCompositionを生成します。

CompositionはFileStore、JsonJournal、PeriodicCsvImportExecutor、Cycle、Scheduler、RecoveryService、Presenterを手動構成します。SchedulerのLifecycleログも同じFactoryのDbLogWriterです。Factory.DatabasePathをOptionsのDatabaseIdentityへ渡します。新DIコンテナなし。Compositionを一時DBへ接続した実取込テストにより、開始・取込・停止ログと測定値がそのDBに保存されることを確認しました。Program.Mainはテスト起動していません。

## 31–34. ログ

|項目|内容|
|---|---|
|31 開始|Prepare成功後、Running遷移で「周期監視開始」1回。件数0|
|32 通常停止|当該Sessionの後始末完了時に「周期監視停止」1回。件数0、停止要求からの経過ms|
|33 異常停止|LastErrorのあるSessionは「周期監視異常停止」1回。開始準備失敗なら開始ログなし。件数0|
|34 表示|OperationLogFormへ周期CSV取込／失敗／結果不明、監視開始／停止／異常停止の6候補を追加。既存候補と編集可能ComboBoxを維持|

通常tick／空フォルダ／SkipごとのOperationLog追加はありません。Lifecycleログ失敗は安全な通知だけにし、ログの再帰記録や取込結果の変更をしません。

## 35–45. 既存回帰の確認範囲

|項目|確認|
|---|---|
|35 手動取込|旧5引数DI・手動入口を維持。既存AllowPartial、両Modeと保存方針独立のテスト成功|
|36 検索|Service／検索処理は無変更。監視中も検索有効。周期通知時にDataSource・検索条件を変更しないSTA検証成功|
|37 編集／削除|既存行ID取得テスト・Service回帰成功。Starting／Running／Stoppingの直接イベント呼出しでもDialog／DB処理へ進まない|
|38 CSV出力|既存出力処理無変更、監視中もButton有効。Config選択は固定。既存回帰成功|
|39 CSV設定|既存56件を維持。MainFormは監視3項目だけ保存、DB Snapshotで形式／Mapping等不変を確認。複数設定の復元・再生成も検証|
|40 マスタ|Service／Dialog本体無変更。監視中の入口を抑止、停止後復帰。既存回帰成功|
|41 ログ画面|ErrorLog無変更。OperationLogの旧・新候補と編集可能属性を実Controlで確認。既存ログ回帰成功|
|42 Phase 1|21件成功。固定周期／二重実行防止／安全停止のAssertion維持。T04のMessage期待値だけ仕様更新|
|43 Phase 2|56件成功。DB／Migration／新規初期化／Ver1.00互換は無変更|
|44 Phase 3|31件成功。Atomic／部分登録／Transaction／結果DTOは無変更|
|45 Phase 4|82件成功。順序／Journal／移動／復旧／Binding実行／Lease／停止境界を維持。追加でSink例外時もAtomic成功・Journal完了を確認|

上記はコード・自動テストによる確認です。既存全Dialogを実デスクトップで手操作した、スクリーンショットで確認した、という意味ではありません。

## 46–51. 最終テスト結果

|区分|件数|Pass|Fail|Skip|
|---|---:|---:|---:|---:|
|46 既存（Ver1.00 71＋Phase 1 21＋Phase 2 56＋Phase 3 31＋Phase 4 82）|261|261|0|0|
|47 Phase 5追加|44|44|0|0|
|48–51 総数|305|305|0|0|

追加44件の内訳（Theory展開後）:

|テストクラス|件数|主な内容|
|---|---:|---|
|Phase5PresenterTests|8|状態マトリクス、有界Queue・履歴、選択、制御文字、Skip集約、並行通知・Dispose|
|Phase5SettingsTests|9|Dirty保存／破棄／取消、保存失敗、開始前の呼出順、形式独立、範囲・未選択|
|Phase5RecoveryTests|9|安全Query／Preview、初回・再確認・無効化順、使用済み拒否、指紋変化、設定不一致、破損|
|Phase5NotificationTests|8|Sessionログ、準備／実行失敗、Skip後の正常状態、ログ失敗、Sink失敗、実結果通知|
|Phase5MainFormTests|10|STA実Control、直接イベントガード、設定再生成、履歴、取消、ログ候補、一時DB実DI|

MessageBoxの自動クリックやThread.Sleepには依存していません。実Controlが必要なテストのみSTAで生成し、アプリのメインループは起動していません。

## 52–54. 最終Release検証・Warning・依存

最後に次の順で実行しました（すべて終了コード0）。

```text
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --no-restore
```

- 52: Release Build成功、0エラー。最終Testは305 Pass / 0 Fail / 0 Skip（約20秒）。
- 53: NU1900が3件。対象はPresentation／Infrastructure／Tests。NuGetサービスインデックスに到達できず、脆弱性データ取得に失敗。通常制限外でのrestore再実行でも継続しました。ビルド・テスト失敗ではありませんが、脆弱性監査成功とは判断できません。警告抑制は追加していません。
- 54: Solution 5 Project、ProjectReference、TargetFramework、Package Versionに変更なし。Microsoft.Data.Sqlite 8.0.31、CsvHelper 33.1.0、Microsoft.NET.Test.Sdk 18.10.0、xunit.v3／runner 4.0.0を維持。新NuGet・DIコンテナなし。

## 55–58. 対象外の維持

55. 一時SQLite／一時Root／一時Journalのみで検証。本番LocalApplicationData配下のDBへ接続していません。
56. FormClosingによる終了時StopAsync、安全終了の統合、終了要求フラグ等は未実装。
57. USER_GUIDEは未更新。
58. dist、Publish、Package、配布生成スクリプトは未変更・未実行。既存dataと設計文書も開始時ハッシュと一致。

## 59. 設計との差分

要件・DB・画面の業務仕様変更なし。指示書で認められたStatus.Messageの仕様更新と、Phase 5予定のUI・通知・再確認契約を実装しました。

具体化として、MainFormをPartialへ分離、操作可否とDirty処理を小型Presentationクラスへ分離、通知の重大即時反映を合成イベント＋BeginInvoke、通常反映を250ms Timerとしました。候補Preview後の変更を拒否する任意のexpectedFingerprint引数を確認APIへ追加しています。既存呼出しは従来の引数で動作します。ApplicationにWinForms／SQLite／CsvHelper／Infrastructure参照は追加していません。

## 60. 残リスク

- 実デスクトップの目視／DPI・画面サイズ・大量履歴の操作感は未確認。STA Controlテストと実運用の目視確認は別です。
- Phase 6の終了制御がないため、監視中のウィンドウ終了を安全終了済みとは扱えません。現段階では停止ButtonでStoppedを確認してから終了する必要があります。
- ファイル変更／DB結果不明／管理記録不整合は従来どおり安全停止します。RecoveryRequiredを無条件に解除するUIは追加していません。
- UI履歴は100件のメモリ内表示であり監査用の永続保存ではありません。
- NU1900のため脆弱性情報取得は未確認。ネットワーク回復後の再監査が必要です。

## 61. Phase 6へ進めるか

コード・Release Build・305件の自動テスト上は、Phase 5レビューへ提出可能です。Phase 6へ進むにはユーザーレビューと次の明示指示が必要です。今回は開始しません。

おすすめ: 本報告書の操作可否・修正版確認・残リスクをレビューすること。
理由: 終了時の安全停止は次Phaseであり、実運用可能な完成版とはまだ区別する必要があるためです。
今すぐやるべきこと: Phase 5の内容を確認し、修正希望またはPhase 6の指示を提示してください。

