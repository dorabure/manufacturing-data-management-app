# Ver1.10 Phase 4 実装報告書

実施日: 2026-09-27

## 結論

**Phase 4完了・ユーザーレビュー待ち。Phase 5は開始していません。**

Release Build成功、Release Testは既存179件＋Phase 4新規82件＝261件、Pass 261 / Fail 0 / Skip 0です。実監視フォルダ／一時SQLite／実JSONを組み合わせて、順序保持、Atomic、移動、復旧、修正版対応付け、停止境界を検証しました。本番DB・アプリ本体・UI・Program.vb・配布物は扱っていません。

## 1. 変更ファイル（5）

- `src/ManufacturingDataApp.Application/DTOs/PeriodicImportOptionsDto.vb`
- `src/ManufacturingDataApp.Application/Interfaces/IPeriodicImportCycle.vb`
- `src/ManufacturingDataApp.Application/Services/PeriodicImportService.vb`
- `src/ManufacturingDataApp.Infrastructure/Logging/PeriodicImportLogWriter.vb`
- `tests/ManufacturingDataApp.Tests/TestSupport/PeriodicImportCycleFake.vb`

既存Phase 1テスト本体の期待値・Assertionは変更せず、Fakeだけを新Interfaceへ機械的に適合しました。既存179ケースの削除／Skip／期待値弱体化はありません。

## 2. 追加ファイル（15、本報告書を含む）

- `src/ManufacturingDataApp.Application/DTOs/PeriodicJournalDtos.vb`
- `src/ManufacturingDataApp.Application/Interfaces/IPeriodicImportStorage.vb`
- `src/ManufacturingDataApp.Application/Services/PeriodicImportContracts.vb`
- `src/ManufacturingDataApp.Application/Services/PeriodicImportCycle.vb`
- `src/ManufacturingDataApp.Application/Services/PeriodicImportRecoveryService.vb`
- `src/ManufacturingDataApp.Application/Services/PeriodicJournalValidator.vb`
- `src/ManufacturingDataApp.Infrastructure/PeriodicImport/JsonImportProcessingJournal.vb`
- `src/ManufacturingDataApp.Infrastructure/PeriodicImport/PeriodicCsvImportExecutor.vb`
- `src/ManufacturingDataApp.Infrastructure/PeriodicImport/PeriodicImportFileStore.vb`
- `tests/ManufacturingDataApp.Tests/Infrastructure/Phase4BoundaryTests.vb`
- `tests/ManufacturingDataApp.Tests/Infrastructure/Phase4PathAndJournalTests.vb`
- `tests/ManufacturingDataApp.Tests/Integration/Phase4ImportTests.vb`
- `tests/ManufacturingDataApp.Tests/Integration/Phase4RecoveryTests.vb`
- `tests/ManufacturingDataApp.Tests/TestSupport/Phase4Fixture.vb`
- `docs/V1_10_PHASE4_IMPLEMENTATION_REPORT.md`

関連する小型DTOはPeriodicJournalDtos.vb、Storage／Executor抽象はIPeriodicImportStorage.vbにまとめています。新Projectはありません。

## 3. 前Phaseからの実差分

Phase 3のCsvImportService、CsvImportExecutionResultDto、PeriodicFileResultDto、AtomicCsvImportTestsは無変更。手動AllowPartial／周期AllOrNothingの入口契約はそのままです。Phase 2のMigration／Schema／Config persistenceも無変更。user_version=110を維持しました。

開始時SHA-256と照合して、既存ファイルの差分は§1の5ファイルのみ、既存ファイル削除なしを確認しています。開始時からGit未追跡のファイル群のため、git diffだけを差分の証拠としていません。bin／obj／.gitはハッシュ比較対象外です。

## 4–7. Cycle契約・設定・終了

