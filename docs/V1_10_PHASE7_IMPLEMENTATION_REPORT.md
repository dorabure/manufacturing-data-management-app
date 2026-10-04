# Phase 7 実装・検証報告書

検証日：2026-09-27（JST）
結論：**Phase 7 自動検証完了・実機確認待ち**。
実装指示書の主要横断自動検証・文書更新を完了しました。T01～T78すべての実機／クラッシュ試験が合格した意味ではありません。未実施範囲は第3・28節と対応表に明記します。Phase 8は開始していません。

## 1. Phase 7概要

MainForm→監視設定→Scheduler→Cycle→Order／Attempt／Journal→File Lease→Atomic→SQLite→Move→Recovery→StopAsync→FormClosingを横断確認しました。二重登録防止と元Order順序を優先し、新機能・安全チェック省略・大規模リファクタリングは行っていません。

入力基準はPhase 6までの現ワークスペースです。指示書記載のPhase 6 ZIP名・SHA-256は参照情報であり、今回そのZIP原本との一致を検証したとは扱いません。

## 2. Baseline結果

変更前にrestore→Release build --no-restore→test --no-build --no-restoreを実行。330 Pass／0 Fail／0 Skip、Build 0エラー。NU1900×3のみ継続しました。Baseline失敗はなく、既存テストを変更して通す対応はしていません。

## 3. T01～T78対応状況

[対応表](INTEGRATION_TEST_MATRIX.md)に全78 ID、実クラス・メソッド、種別、追加区分、Assertion、未確認範囲を記載しました。

| 表記 | ID数 | 意味 |
|---|---:|---|
| PASS | 48 | 記載した自動確認範囲で合格 |
| PASS / MANUAL | 17 | 自動部分は合格、実操作／実機残り |
| PASS / PENDING | 10 | 境界注入等は合格、要求の一部の専用試験が残り |
| MANUAL | 1 | T19実SCADA環境なし |
| PENDING | 2 | T27・T38の専用全体シナリオ未実施 |
| 合計 | 78 | N/Aで除外した項目なし |

48という値は実機を含めた包括的受入の合格数ではありません。既存の16業務フロー対応表も維持しました。テスト名だけで判定せずAssertionを確認しています。

## 4. Phase 7追加テスト

| クラス・実メソッド | ケース数 | 主な確認 |
|---|---:|---|
| Phase7EndToEndTests.MainFormSettingsStartThreeFilesLogsSearchThenClose | 1 | 実Control設定→開始、A/B/C、DB／ログ／Cursor、検索・クリア・終了 |
| Phase7EndToEndTests.SchedulerCorrectionBindingPreservesSnapshotFingerprintAndChain | 2 | 修正版成功／再拒否、指紋・Snapshot・関連ID・順序 |
| Phase7EndToEndTests.TwoSchedulersRespectActualOwnerLease | 1 | 実owner lease競合と安全停止後再取得 |
| Phase7EndToEndTests.FatalRepositoryBoundaryNeverMovesToErrorOrRetries | 8 | connection/locked/search/equipment/add/commit-unknown/unique/fk |
| Phase7EndToEndTests.LegacyMigrationThroughMainFormStartPreservesOriginalData | 1 | 旧DB→backup→反復Migration→実MainForm設定→開始 |
| Phase7PublicationTests.TmpWriterIsIgnoredUntilClosedAndRenamedAtNextTick | 1 | 書込み中tmp除外、Close＋rename後取込 |
| Phase7PublicationTests.DirectCsvStableWhileOpenRemainsWaitingAcrossRestart | 2 | FileShare.None/Read、複数周期・再起動・mtime逆転 |
| Phase7PublicationTests.RejectMoveFailureWithTickRecoversMoveOnlyAndNeverStartsFollowing | 1 | 拒否中tick・error Move失敗・移動だけ復旧 |
| Phase7PerformanceTests.TenThousandRows_UiRespondsAndCloseWaitsForAtomic | 1 | 1万行、UI post、Close待ち、次CSV未処理 |
| Phase7PerformanceTests.CsvBatch_OrderedSingleWorkerDurableAndNoDuplicates | 1 | 多数CSV、並行1、DB／success／Journal／ログ件数 |
| Phase7PerformanceTests.LongElapsedAndTenThousandSkipsKeepNotificationsAndLogsBounded | 1 | 1万skipと3650日仮想経過、履歴・ログ有界 |
| Phase7GridTests.DataCellsCannotEditBoundExportRowsButCheckboxCanEdit | 1 | 全非チェック列ReadOnly、6項目編集不可、チェック可 |
| Phase7DateFilterTests.UncheckedLogDatesDoNotBecomeMinValue | 2 | 両ログ実Controlの日付未指定検索 |
| 合計 | 23 | 既存330に上乗せ |

