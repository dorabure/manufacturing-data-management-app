# 結合テスト対応表 Ver1.10 Phase 7

基本設計T01～T78と実テストの対応です。各テスト本文とAssertionを確認し、名前の類似だけではPASSにしていません。最終Releaseは353件×3回、各Fail0/Skip0。旧330件を変更せず23件追加しています。

PASSは備考に書いた自動確認範囲の成功です。MANUALは環境・実操作待ち、PENDINGは要求の一部について専用試験が未実施です。「PASS / PENDING」等は要求全体の合格ではありません。実機合格は0件、N/Aによる除外はありません。GUIの自動Control試験と目視は別です。

実テスト名はクラス.メソッドで記載（同名.vbをtests/ManufacturingDataApp.Tests配下から検索可能）。セミコロン区切りは複数の補完テストです。理論テストの複数引数はxUnit件数に含みます。例外注入とサービス再生成は実プロセス強制終了ではありません。

| T-ID | 要求シナリオ | 対応する実テスト名 | 自動／手動／実機 | 既存／Phase7追加 | 結果 | Assertion・備考 |
|---|---|---|---|---|---|---|
| T01 | 固定周期初回・二重Start | `PeriodicImportServiceTests.T01_StartAsync_ImmediatelyExecutesOnce_AndRejectsDuplicateStart` | 自動 | 既存 | PASS | 時刻前進なしでExecute1、二重Start拒否 |
| T02 | 周期下限／上限 | `PeriodicImportServiceTests.T02_IntervalBoundary_OnlyRunsWhenDue` | 自動 | 既存 | PASS | 1／604800秒の直前0・境界1、仮想時刻 |
| T03 | 不正周期 | `PeriodicImportServiceTests.T03_InvalidInterval_RejectsBeforePreparation`<br>`Phase5SettingsTests.InvalidIntervalRejectedBeforeSave` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | 0・負数・上限超えで準備／保存なし。小数・空の実UI入力は未実施 |
| T04 | 遅い周期と並行防止 | `PeriodicImportServiceTests.T04_ActiveCycle_SkipsTen_CompletesAtFifteen_RunsAtTwenty` | 自動 | 既存 | PASS | 0開始・10skip・15完了・20開始、並行1 |
| T05 | 遅延／壁時計変更 | `PeriodicImportServiceTests.T05_DelayedScheduler_CoalescesMissedTicks_AndIgnoresWallClock` | 自動 | 既存 | PASS | 遅れを集約、壁時計に依存しない |
| T06 | 空フォルダ多数周期 | `PeriodicImportServiceTests.T06_NoWork_ManyPeriods_KeepOneTimerAndNoQueuedWork`<br>`Phase5NotificationTests.LifecycleLogsOncePerSessionNotPerEmptyCycle` | 自動 | 既存 | PASS | タイマー1、キュー増殖なし、ログはセッション単位 |
| T07 | 複数CSV順序 | `Phase7EndToEndTests.MainFormSettingsStartThreeFilesLogsSearchThenClose`<br>`Phase4BoundaryTests.OrdinalTieBreaker_IsDeterministic` | 自動 | 既存＋追加 | PASS | A/B/C、DB3・success3・Completed3・Cursor3・取込ログ合計3、同時刻Ordinal |
| T08 | 対象拡張子・非再帰 | `Phase4ImportTests.NormalOrder_Extensions_AndDurableStages` | 自動 | 既存 | PASS | csv/CSV/CsVのみ、csvx・txt・下位対象外 |
| T09 | 途中追加／消失／置換 | `Phase4ImportTests.NewOldMtimeArrival_WaitsForNextOrder`<br>`Phase4PathAndJournalTests.SourceReplacedAfterDbBeforeMove_RequiresRecovery` | 自動＋未実施 | 既存 | PASS / PENDING | 追加は次Order、DB後置換でRecovery。確定後DB前の元CSV消失・置換の専用ケース未追加 |
| T10 | 全行正常 | `AtomicCsvImportTests.Atomic_AllValid_SavesOnce`<br>`Phase7EndToEndTests.MainFormSettingsStartThreeFilesLogsSearchThenClose` | 自動 | 既存＋追加 | PASS | AddRange1、DB・success・ログ件数一致 |
| T11 | 正常3異常2 | `AtomicCsvImportTests.MixedRows_EntryNotModeDeterminesPolicy`<br>`Phase4BoundaryTests.ValidationFailure_AtomicAndFollowingStopped` | 自動 | 既存 | PASS | Atomic AddRange0、5/0/5、不正2・保留3、error・後続停止 |
| T12 | 全異常・重複 | `AtomicCsvImportTests.Atomic_AllInvalid_NeverCallsAddRange`<br>`Phase4BoundaryTests.ValidationFailure_AtomicAndFollowingStopped` | 自動 | 既存 | PASS | 全異常AddRange0、csvdup/dbdupは全件拒否・error・後続なし |
| T13 | 空／Headerのみ | `Phase4ImportTests.EmptyCsvIsSuccess_NotEmptyFolder`<br>`AtomicCsvImportTests.Atomic_EmptyAndHeaderOnly_AreSuccessful` | 自動 | 既存 | PASS | 正常0件success、2回目追加呼出しなし |
| T14 | 移動先事前準備 | `Phase4ImportTests.NormalOrder_Extensions_AndDurableStages`<br>`Phase4ImportTests.PreparationFailure_NoDbOrSourceChange` | 自動 | 既存 | PASS | 未作成フォルダ準備後実行。準備失敗でAtomic0 |
| T15 | 移動先衝突 | `Phase4BoundaryTests.DestinationCollision_NeverOverwrites_HandlesRace`<br>`Phase4BoundaryTests.OneHundredCollisions_StopWithoutOverwriteOrReimport` | 自動＋未実施 | 既存 | PASS / PENDING | success衝突とレース・100回上限・上書きなし。error側同名衝突専用ケースは未実施 |
| T16 | write handle保持 | `Phase7PublicationTests.DirectCsvStableWhileOpenRemainsWaitingAcrossRestart` | 自動 | 追加 | PASS | FileShare.None/Read、複数周期A/B/C保留、Running、DB0/error0 |
| T17 | Close後元順序 | `Phase7PublicationTests.DirectCsvStableWhileOpenRemainsWaitingAcrossRestart` | 自動 | 追加 | PASS | writer Close後Aのmtimeを未来へ、A/B/C各1回 |
| T18 | read guard | `Phase4ImportTests.ReadLease_DeniesWriteDeleteRename_AllowsExistingAdapter` | 自動 | 既存 | PASS | 追記・削除・rename拒否、既存Adapter読取可 |
| T19 | 実SCADA公開契約 | — | 実機 | 実機手順追加 | MANUAL | 環境なし。SCADA手順S01～S09をユーザー実施 |
| T20 | Stop／tick競合 | `PeriodicImportServiceTests.T20_StopWinsBeforeTick_NoNewCycleStarts`<br>`PeriodicImportServiceTests.T20_TickWinsBeforeStop_StopWaitsForAcceptedCycle` | 自動 | 既存 | PASS | Stop先行は0新規、tick先行は受理済み完了待ち |
| T21 | モード・開始停止UI | `Phase5MainFormTests.T21_ModeSaveRecreationNeverAutoStarts`<br>`Phase5PresenterTests.T21_T22_Policy` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | 保存・再生成後Stopped、状態別Policy。目視は未実施 |
| T22 | 監視中入口防御 | `Phase5MainFormTests.T22_ActualControlsAndEventGuards` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | Starting/Running/StoppingのEnabledと直接イベント呼出し。実クリックは未実施 |
| T23 | 通知・履歴 | `Phase5PresenterTests.T23_BoundedQueueHistoryAndCriticalRetention`<br>`Phase5PresenterTests.T23_ControlCharactersLengthAndSelection`<br>`Phase5MainFormTests.T23_NotificationDoesNotSearchOrChangeSelection` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | 100件、512字、制御文字、選択維持、検索非実行。表示倍率別は未実施 |
| T24 | 待機・開始準備停止 | `PeriodicImportServiceTests.StopAsync_WhileWaiting_CancelsDelay_AndCanRestart`<br>`Phase4ImportTests.StopDuringPrepare_ReleasesOwnership_AndCanRestart` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | 遅延取消、準備完了待ち・所有解放・再開始。実画面未実施 |
| T25 | 処理中停止 | `Phase6ClosingIntegrationTests.CloseDuringRealPipelineCompletesAOnlyReleasesOwnerAndCanRestart` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | Atomic/Validation/Transaction/Journal/Move/Cleanup各ゲート、Aのみ完了・次Bなし・所有再取得 |
| T26 | FormClosing・連打・Dispose | `Phase6MainFormTests.ActualFormClosingCancelsThenPostsExactlyOneUiClose`<br>`Phase6MainFormTests.DisposedFormDoesNotReceiveDeferredCloseOrNotification`<br>`Phase6ExitControllerTests.TenCloseRequestsJoinOneStop` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | Close1・Stop1・終了待ち、破棄後通知なし。実デスクトップ未実施 |
| T27 | Root削除／ACL変更 | `PeriodicImportServiceTests.CycleFailure_IsObserved_AutomaticallyStopsWithoutSelfAwait` | 自動＋未実施 | 既存（部分） | PENDING | 例外通知・停止はPASS。実フォルダ削除／権限変更を通す専用試験未実施 |
| T28 | success/error作成不可 | `Phase4ImportTests.PreparationFailure_NoDbOrSourceChange` | 自動 | 既存 | PASS | 同名ファイルで作成失敗、Atomic0・元bytes不変 |
| T29 | INSERT障害・locked・容量 | `AtomicCsvImportTests.Atomic_RealTransactionSecondInsertFailure_RollsBackFirst`<br>`Phase7EndToEndTests.FatalRepositoryBoundaryNeverMovesToErrorOrRetries` | 自動＋未実施 | 既存＋追加 | PASS / PENDING | 実UNIQUE/FK・rollback、lockedはSqliteException注入。物理disk full・実接続間lock待ちは未実施 |
| T30 | DB後Move失敗 | `Phase4RecoveryTests.MoveFailure_RestartMovesOnlyAndStops` | 自動 | 既存 | PASS | DB1のままMovePending、移動のみ復旧 |
| T31 | MovePending再起動 | `Phase4RecoveryTests.RealJsonRestart_NeverReexecutesAtomic` | 自動 | 既存 | PASS | 永続JSON再読込、既処理に追加Atomic0。サービス再生成 |
| T32 | ImportStarted直後crash | `Phase4RecoveryTests.RealJsonRestart_NeverReexecutesAtomic` | 自動＋未実施 | 既存 | PASS / PENDING | 境界例外注入でRecoveryRequired、追加Atomic0。子プロセス強制終了未実施 |
| T33 | commit後結果保存前crash | `Phase4RecoveryTests.RealJsonRestart_NeverReexecutesAtomic`<br>`Phase7EndToEndTests.FatalRepositoryBoundaryNeverMovesToErrorOrRetries` | 自動＋未実施 | 既存＋追加 | PASS / PENDING | commit-unknownは実commit後例外、DB1・再生成Atomic0。OS強制終了未実施 |
| T34 | Move後Completed前crash | `Phase4RecoveryTests.MovedBeforeCompleted_RepairsFromDestinationOnly` | 自動＋未実施 | 既存 | PASS / PENDING | 移動先指紋から修復、Atomic増加なし。OS強制終了未実施 |
| T35 | Journal破損・書込不能・置換 | `Phase4RecoveryTests.JournalInconsistency_DoesNotBecomeEmptyOrRetry`<br>`Phase4PathAndJournalTests.SaveImportStartedFailure_PreventsDbCall`<br>`Phase4PathAndJournalTests.SourceReplacedAfterDbBeforeMove_RequiresRecovery` | 自動＋未実施 | 既存 | PASS / PENDING | 不整合で保留、保存失敗Atomic0。disk fullはIOException注入、物理容量／ACL未実施。痕跡なし削除保証外 |
| T36 | 物理状態不明 | `Phase4RecoveryTests.MoveRecovery_AmbiguousPhysicalStateRequiresRecovery` | 自動 | 既存 | PASS | both/neither/mismatch、RecoveryRequired・再実行なし |
| T37 | ログ部分失敗 | `Phase4BoundaryTests.LogPartialFailure_MovesThenStops_NoRepeatErrors`<br>`AtomicCsvImportTests.OperationLogFailure_PreservesDbResultAndDoesNotRepeatErrors` | 自動 | 既存 | PASS | LogRecorded=False、DB結果維持、行エラー再記録なし |
| T38 | DB全体・ErrorLog障害 | `Phase5NotificationTests.LoggingFailureDoesNotRecursivelyLogOrFailImportState`<br>`Phase7EndToEndTests.FatalRepositoryBoundaryNeverMovesToErrorOrRetries` | 自動＋未実施 | 既存＋追加（部分） | PENDING | ライフサイクルログ失敗とDB境界は個別PASS。ErrorLogも同時不能な全体E2E未実施 |
| T39 | A正常B拒否C保留 | `Phase4ImportTests.RejectedHead_StopsAfterPriorSuccess_AndDoesNotAutoBindSameName`<br>`Phase4BoundaryTests.ActualBadCsv_ParseFailure_IsReadFailed`<br>`Phase7EndToEndTests.SchedulerCorrectionBindingPreservesSnapshotFingerprintAndChain` | 自動 | 既存＋追加 | PASS | Aのみ登録、B error・CorrectionPending、C未処理、同名自動対応なし。解析不正分類は別テスト |
| T40 | A正常B致命C保留 | `Phase4ImportTests.UnknownDb_KeepsSourceAndRejectsBinding`<br>`Phase7EndToEndTests.FatalRepositoryBoundaryNeverMovesToErrorOrRetries` | 自動 | 既存＋追加 | PASS | 既存A維持、B元CSV/error0・RecoveryRequired、Cなし。新規境界8種類 |
| T41 | 二重所有 | `Phase7EndToEndTests.TwoSchedulersRespectActualOwnerLease`<br>`Phase4ImportTests.Ownership_IsHandleBased_NotLockFileExistence` | 自動 | 既存＋追加 | PASS | SchedulerA所有→B拒否→A停止→B開始。存在だけに依存しない |
| T42 | 境界パス・junction | `Phase4BoundaryTests.UnsafeRelativePath_RejectedWithoutIoOutsideRoot`<br>`Phase4PathAndJournalTests.JunctionPaths_AreRejectedWithoutTargetMutation` | 自動 | 既存 | PASS | ../・兄弟prefix拒否、実junction各位置、外側変更なし |
| T43 | Config独立・再起動 | `MonitoringConfigPersistenceTests.T43_T60_SettingsSurviveReopen_AreIndependent_AndSingleFileKeepsValues`<br>`Phase5MainFormTests.T43_TwoSavedConfigsRestoreIndependentlyInActualControls`<br>`Phase5SettingsTests.SaveFailurePreventsOptionsAndSchedulerStart` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | 設定別復元・自動Start0、保存失敗開始0。画面目視未実施 |
| T44 | Ver1.00 Migration | `DatabaseMigrationTests.T44_LegacyMigration_PreservesAllSevenTablesAndIds_AndValidBackup`<br>`Phase7EndToEndTests.LegacyMigrationThroughMainFormStartPreservesOriginalData` | 自動 | 既存＋追加 | PASS | 7表値ID・backup、旧CSV形式維持、UI手動開始・追加データ、Seed再投入なし |
| T45 | 性能 | `Phase7PerformanceTests.TenThousandRows_UiRespondsAndCloseWaitsForAtomic`<br>`Phase7PerformanceTests.CsvBatch_OrderedSingleWorkerDurableAndNoDuplicates` | 自動＋手動／実機待ち | 追加 | PASS / MANUAL | 正しさPASS、10000行／1000 CSV実測記録。通常回帰100 CSV。性能閾値未合意・実機UI/低速装置未確認 |
| T46 | 既存回帰 | `既存330ケース（Ver1.00の71ケースを含む）` | 自動 | 既存 | PASS | 既存テストファイルhash不変、353件全実行3回 |
| T47 | 手動・検索・詳細・編集・削除 | `BusinessFlowIntegrationTests.SearchIds_FindDetailsAndDeleteOneOfThreePersistedRecords`<br>`BusinessFlowIntegrationTests.EditSearchAndLog_PersistsOnlyTargetChange`<br>`BusinessFlowIntegrationTests.DeleteMultipleSearchAndLog_DeletesOnlySpecifiedRecords`<br>`Phase7EndToEndTests.MainFormSettingsStartThreeFilesLogsSearchThenClose` | 自動＋手動／実機待ち | 既存＋追加 | PASS / MANUAL | Service/DB対象ID・ログ一致。Main実Control検索クリア。全画面クリックは未実施 |
| T48 | 出力・設定・マスタ・ログ | `BusinessFlowIntegrationTests.ExportSearchResultsAndLog_WritesCsvAndOperationLog`<br>`CsvConfigIntegrationTests.DatabaseConfig_ControlsCsvExportColumnOrder`<br>`Phase7DateFilterTests.UncheckedLogDatesDoNotBecomeMinValue` | 自動＋手動／実機待ち | 既存＋追加 | PASS / MANUAL | CSV値・ログ、列順、両ログ日付未指定1件。旧16シナリオも参照。目視未実施 |
| T49 | 同名修正・DB復元手順 | `Phase4ImportTests.RejectedHead_StopsAfterPriorSuccess_AndDoesNotAutoBindSameName`<br>`Phase5RecoveryTests.QueryAndPreviewDoNotBindOrImport` | 自動＋手動／実機待ち | 既存＋文書追加 | PASS / MANUAL | 同名だけで再開しない。DB復元一式整合手順をUSER_GUIDEへ、実復元演習未実施 |
| T50 | 長時間・大量skip | `Phase7PerformanceTests.LongElapsedAndTenThousandSkipsKeepNotificationsAndLogsBounded`<br>`Phase5PresenterTests.T23_SkipAggregationAcrossDrainsDoesNotReplaceCurrentState` | 自動 | 既存＋追加 | PASS | 10000skip＋3650日経過、集約・queue<=100・状態非上書き・ログ開始停止2 |
| T51 | 手動3/5部分登録 | `AtomicCsvImportTests.MixedRows_EntryNotModeDeterminesPolicy` | 自動 | 既存 | PASS | 手動DB3・5/3/2、モードを変えても入口優先 |
| T52 | 修正版→後続 | `Phase7EndToEndTests.SchedulerCorrectionBindingPreservesSnapshotFingerprintAndChain` | 自動 | 追加 | PASS | 保存後Stopped/追加呼出しなし、Start後B2→C、DB3 |
| T53 | 未知設備・重複 | `AtomicCsvImportTests.Atomic_UnknownEquipment_IsRowError`<br>`AtomicCsvImportTests.Manual_UnknownEquipment_StillThrowsForeignKeyWithoutPrecheck`<br>`Phase4BoundaryTests.ValidationFailure_AtomicAndFollowingStopped` | 自動 | 既存 | PASS | 未知設備Atomic0と手動FK維持、DB/CSV重複拒否 |
| T54 | 途中INSERT rollback | `AtomicCsvImportTests.Atomic_RealTransactionSecondInsertFailure_RollsBackFirst` | 自動 | 既存 | PASS | 実UNIQUE/FK、先行行も残らない。Transactionによるrollback |
| T55 | Migration反復・中断 | `DatabaseMigrationTests.T55_InitializeTwice_DoesNotRepeatMigrationBackupOrSeed`<br>`DatabaseMigrationTests.T55_FailureDuringMigration_RollsBackColumnsVersionAndPreservesData` | 自動＋未実施 | 既存 | PASS / PENDING | 4地点例外注入で原本・版・列rollback、反復backup/Seed増なし。子プロセスkill未実施 |
| T56 | backup・未知Schema | `DatabaseMigrationTests.T56_BackupFailure_DoesNotAlterSource`<br>`DatabaseMigrationTests.T56_UnknownOrPartialSchema_RejectsWithoutRepair`<br>`DatabaseMigrationTests.T56_CorruptFile_IsNotRecreated`<br>`DatabaseMigrationTests.T56_AllNewColumnsWithVersionZero_RefusesToGuessMigrationState` | 自動 | 既存 | PASS | 原本不変、推測修復なし・例外。起動エラーの実画面は未確認 |
| T57 | 新規DB初期化 | `DatabaseMigrationTests.T57_NewOrEmptyDatabase_SeedsOnlyOnce`<br>`DatabaseMigrationTests.T57_NewInitializationFailure_RollsBackTablesSeedAndVersion` | 自動 | 既存 | PASS | 版110・初期値・Seed1回、失敗全体rollback |
| T58 | 形式／監視設定独立 | `MonitoringConfigPersistenceTests.T58_FormatSaveUsingFreshEntity_PreservesMonitoring_AndMonitoringPreservesMappingIds`<br>`MonitoringConfigPersistenceTests.T58_NewConfig_UsesDatabaseDefaults_NotEntityMonitoringValues`<br>`Phase5SettingsTests.SaveReloadMappingsOrderAndFormatIndependence` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | Mapping ID・形式保持、新規SingleFile/NULL/60。全画面実操作未実施 |
| T59 | 保存値制約 | `MonitoringConfigPersistenceTests.T59_DatabaseConstraints_RejectInvalidStorage`<br>`MonitoringConfigPersistenceTests.T59_CorruptStoredValues_AreNotSilentlyCoerced`<br>`MonitoringConfigPersistenceTests.T59_BoundariesAndUnsetFolder_AreAllowedWithoutFilesystemAccess`<br>`Phase5SettingsTests.MissingConfigOrSingleModeCannotStart` | 自動 | 既存 | PASS | 不正mode/秒/小数/NULL拒否、未設定フォルダ保存可・開始時別検証 |
| T60 | 未保存設定切替 | `Phase5SettingsTests.DirtySwitchPreservesOriginalTarget`<br>`Phase5SettingsTests.SaveFailurePreventsSwitchAndPreservesInput`<br>`MonitoringConfigPersistenceTests.T43_T60_SettingsSurviveReopen_AreIndependent_AndSingleFileKeepsValues` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | Save/Discard/Cancel、別Config汚染なし。実操作未実施 |
| T61 | ネットワーク先拒否 | `Phase4BoundaryTests.UnsupportedRoot_Rejected` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | UNC・拡張prefix・相対拒否。実SMB/NAS/割当ドライブ環境未実施 |
| T62 | 異常tick＋error Move失敗 | `Phase7PublicationTests.RejectMoveFailureWithTickRecoversMoveOnlyAndNeverStartsFollowing` | 自動 | 追加 | PASS | skip1・MovePending→移動だけ、再生成Atomic0・CorrectionPending、C元のまま |
| T63 | 正常0行と空フォルダ | `Phase4ImportTests.EmptyCsvIsSuccess_NotEmptyFolder`<br>`AtomicCsvImportTests.Atomic_EmptyAndHeaderOnly_AreSuccessful`<br>`Phase5NotificationTests.LifecycleLogsOncePerSessionNotPerEmptyCycle` | 自動 | 既存 | PASS | 0行成功・success・結果記録、空周期は取込なし。ライフサイクルログは別 |
| T64 | 厳密元順序 | `Phase7EndToEndTests.SchedulerCorrectionBindingPreservesSnapshotFingerprintAndChain`<br>`Phase7PublicationTests.DirectCsvStableWhileOpenRemainsWaitingAcrossRestart` | 自動 | 追加 | PASS | 修正版未来mtimeでも元位置、書込み先頭を追越さない |
| T65 | 先頭書込み初回 | `Phase7PublicationTests.DirectCsvStableWhileOpenRemainsWaitingAcrossRestart` | 自動 | 追加 | PASS | 3周期write handle保持、全DB0・errorなし・Running |
| T66 | 先頭完成でmtime逆転 | `Phase7PublicationTests.DirectCsvStableWhileOpenRemainsWaitingAcrossRestart` | 自動 | 追加 | PASS | writer/test側がmtime変更、アプリはA/B/C保存順 |
| T67 | 拒否後再起動 | `Phase4ImportTests.RejectedHead_StopsAfterPriorSuccess_AndDoesNotAutoBindSameName` | 自動 | 既存 | PASS | 再生成後A維持、B CorrectionPending、Cなし |
| T68 | 明示確認・関連履歴 | `Phase7EndToEndTests.SchedulerCorrectionBindingPreservesSnapshotFingerprintAndChain` | 自動 | 追加 | PASS | BindingId・指紋・SnapshotHash・ConfigHash・元Order位置・新Attemptを照合 |
| T69 | 修正版再失敗 | `Phase7EndToEndTests.SchedulerCorrectionBindingPreservesSnapshotFingerprintAndChain` | 自動 | 追加 | PASS | rejectAgain=True、DBはAの1行のみ、B2 error、Root/Latest失敗ID・C保留 |
| T70 | 未選択・取消・不明 | `Phase5MainFormTests.T70_RecoveryDialogStartsUnselectedAndCancelDoesNothing`<br>`Phase5RecoveryTests.QueryAndPreviewDoNotBindOrImport` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | 未選択・保存不可、CancelでBinding/Atomic0。目視未実施 |
| T71 | DB不明は修正不可 | `Phase4ImportTests.UnknownDb_KeepsSourceAndRejectsBinding`<br>`Phase5RecoveryTests.RecoveryRequiredIsNotCorrectionPending`<br>`Phase7EndToEndTests.FatalRepositoryBoundaryNeverMovesToErrorOrRetries` | 自動 | 既存＋追加 | PASS | commit-unknownも元維持/error0、対応付け不可、再Startで追加Atomic0 |
| T72 | 書込み待ち再起動 | `Phase7PublicationTests.DirectCsvStableWhileOpenRemainsWaitingAcrossRestart` | 自動 | 追加 | PASS | 新SchedulerはStopped、手動Startで同じOrder、writer解放後元順序 |
| T73 | 拒否Completed後crash | `Phase4RecoveryTests.CompletedRejectWithoutOrderUpdate_RestoresCorrectionPending` | 自動＋未実施 | 既存 | PASS / PENDING | 永続記録からCorrectionPending修復、再実行なし。子プロセスkill未実施 |
| T74 | 修正版完了Cursor前crash | `Phase4RecoveryTests.CorrectedSuccessCompletedBeforeCursor_RepairsWithoutReuse` | 自動＋未実施 | 既存 | PASS / PENDING | Completed関連からCursor修復、修正版再呼出しなし。子プロセスkill未実施 |
| T75 | 候補変更・禁止候補・lock | `Phase4ImportTests.ChangedBinding_RequiresReconfirmationWithoutAtomic`<br>`Phase4ImportTests.ForbiddenCorrectionCandidates_AreRejected`<br>`Phase4ImportTests.BoundCorrectionSharingViolation_HoldsFollowingCsv`<br>`Phase5RecoveryTests.InvalidNewCandidateStillInvalidatesOldBinding` | 自動＋手動／実機待ち | 既存 | PASS / MANUAL | 内容/設定/missing再確認、禁止候補拒否、lock保留、旧Binding無効化。実ダイアログ未実施 |
| T76 | 保持期限 | `Phase4RecoveryTests.Retention_ReferencedCompletedKept_UntilThirtyDaysAfterResolution` | 自動 | 既存 | PASS | 未解決40日保持、解決後29日保持31日削除、CSV維持 |
| T77 | 古い新着追越し禁止 | `Phase4ImportTests.NewOldMtimeArrival_WaitsForNextOrder` | 自動 | 既存 | PASS | 既存順序完了後、次周期の新Order |
| T78 | Order不整合 | `Phase4RecoveryTests.JournalInconsistency_DoesNotBecomeEmptyOrRetry`<br>`Phase4PathAndJournalTests.OrphanUnresolvedAttempt_BlocksCorrectionBinding` | 自動 | 既存 | PASS | 欠落/複数/cursor/hash等でRecovery、元CSV維持・追加Atomic0 |