|報告項目|実装|
|---|---|
|4 Cycle変更理由|PrepareAsync(optionsSnapshot, cancellationToken)、ExecuteAsync(stopBoundaryToken)、CleanupAsyncへ拡張。設定固定、所有Lease寿命、同一バッチ内の未着手CSV停止を表現するため|
|5 Deep Copy|StartAsync受付時にConfigの全項目、Mappingの全要素、周期、DatabaseIdentityを値コピー。Cycleでも専用コピーを保持し、Executorへ渡すRequestも別コピー。RootはInfrastructureで正規化、DB識別はExecutorの接続Factoryの絶対パスと照合|
|6 Cleanup|Scheduler監督Taskが準備・Scheduler・受理済みCycleを待った後に1回呼出し。開始失敗・準備中Stop・異常停止も同じ経路。CycleのownerをDisposeして参照を解放。Cleanup自体も反復に安全|
|7 Stop境界|停止受付時に境界確認専用Tokenをキャンセル。Cycleは未着手ファイル／新Attemptの前でのみ確認。CSV解析・Validation・DB Transaction・結果Journal・移動は途中キャンセルしない。次CSVは開始せず、Schedulerの既存固定周期・排他を維持|

停止境界Tokenはポーリング専用で、Cycleでコールバック登録やCSV処理への転送を行いません。既存Scheduler待機／Prepare用のCancellationは別管理です。

## 8–11. FileStore・パス・Lease・指紋

|報告項目|実装|
|---|---|
|8 FileStore|PeriodicImportFileStoreに具体I/Oを集約。Root検証、直下列挙、フォルダ準備、owner、Read Lease、宛先生成、非上書きMove|
|9 パス|Windows絶対ローカルパスだけ許可。UNC・拡張UNC／デバイス形式・Network等の非ローカルDriveType、Root／祖先／対象／出力先のreparseを拒否。GetFullPath／GetRelativePathとパス要素検証を併用し、..、兄弟prefix、ADS、末尾空白／ドット、監視直下外を拒否|
|10 Read Lease|FileMode.Open／FileAccess.Read／FileShare.Read。Atomic終了まで保持し、既存CsvHelperAdapterの別Readを許可。Write／Delete／Renameは不許可。Win32共有違反32／lock violation33だけNothing＝次周期待ち。アクセス拒否／一般I/Oは待ち扱いにしない|
|11 Fingerprint|正規化相対パス、Length、LastWriteTimeUtc、SHA-256、取得できる場合はWindows Volume Serial＋File Index。Read Lease下で確定。列挙時の未読取ファイルには観測サイズ・mtime・取得可能なIDを保存。指紋を永久除外キーにはしない|

success／error／.periodic-importをPrepare時と各Cycle冒頭で検証し、自分でCreateNewしたprobeのみFlush・削除します。失敗時はAtomic 0回で停止。既存CSV／既存フォルダを準備テストとして削除しません。

## 12–14. OrderとJ-01=B

未完了Orderがない場合だけ.csvを大小文字無視で直下列挙します。.csvx／.txt／子フォルダ内は対象外。LastWriteTimeUtc→FileName OrdinalIgnoreCase→Ordinalで並べ、Positionを付けて**最初のCSV処理前に保存**します。

未完了Orderがあれば列挙結果で並べ直しません。Cursorで示す先頭が共有違反なら後続も未処理で残し、その周期だけ終了、Running継続。Stop→Start／Cycle再構築後も同じOrderを読みます。未着手CSVの書込みによるmtime／サイズ更新は順序変更の理由にしません。観測済みFile IDの変化・消失など検出可能な置換は停止します。

途中の新着Dは現在Orderへ挿入せず、現在Order完了後の次周期に新Orderを作ります。修正版も元Positionで処理し、現在mtimeで後続を追い越したり後ろへ回したりしません。

## 15–19. Attempt・永続化