Phase7Supportは本物のAdapter・Repository・Atomic実装の周囲に計測／障害注入を置くテスト用部品です。本番コードにテスト専用分岐は追加していません。

## 5. 横断E2E結果

正常系はA→B→Cの呼出順、DB3、success3、Attempt Completed3、Cursor3、取込成功数合計3、監視開始／停止ログ各1を確認。MainForm日付未指定検索3件、存在しない設備検索0件、条件クリア3件を確認しました。大量CSVでは再開始しても呼出し・DB件数が増えないことも検証しています。

## 6. Order / Journal / Recovery結果

既存Phase4RecoveryTestsを再実行しImportStarted／ResultKnown／MovePending／Completedの永続JSONを再読込。結果不明はRecoveryRequired、結果既知は移動またはメタデータのみ修復し、既処理CSVへの追加Atomic呼出し0を検証しました。

Phase7の8種類の障害でも、元CSVと後続が残りerror移動なし、Order／AttemptがRecoveryRequired、Stopped、新Schedulerで追加Atomic0です。commit-unknownは実DB commit後に例外を注入し、DB1件が残っていても再実行しないことを確認しました。UNIQUE／FKは実SQLite制約違反。connection／locked／Search／Equipment／Add障害は境界例外注入で、物理ディスク障害や実lock待ちではありません。

Journal不整合、指紋不一致、両方存在／両方なし、Cursor不整合、参照欠落等も既存テストで確認。OS強制終了の試験ではなく例外注入・サービス再生成です。

## 7. 修正版フロー結果

A正常→B拒否→C保留後、B2をCより新しいmtimeにし、明示Preview／Confirmを行いました。Binding保存だけではStartしません。手動Start後B2→C、元OrderId・Position・ConfigHash・SnapshotHash・Root／Latest失敗Attempt・BindingId・Fingerprint・UsedAttemptIdを照合しました。

再拒否ではDBはAの1件だけ、B2 error、再びCorrectionPending、Cは元のままです。Phase5の再確認・旧Binding先行無効化・候補変更・禁止候補防止を維持しています。

## 8. Stop / FormClosing競合結果

Phase1のtick直前／直後Stop、準備中／待機中Stopと、Phase6のAtomic／Validation／Transaction／Journal／Move／Cleanup境界の終了試験を再実行しました。現在A完了・次B未開始・Cleanup一回・所有解放・再取得を確認しています。

Close連打10回でもStop一回、Stopped／Running／Starting等の終了防御、Cleanup失敗時に画面保持、Dispose後の通知抑止を維持。1万行試験でもDB開始前にCloseし、AのDB登録とCompleted後に閉じてBを残すことを確認しました。

## 9. owner.lock結果

同RootでScheduler Aが所有、BはStart拒否・Atomic0。AのStopAsync完了後にBがStartでき、1件取込。ファイル存在のみではなく実Leaseに基づくことを検証しました。実アプリ2プロセスの目視試験ではありません。

## 10. 手動Import回帰

既存MixedRows_EntryNotModeDeterminesPolicyの4組合せを維持。手動はDB3・Total5/Success3/Failure2、AtomicはDB0・5/0/5・不正2・正常保留3。ImportModeで保存方針を切り替えません。手動の未知設備FK挙動も維持しました。CSV出力・検索・編集・削除・ログ等は既存業務フローで再検証しています。

## 11. Ver1.00 DB Migration回帰

既存7表の値・ID・Backup・反復・未知Schema拒否・WALバックアップ等を再実行。追加E2Eでは旧データのSnapshotを比較し、Migration反復後も不変、Backup1、旧区切り「;」・ヘッダーなし設定を維持してMainForm開始から新CSVを登録しました。Measurement Id71、LogId81、ErrorId91を保持し、削除済みSeed設備を再投入しません。

強制終了を伴うMigrationは未実施。既存4地点例外注入のRollback確認とは区別します。

## 12. UI回帰

MainFormの設定・検索・クリア・開始／終了、両ログの日付未指定検索、状態別Enabledとイベント入口を自動Controlで確認。詳細編集・削除・CSV出力・CSV設定・マスタのService／DB回帰も成功しました。