## 未実施範囲の扱い

- 実UI操作・DPI：UI_LAYOUT_CHECK.mdの8画面×3倍率。SCADA：V1_10_SCADA_MANUAL_VERIFICATION.md。
- T09のDB前消失／置換、T15のerror側衝突、T27の実Root削除／ACL、T38のDBとErrorLog同時障害は、専用自動試験の追加または隔離環境試験が残ります。
- T29/35の物理容量不足・実ACL、T61の実ネットワーク割当は隔離環境で確認します。本番のディスクや権限を変えて試験しません。
- T32/33/34/55/73/74の強制終了は、専用子プロセスを用い境界ゲートで終了し、一時DB・Journalを別プロセスで再読込する追加検証が必要です。今回の例外注入を実施済みクラッシュ試験とは扱いません。
- T45は正しさと測定値のみの確認です。速度合格・メモリリーク完全否定・実デスクトップ応答を宣言しません。
- DB差替えや管理記録の痕跡なき削除の完全検出は保証外。USER_GUIDEに保全・復元時の一式整合を明記しています。

## Ver1.00の既存16シナリオ（維持）

以下のE2EはService／実SQLite／CSVを結ぶ自動試験を意味し、画面の全クリック経路を意味しません。


| ID | シナリオ | 対応テスト | 区分 | 結果 |
|---|---|---|---|---|
| INT-FLOW-001 | CSV取込→DB→検索→ログ | BusinessFlowIntegrationTests.ImportNormalCsv_PersistsSearchesAndWritesOperationLog（既存のCsvImportPersistenceTestsも補完） | E2E | PASS |
| INT-FLOW-002 | 正常＋異常CSV→ErrorLog→OperationLog | BusinessFlowIntegrationTests.ImportMixedCsv_PersistsErrorsAndOnlyValidMeasurements | E2E | PASS |
| INT-FLOW-003 | CSV内重複 | CsvImportServiceTests.Import_SeparatesValidAndInvalidRows | 既存 | PASS |
| INT-FLOW-004 | DB既存重複 | CsvImportPersistenceTests.ImportAndSave_ExistingDatabaseDuplicate_IsLoggedWithSourceRowNumber | 既存 | PASS |
| INT-FLOW-005 | 設定→取込 | CsvConfigIntegrationTests.DatabaseConfig_ControlsCsvImportColumnOrder | 既存 | PASS |
| INT-FLOW-006 | Min/Max→CSV検証 | MasterDataServiceTests.MeasurementItem_ValidationAndRangeChanges_AffectImportAndEdit | 既存 | PASS |
| INT-FLOW-007 | 検索→編集→再検索→ログ | BusinessFlowIntegrationTests.EditSearchAndLog_PersistsOnlyTargetChange | E2E | PASS |
| INT-FLOW-008 | 編集Validation | MeasurementDataServiceTests.ValidateForUpdate_RejectsRequiredTypeAndRangeErrorsWithoutUpdating | 既存 | PASS |
| INT-FLOW-009 | 複数削除→再検索→ログ | BusinessFlowIntegrationTests.DeleteMultipleSearchAndLog_DeletesOnlySpecifiedRecords | E2E | PASS |
| INT-FLOW-010 | CSV出力→再読込 | CsvExportServiceTests.Export_Utf8AndSpecialCharacters_RoundTripsThroughExistingReader（出力ログはBusinessFlowIntegrationTests.ExportSearchResultsAndLog_WritesCsvAndOperationLog） | 既存 + E2E | PASS |
| INT-FLOW-011 | 設定変更→出力 | CsvConfigIntegrationTests.DatabaseConfig_ControlsCsvExportColumnOrder | 既存 | PASS |
| INT-FLOW-012 | 使用中設備削除 | MasterDataServiceTests.Equipment_RejectsInvalidDuplicateMissingAndReferencedOperations | 既存 | PASS |
| INT-FLOW-013 | 使用中項目削除 | MasterDataServiceTests.MeasurementItem_Delete_RejectsItemUsedByMeasurementDataWithoutChangingData | 既存 | PASS |
| INT-FLOW-014 | マスタ変更→編集検証 | MasterDataServiceTests.MeasurementItem_ValidationAndRangeChanges_AffectImportAndEdit | 既存 | PASS |
| INT-FLOW-015 | 業務操作→ログ閲覧 | BusinessFlowIntegrationTestsのCSV取込・編集・削除・出力フロー、およびLogRepositoryTests | E2E + 既存 | PASS |
| INT-FLOW-016 | 日付境界 | RepositoryTests.MeasurementData_Search_AppliesAllSpecifiedConditionsAndDateBoundaries / LogRepositoryTests | 既存 | PASS |

すべてのテストはGUID付き一時ディレクトリ配下のSQLite DBを使用し、LocalAppDataの本番DBを使用しません。