|報告項目|実装|
|---|---|
|15 Attempt|Version、AttemptId、OrderId、Position、根本／直前失敗Attempt、Binding ID、Root、DB識別、元名、Fingerprint、設定／Mapping Snapshot・Hash、開始時刻、Stage、Result、宛先、完了時刻、安全な理由。ConfigIdはSnapshot.Config、各件数／OutcomeはResultに格納。CSV本文なし|
|16 Stage|ImportStarted→ResultKnown→MovePending→Completed。結果不明／実体不一致等はRecoveryRequired。Rejected／ReadFailedのCompletedはOrder上CorrectionPending|
|17 JSON保存|.periodic-import内のwrite-GUID.tmpへSystem.Text.JsonでSerialize、Flush(True)後に既存JSONへFile.Replace、初回はFile.Move。JSONはVersion＋Payload＋SHA-256 envelope。破損・未確定tmpを空Journalとみなさない|
|18 owner.lock|OpenOrCreate／ReadWrite／FileShare.Noneで取得。存在だけで使用中と判断せず、truncateしない。Session中保持。RecoveryServiceも停止確認＋同じownerを取得|
|19 Atomic前保証|ImportStarted保存→Binding使用Attempt保存（修正版のみ）→Order Entryとの関連保存→実JSON再読込・参照検証→Atomic。どこかの保存失敗／参照不整合でDB処理を開始しない|

OrderはGenerationを比較してから増加・原子的置換。Attempt／Order／Bindingは別JSONです。複数JSONとDBを同時commitしたとは扱わず、中間不整合は推測せず停止します。SHA-256 envelopeは破損検出であり、改ざん耐性のある署名ではありません。

## 20–24. Executorと結果分類

PeriodicCsvImportExecutorが既存ImportAndSaveAtomicを直接呼出します。Parse／Validation／DB重複の複製なし。既存CsvHelperAdapterは未変更で、その周囲のReadBoundaryAdapterが「Repository到達前の読込例外」であることを明示します。

|報告項目|結果|DB確定性・処理|
|---|---|---|
|20 分類境界|Infrastructure内のみ|SQLite／CsvHelper型をApplicationへ持ち込まない|
|21 Succeeded|行エラーなし（0行含む）|DbOutcomeKnown=True→success→Completed→Cursor前進|
|22 Rejected|Validationエラーあり|登録0、error→Completed→CorrectionPending→停止。正常保留行の偽ErrorLogなし|
|23 ReadFailed|Read境界のCsvHelperException／IOException／UnauthorizedAccessException|DB未実行確定。error→Completed→CorrectionPending。未計数の件数0を行数実測として扱わない|
|24 DatabaseFailed／Unknown|SqliteException／その他想定外|DbOutcomeKnown=False、元CSV保持、RecoveryRequired、後続禁止。Rollbackできたと思われる障害もValidation拒否へ変換しない|

致命例外ログは既存IImportLogWriterを利用し、ReadFailedは[CSVRead]＋周期CSV取込失敗、DB／不明は[DB]／[RecoveryRequired]＋周期CSV取込結果不明。例外の生メッセージやCSV本文をJournal／追加ログへ転記しません。不明結果の件数0は未登録確定を意味しません。ログ自体の失敗は再帰的にログしません。

## 25–31. 移動・復旧

|報告項目|契約|
|---|---|
|25 MovePending|結果保存→宛先保存（MovePending）→Read Lease解放→元指紋照合→Move→先指紋照合→Completed。移動だけの障害では既知DB結果を維持|
|26 衝突|非上書きMove。同名から開始、衝突時UTCの元名_yyyyMMdd_HHmmssfff_連番.csv。合計最大100候補。存在確認後の衝突も80／183のみ再候補化。一般IOExceptionは無条件再試行しない。長名は元stemを短縮＋SHA-256由来識別子、.csv維持|
|27 Move復旧|元あり／先なしで一致なら移動のみ、元なし／先ありで一致ならCompletedのみ。両方あり／なし・不一致はRecoveryRequired。復旧後は一度Stoppedに戻し、新規Atomicを呼ばない|
|28 ImportStarted復旧|未実行／拒否／commit済みを推測せずRecoveryRequired。Atomic再呼出し0|
|29 ResultKnown復旧|MovePending以降だけ実行。Atomic再呼出し0|
|30 Completed復旧|成功でCursor未更新ならCursorのみ修復。拒否でOrder未更新ならCorrectionPending復元。修正版成功についても検証|
|31 RecoveryRequired|DB不明、ImportStarted残存、JSON破損、参照／位置／Root／DB／Hash不一致、複数未完了Order、孤立未完了Attempt、Cursor不正前進、ファイル実体不一致等。元CSVを通常error扱いで移動せず、新規／修正版を実行しない|