全ダイアログの実クリック・表示品質は未実施。自動ハンドル生成やBeginInvoke成功から、実デスクトップ全操作の成功を推測していません。

## 13. DataGridViewハードニング

MainFormの全列を列名で判定し、選択Checkbox以外をReadOnly=Trueにしました（非表示IDも含む）。測定日時・設備ID・設備名・項目名・測定値・単位のBeginEdit=False、バインドモデル不変を確認。CheckboxはBeginEdit=True、選択ID7を取得できます。

正式な編集経路はMeasurementDetailFormのままです。CSV出力の元になる一覧モデルをセル直接編集で変更する経路を閉じました。出力処理自体は変更せず、既存CSV出力テストを維持しています。

## 14. 通知・履歴ストレス

1万回skipでも通知Queue<=100、集約Repetitions10000、現在状態をskipで置換しないことを確認。さらに3650日進めた期日遅延を1つに集約し累計10001。実時間10年待機ではありません。監視ログは開始／停止の2件のみで毎秒増加しません。

既存Presenterテストで履歴100、512文字制限・制御文字正規化・選択保持・重要通知・Dispose防御を確認。これらの有界性は検証しましたが、アプリ全域の長期メモリリーク不存在の証明ではありません。

## 15. 性能結果

時間値に独断の性能合否閾値を設けず、正しさと測定値を分けました。既存UI試験の25秒待機上限はデッドロック検出用で、5秒等の速度合否判定を追加していません。

### 1万行CSV（1回の測定記録）

| 指標 | 実測 |
|---|---:|
| CSV読込 | 67.70 ms |
| Validation＋mapping＋DB／設備lookup | 2215.36 ms |
| DB AddRange | 230.34 ms |
| Atomic入口全体 | 2568.83 ms |
| 開始～安全終了 | 2860.99 ms |
| UI BeginInvoke応答 | 0.81 ms |
| managed memory 前／後 | 22684816 / 8525376 bytes |
| 最終DB／並行数 | 10000行 / 1 |

Validation値は純粋なValidator単体ではなく、読込終了からAddRange開始までのmapping・検索等を含む区間です。UI応答はDB直前の制御ゲート中にSTAメッセージループへ投稿した測定で、実画面の全処理期間の無停止を保証しません。Close要求後Aを完了してB未開始を確認しました。

### 1000 CSV × 各1行（実測完走）

| 指標 | 実測 |
|---|---:|
| 総処理 | 4973289.40 ms（約82.9分） |
| CSV読込合計 | 331.50 ms |
| Validation＋mapping＋lookup合計 | 516.95 ms |
| DB AddRange合計 | 18374.62 ms |
| managed memory 前／後 | 2512056 / 20299232 bytes |
| テストプロセスpeak working set | 101552128 bytes |
| DB／success／Attempt／Cursor | 各1000 |
| 最大並行数 | 1 |

全順序、Completed全件、JournalValidator成功、取込ログ1000、再開始による追加Atomic・DB登録なしを確認。測定時メソッド名はThousandCsv_OrderedSingleWorkerDurableAndNoDuplicatesで、実測後に現在の件数選択式CsvBatch_OrderedSingleWorkerDurableAndNoDuplicatesへ変更しました。1000件を未実施のまま100件に置き換えたわけではありません。

約83分と極端に時間を要したため、通常の全回帰3回は100 CSVで実行しました（1万行試験は各回含む）。1000件を再測定する方法：

```powershell
$env:MDA_PERF_CSV_COUNT = "1000"
try {
    dotnet test -c Release --no-build --no-restore --filter FullyQualifiedName~CsvBatch_OrderedSingleWorkerDurableAndNoDuplicates --logger "console;verbosity=detailed"
} finally {
    Remove-Item Env:MDA_PERF_CSV_COUNT
}
```

原因候補（推測）：Journal全体の反復読込、パス安全確認、Order／Attempt相関検証の増加が支配的な可能性があります。読込・DB計測合計と総時間の差だけで原因を断定していません。詳細プロファイル未実施で、安全チェック削除による高速化はしていません。

メモリ値はGCタイミングや他テストの影響を受けるスナップショットです。後値が小さい／大きいことだけでリークの有無を判定しません。多数CSV処理の実運用適性・許容遅延は対象PCで別途合意が必要です。

### 長時間相当と測定環境