100候補がすべて占有されて宛先を決定できない場合は、永続StageはResultKnownのまま、停止理由MovePendingです。架空の移動先・完了状態を保存せず、再開始時は移動後半だけ再評価します。

Move前後の照合により検出可能な置換を止めますが、ガード解放→Moveの競合窓そのものをなくすものではありません。違反ファイルがMoveされた後に差替えを検出する場合は移動先を含めて要復旧とし、Completedにはしません。

## 32–36. CorrectionBinding

PeriodicImportRecoveryService.ConfirmCorrectionは停止中にユーザーが明示選択した元Attemptと候補を受け取るApplication契約です。画面は作っていません。

|報告項目|内容|
|---|---|
|32 構造|Binding ID、OrderId、Position、FailedAttemptId、候補Fingerprint、ConfigHash、確認時刻／確認印、Invalidated、UsedAttemptId|
|33 候補検証|元は既知0件のRejected／ReadFailed、Completed、error移動済み、現在HeadとLatestFailed一致。候補は同Root直下の通常.csvで、Read Lease下確認。後続Entry／他未解決Attempt／成功済み実体・同内容コピー・Root外を拒否。後続の消失・識別不能も拒否|
|34 優先処理|Binding保存では開始しない。明示Start後に設定／指紋再照合し、新Attemptを元Orderの元Positionへ関連付けて保存してからAtomic。B2のmtimeがCより新しくてもB2→C|
|35 再失敗|新AttemptをLatestFailedにし、RootFailedは初回Bのまま。error→Completed→再びCorrectionPending、Cは未処理|
|36 一回性|ImportStartedに関連付けてUsedAttemptIdを保存。再起動でも使用済みBindingをAtomic再登録に利用しない。使用途中の曖昧な状態はRecoveryRequired|

候補の内容・指紋・消失または設定変更はBinding無効／再確認待ち。単なる共有違反なら同じBoundCorrectionを先頭に残し、Runningで次周期へ。成功済みerror原本を後日ユーザーが編集・再配置する運用を妨げないよう、既にCompletedの拒否原本を再認定用の指紋条件にはしていません。確認した候補の意味上の正しさはユーザー判断、内容の適否は既存全行Validationです。

## 37. Completed保持30日

TimeProviderで日時を注入。未完了Orderに属するAttempt／Bindingは期限超過でも削除しません。Orderが正常解決した時刻と各Completed時刻の遅い方から30日経過後だけ、検証した自アプリ管理JSONを削除します。

Cleanup前に実JSONを再読込・グラフ検証。Orderを空のResolved receiptとして原子的保存し、Binding→Attempt→最後にOrderを削除します。削除途中で停止した場合はそのreceiptで中間状態を識別でき、未完了Orderとして新規登録しません。CSV、success／error内CSV、ユーザーフォルダ、未知ファイル、未解決Journalを自動削除しません。owner.lockも残し、次回は実ハンドル取得可否で判定します。

## 38. ログ失敗

WriteErrors成功→WriteOperation失敗でもDB結果を反転させません。Succeededならsuccess、Rejectedならerrorへ移動しCompletedまで確定して停止。成功時はCursor前進後、次CSVなし。Rejectedの停止理由はCorrectionPendingで、ログ欠落情報はAttempt.Result.LogRecorded=Falseとして併せて残します。二重ErrorLog・ログ無限再試行なし。既存DecoratorはCSV取込だけ周期CSV取込へ変換し、明示した周期CSV取込失敗／周期CSV取込結果不明等は保持します。

## 39–45. 必須テストとの対応

各テストは一時監視フォルダ・一時DB・実JSONを使用します。以下は指示書A～AMの対応です（Theoryの展開件数と要件数は別）。

|指示書|主な検証|結果|
|---|---|---|
|A・B|mtime／大小文字無視／Ordinalの安定ソート、拡張子、直下のみ|Pass|
|C|success／error／管理フォルダ準備失敗、Atomic 0、元不変|Pass|
|D・E|Write handleで先頭保留、Scheduler Running、Stop・再構築、完成後mtime変更でもA→B→C|Pass|
|F|実Read Lease中のWrite／Delete／Rename拒否、既存Adapter Read許可|Pass|
|G・H|正常・空・ヘッダーのみ、実DB、各Stage、Cursor、success|Pass|
|I・J|正常3＋異常2、DB／CSV内重複、全件拒否、error、後続なし|Pass|
|K・S|DatabaseFailed／Unknown、実SQLite INSERT障害、元保持、既成功A維持、RecoveryRequired|Pass|
|L・M|Move障害、MovePendingから移動のみ復旧、DB再実行0|Pass|
|N・O・P|元なし先あり、ImportStarted、ResultKnownを新Cycle／Journal読込で復旧。再Atomic 0|Pass|
|Q|両方あり／両方なし／指紋不一致|Pass|
|R|A成功／B異常／C untouched、A再実行なし|Pass|
|T|同Rootの独立所有ハンドル2つを取得不可、Cleanup後取得可、lock内容非truncate|Pass|
|U|UNC、拡張パス、実junction（Root／祖先／出力先／対象パス）、..／兄弟prefix／ADS拒否|Pass（実Network割当ドライブは未実測）|
|V・W・X|同名だけでは再開しない、B2→C、再失敗の根本／直前Attempt|Pass|
|Y・Z|DB不明へBinding拒否、確認後内容／設定／消失で無効化|Pass|
|AA・AB・AC|後続C、成功実体／コピー、別孤立未解決Attemptの候補拒否|Pass|
|AD|使用済BindingのImportStarted残存から再利用なし|Pass|
|AE・AF|通常／修正版のCompleted成功後Cursor修復、Completed拒否後CorrectionPending修復|Pass|
|AG|古いmtimeの新着Dは次Order、既存を追い越さない|Pass|
|AH・AI|Stopは現在CSV確定待ち、次CSVなし。Prepare中Stopでowner解放・再Start|Pass|
|AJ|ログ部分失敗でも配置／DB結果維持、後続なし、ErrorLog1回|Pass|
|AK|40日経過した参照Attempt保持、解決後29日保持・31日で管理JSONだけ削除|Pass|
|AL・AM|破損／未確定tmp／複数未完了Order／参照欠落／Cursor／Position／Hash／DB不一致|Pass|

追加で最大100候補衝突、Move直前競合、長い衝突名、一般IOExceptionの非再試行、ImportStarted保存失敗、Move後指紋差分、tmp→close→csv rename公開を検証しています。大文字小文字だけ異なる名前の最終Ordinal比較は、通常Windowsフォルダの制約に依存しない列挙DTOのソート単体試験で確認しました。

## 46–52. 回帰・件数

|報告項目|区分|Pass|Fail|Skip|
|---|---|---:|---:|---:|
|46|Phase 1|21|0|0|
|47|Phase 2|56|0|0|
|48|Phase 3|31|0|0|
|49|既存全体（Ver1.00 71を含む）|179|0|0|
|50|Phase 4追加|82|0|0|
|51・52|最終総数|261|0|0|

追加82件の内訳: Phase4ImportTests 25、Phase4RecoveryTests 22、Phase4BoundaryTests 25、Phase4PathAndJournalTests 10。

着手前179件成功、段階ごとのBuild／限定試験、最後に全261件成功を確認しました。途中の新規テストコンパイルエラー（VBのCount overload、変数shadowing等）と、新規復旧テストの例外型／中断位置指定を修正済みです。既存期待値の変更による回避はありません。

## 53–55. 最終Release検証・依存

順番に実行しました。

```text
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --no-restore
```

- restore: 終了コード0。ネットワーク制限外の再確認も終了コード0。
- Release Build: 成功、0エラー、3警告。
- Release Test: 終了コード0、261 Pass / 0 Fail / 0 Skip。
- NU1900はInfrastructure／Presentation／Testsの3プロジェクトで継続。NuGetサービスインデックス取得不可による脆弱性情報取得失敗です。restore／buildそれぞれ3件。オンライン脆弱性監査の成功とは扱いません。
- Project／Package／Reference差分なし、5 Project維持。新NuGet・Version変更・DIコンテナ・警告抑制なし。
- 新Applicationコードに具体System.IO／SQLite／CsvHelper／Infrastructure／WinForms参照なし。標準JSON／SHA-256は設定Hashに使用。新規の具体I/O・例外型分類はInfrastructureに配置。