仮想時刻で10000周期と3650日を確認（第14節）。実長期稼働や実低速SCADAは未実施。

Windows 10.0.26200、win-x64、Intel Core i5-1135G7 @ 2.40GHz（4コア8論理）、.NET SDK 8.0.425、MSBuild 17.11.48、Runtime／WindowsDesktop 8.0.31。一時SQLite・一時ローカルWatch／Journal使用。搭載RAM・ディスク性能は未測定で、他PCの保証値ではありません。

## 16. SCADA公開方式相当試験

本物のFileStreamでtmpを開いたまま複数周期待っても取込0、Close→csv rename後に1回登録。直接csvではFileShare.NoneとReadの両方で、サイズ・mtimeが安定してもwrite handle保持中は全件保留、再起動後も元順序を維持しました。

reject＋Move失敗＋tickではskip、MovePending、再生成で移動のみ・Atomic0・CorrectionPending・C未処理を確認しました。

## 17. 実SCADA確認

**未実施（環境なし）**。本番設備への接続・設定変更・操作なし。[実機確認手順](V1_10_SCADA_MANUAL_VERIFICATION.md)に承認・隔離環境・Open／Close／rename／追記／断続open-close観測、S01～S09、結果記録方法を記載しました。

## 18. 実デスクトップ確認

**100%／125%／150%すべて未実施**。利用可能な操作環境ではネイティブWindowsデスクトップ操作・撮影を使用できず、実画面のPASSは宣言していません。[UI確認表](UI_LAYOUT_CHECK.md)に8画面、周期Panel・履歴・状態・修正版Button、最小サイズ・長文の確認手順を記載しました。新しい実画像はありません。

## 19. ドキュメント更新

README、USER_GUIDE、IMPLEMENTATION_STATUS、INTEGRATION_TEST_MATRIX、UI_LAYOUT_CHECK、screenshots案内をVer1.10へ更新。本報告書とSCADA実機確認手順を新規作成。存在しない画像リンクは追加せず、旧distへの文書コピーもしていません。

## 20. 発見・修正した不具合

| 不具合／原因 | 修正と回帰テスト | 既存仕様への影響 |
|---|---|---|
| Gridの非チェック列が編集可能でバインド結果が変更され得る | MainFormで全非チェック列ReadOnly。Phase7GridTestsは修正前に失敗、修正後成功 | 正式な詳細画面編集だけを維持 |
| 日付未指定なのに検索0件。VBのIf式でDateTime側へ型推論されNothingがMinValueになる | MainForm／ErrorLogForm／OperationLogFormのIf分岐をCType(..., DateTime?)で明示。Main E2E・両ログ2ケースが修正前失敗、修正後成功 | 「未指定は条件なし」という既存仕様を回復 |

Main E2Eの最初の失敗では停止前のAssertionによりowner.lockのfixture削除例外が本来の失敗を覆いました。新規テストで例外を保存しStopAsync完了後に再throwする後片付けを入れ、本来の「3件期待に対し0件」を確認して修正しました。例外の握り潰しやRetry／Sleepで隠す変更ではありません。

Phase6で一度観測されたDirtySwitchPreservesOriginalTarget(Save)のtest.db削除時共有IOExceptionは今回のBaseline・最終3回で再現しませんでした。原因を特定したとは報告しません。上のowner.lock問題とは別です。

## 21. 変更ファイル一覧

本番コード（3）：

- src/ManufacturingDataApp/Forms/MainForm.vb
- src/ManufacturingDataApp/Forms/ErrorLogForm.vb
- src/ManufacturingDataApp/Forms/OperationLogForm.vb

追加テスト・補助（6）：

- tests/ManufacturingDataApp.Tests/Integration/Phase7EndToEndTests.vb
- tests/ManufacturingDataApp.Tests/Integration/Phase7PublicationTests.vb
- tests/ManufacturingDataApp.Tests/Integration/Phase7PerformanceTests.vb
- tests/ManufacturingDataApp.Tests/Presentation/Phase7GridTests.vb
- tests/ManufacturingDataApp.Tests/Presentation/Phase7DateFilterTests.vb
- tests/ManufacturingDataApp.Tests/TestSupport/Phase7Support.vb

文書（8）：

- README.md
- docs/USER_GUIDE.md
- docs/IMPLEMENTATION_STATUS.md
- docs/INTEGRATION_TEST_MATRIX.md
- docs/UI_LAYOUT_CHECK.md
- docs/screenshots/README.md
- docs/V1_10_SCADA_MANUAL_VERIFICATION.md（新規）
- docs/V1_10_PHASE7_IMPLEMENTATION_REPORT.md（本書、新規）

生成された検証証跡（3）：tests/TestResults/Phase7/phase7-run1.trx、phase7-run2.trx、phase7-run3.trx。bin／objは通常のBuild生成物です。削除ファイルなし。

## 22. テスト総数

既存330＋Phase7追加23＝353。既存テスト・補助VB計38ファイルは変更前SHA-256と一致。既存テスト削除、Skip追加、Assertion削除・弱体化なし。

## 23. Release Build結果

最終restore成功後、dotnet build -c Release --no-restore成功。0エラー、3警告（NU1900）。コード変更後にBuildし、その成果物で次節の全3回を実行しました。3回後は文書のみ編集しています。

## 24. Release Test 3回結果

`dotnet test -c Release --no-build --no-restore` にTRX出力指定を付けて連続実行しました。

| 回 | TRX開始～終了（2026-09-27 JST） | Pass | Fail | Skip |
|---|---|---:|---:|---:|
| 1 | 18:26:20～18:28:10 | 353 | 0 | 0 |
| 2 | 18:30:04～18:31:29 | 353 | 0 | 0 |
| 3 | 18:32:17～18:34:08 | 353 | 0 | 0 |

3回とも終了コード0。TRXのCounters(total/passed/failed/notExecuted)を照合しました。再試行で失敗を隠していません。通常多数CSVは100件、1000件別測定は第15節です。

## 25. Warning

NU1900×3（Presentation／Infrastructure／Tests）が継続。NuGet脆弱性サービスへ到達できず、権限承認後のrestoreでも同警告。restoreとbuildは成功していますが**脆弱性監査完了ではありません**。警告抑制・Package更新をしていません。

## 26. Project / Package差分

5 Project維持、追加・削除なし。Solutionと5 vbproj、Directory.Build.propsを含む既存設定は変更前hashと一致。TargetFramework・ProjectReference・Package・Package Version変更なし。DIコンテナ追加なし。ApplicationのSQLite／CsvHelper直接参照なし、InfrastructureのDB／CSV責務維持。

作業開始時からソース全体がGit未追跡だったため、git diffだけに依存せず、変更前に取得したファイルSHA-256一覧と終了時一覧を照合しました。旧distの4ファイルも一致し、Publishしていません。

## 27. 実ユーザーDB未使用確認

追加テストはPhase2Database／Phase4FixtureのGUID付き一時ディレクトリ、一時SQLite、Watch Root、Journalを使用。CreateMainへ一時DB由来のRepository／Compositionを注入しています。Program.Mainを試験目的で起動せず、実ユーザーLocalAppData DBを開いていません。実ユーザーDBを確認のために読み込む行為もしていません。

## 28. 残課題・既知リスク

- 実SCADA公開契約、実デスクトップ8画面×3倍率、モーダル画面全操作は未実施。
- 1000小CSVは約82.9分。対象PCの生成量・許容遅延を合意し、必要なら別スコープで詳細プロファイルと安全性を維持した性能改善を検討。
- T09のDB前消失／置換、T15 error衝突、T27 Root削除／ACL、T38 DBとErrorLog同時障害の専用ケースが残る。
- Journal／Migration境界の実子プロセス強制終了、実disk full／実接続間lock待ち、実ネットワーク割当拒否は未実施。
- 通知有界は検証済みだが全域の長期メモリリーク保証なし。計測Validation区間はlookup等を含む。
- NU1900で脆弱性監査未完了。既知警告を黙って解消扱いにしない。
- .periodic-import痕跡なし削除、DB単独差替えの完全自動検出は保証外。保全・一式照合が必要。
- Phase8成果物には.git、bin、obj、テスト一時データ、ローカルDB、開発キャッシュを含めない。

## 29. Phase 8進行可否

**この作業からPhase 8へは進みません。** Publish／dist再作成／配布ZIP作成／最終リリースなし。

おすすめ：まず本書と対応表の独立レビュー、次に隔離環境でSCADAとDPI確認。理由：自動試験合格だけでは装置公開契約・画面品質・処理量適性を保証できないためです。今すぐ行うこと：未実施項目と性能リスクを確認し、レビュー結果を返してください。ユーザーの明示指示を待ちます。