## 56–58. 実環境に触れていない範囲

本番LocalApplicationDataのDBへ接続していません。明示的な一時test.dbと一意の一時Rootのみ使用し、テスト終了時にSQLite Connection Poolを解放して片付けました。junction試験は一時領域に作ったリンク自体のみ非再帰削除し、リンク先の保持ファイルが変わらないことを確認しました。

MainForm／UI／Program.vb／FormClosing／USER_GUIDE／dist／Publish／Packageは未変更・未実行。アプリ本体起動なし。監視CycleとScheduler／Atomicの接続はテストから直接構築しています。本番DI・画面による設定再読込／保存・開始停止操作はPhase 5以降です。

実SCADA機器での試験は未実施です。Windows handle保持、tmp→close→renameを一時ファイルで再現しました。実プロセスKill・電源断・実ネットワークドライブ・長期性能試験は未実施で、実JSON状態＋新Service/Cycle/Journalインスタンスによる決定論的復旧試験と区別します。

## 59. 設計との差分・実装具体化

業務要件変更なし。設計の責務を小型型・Interface・Cycleとして具体化しています。

- Snapshotは既存PeriodicImportOptionsDtoを拡張してDeep Copyし、別名のSessionSnapshot DTOを増やしていません。
- DTOのConfigId／結果件数等はSnapshot／Resultへ集約し、同じ値を二重に持つ不整合を避けています。
- Attempt／Order／Bindingを別JSONに保存し、EnvelopeのHashとOrder Generation、再読込によるグラフ検証を追加しました。
- 保存途中の曖昧な孤立記録は自動的に繋ぎ直さずRecoveryRequired。DB完了推測は禁止のままです。
- 保持期限削除中断を扱うため一時的な空Resolved receiptを利用し、最後にそのJSONも削除します。CSVやフォルダの自動削除はありません。
- 周期取込の正常／失敗／結果不明ログを接続。Schedulerの開始停止イベントをDBログへ接続する本番通知配線、UI表示は後工程であり、今回Programへの接続はしていません。

## 60. 残リスク

- Journal、DB、ログ、ファイルの一括Transactionはありません。commit後で結果Journal前の中断はImportStarted残存として停止し、人による照合が必要です。
- FileShare解除からMoveまでの競合窓、断続的close→追記、管理者によるハードリンク・同時パス差替え等を完全に防ぐセキュリティ境界ではありません。公開後不変、ローカル通常フォルダ、運用ACLが前提です。
- 管理フォルダ全削除、同じDBパスへの別DB差替え、整合した全Journalを過去へ戻す操作の完全検出は保証しません。
- JSONのFlush／replaceはソフトウェア上の確定点です。実電源断・ストレージの書込みキャッシュまで含む耐障害性はPhase 7の実機検証が必要です。
- 所有Lease試験は同一プロセスの独立ハンドルでWindows共有制約を確認しました。別OSプロセスによる所有・強制終了の実機試験は未実施です。
- 実Network割当ドライブでの試験は未実施。実junctionでreparse経路を検証しましたが、ファイルシンボリックリンク／全種類の仮想ストレージの網羅試験ではありません。
- Phase 1の累積SkippedCyclesに由来するStatus.Messageの既知表示事項は変更していません。Phase 5で現在状態とイベント通知を分離します。
- NU1900によりオンライン脆弱性情報確認は未完了。ログ欠落は結果を反転させず停止・通知情報に残します。
- 大量CSV／大規模Journalの性能、実SCADA、GUI、復旧操作画面は未検証・未実装です。安全停止後にJournalを削除して再実行させる運用はしません。

## 61. Phase 5へ進めるか

Phase 4の指定自動検証・実装報告まで完了しました。おすすめは本報告書と変更差分のユーザーレビューです。理由は、次工程がUI／本番DIという別の接続責務に進むためです。今すぐ行うことはレビューであり、承認・次の指示があるまでPhase 5を開始しません。

**Phase 4完了・ユーザーレビュー待ち。**

