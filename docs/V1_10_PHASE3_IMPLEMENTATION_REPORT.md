# Ver1.10 Phase 3 実装報告書

実施日: 2026-09-27

## 結論

**Phase 3完了・ユーザーレビュー待ち。Phase 4は開始していません。**

手動取込の部分登録を維持し、明示的なAtomic入口と共通コアを追加しました。最終Releaseテストは既存148件＋新規31件＝179件、Pass 179 / Fail 0 / Skip 0です。

## 1. 変更／追加ファイル

|区分|ファイル|内容|
|---|---|---|
|変更|`src/ManufacturingDataApp.Application/Services/CsvImportService.vb`|明示的なAtomic入口、共通コア、設備事前確認、ログ共通化|
|変更|`src/ManufacturingDataApp.Application/DTOs/CsvImportExecutionResultDto.vb`|エラー行数・正常保留行数を追加|
|追加|`src/ManufacturingDataApp.Application/DTOs/PeriodicFileResultDto.vb`|周期結果DTO・Outcome Enum|
|追加|`src/ManufacturingDataApp.Infrastructure/Logging/PeriodicImportLogWriter.vb`|周期ログDecorator|
|追加|`tests/ManufacturingDataApp.Tests/Infrastructure/AtomicCsvImportTests.vb`|一時DB結合テスト・例外注入・呼出回数検証、31ケース|
|追加|`docs/V1_10_PHASE3_IMPLEMENTATION_REPORT.md`|本報告書|

開始時点のファイルSHA-256と比較し、既存ファイルの変更は上記2ファイルのみと確認。既存ファイル削除なし。bin/obj/.gitは比較対象外です。既存テストファイルの変更・削除・Skip追加・Assertion変更はありません。

## 2. 実装内容

既存Parse／Mapping／Validation／CSV内重複を維持し、DB既存重複とログ処理を共有。周期専用入口では設備確認後にファイル単位All-or-Nothingを判定します。3引数・5引数の既存Constructorは維持し、設備Repositoryを受ける6引数Constructorを追加しました。Atomicで必要依存が不足する場合は読込・登録前にInvalidOperationExceptionとします。

## 3. 入口の契約

両入口とも引数はCsvImportRequestDto、戻り値はCsvImportExecutionResultDto。既存ReadRaw／Import／ImportAndSaveの公開署名を維持しています。

|入口|保存方針|正常3＋異常2のTotal / Success / Failure|DB登録|
|---|---|---|---|
|ImportAndSave|AllowPartial|5 / 3 / 2|3件|
|ImportAndSaveAtomic|AllOrNothing|5 / 0 / 5|0件|

ImportModeは保存方針に使いません。SingleFile／PeriodicFolderの両設定で両入口を検証しました。

## 4. 内部共通化

Private Enum SavePolicyとImportAndSaveCoreで保存方針を明示。Importを再利用し、ExcludeDatabaseDuplicates／RecordLogsを両入口から共有します。Optional Booleanの公開API追加やParse処理の複製はありません。未知MeasurementItemをエラー化せず、従来どおり登録できます。

## 5. DB既存重複

Search(EquipmentId, ItemName, MeasuredAt, MeasuredAt.AddTicks(1))を共通利用。テストのSpyでも検索時間幅1 tickを検証します。既存重複は取得日時／Duplicate／元行番号／ファイル名付きのエラー。手動ではその行のみ除外し、Atomicでは全行未登録です。既存DB行自体は削除しません。

## 6. Atomic設備確認

DB重複を除いた候補をIEquipmentRepository.FindByIdで照会。未知設備は設備ID／System／元行番号／ファイル名と「設備マスタに存在しない」メッセージを記録。設備確認はAtomicのみです。手動未知設備は従来どおりDBのFK例外になり、設備Repositoryを事前呼出ししないことを検証しました。

## 7. All-or-Nothing判定位置

全行Import → DB重複 → Atomic設備確認 → 行数整合性確認 → 保存判定の順です。候補行数＋Distinctエラー行数がSourceRowCountと一致しない、または行番号のないエラーがある場合は、登録前に内部不整合例外とします。正常保留行に偽エラーは追加しません。

## 8. AddRange呼出条件

Atomicはエラーありなら0回、正常な非空CSVなら1回、空CSV／ヘッダーのみなら0回です。手動は既存どおり保存可能候補で呼び出し、候補0件の既存呼出も維持しました。個別Addや登録後DELETEによる補償はありません。

## 9. 障害とValidationの分離

Parse、Search、設備Repository、AddRangeの例外を捕捉してRejectedへ変換していません。注入例外が同一インスタンスで上位へ伝播することを検証。ApplicationへのSQLite／CsvHelper／Infrastructure参照追加はありません。接続・commit・rollback・lock・I/Oの個別分類は後工程のInfrastructure Executorの責務です。

## 10. Transaction／Rollback

実MeasurementDataRepository.AddRangeの既存Transactionをそのまま使用。2行目で制約違反となる次の結合テストが成功しました。

- 設備確認後、登録前に2行目の設備を別接続から削除。1行目INSERT後、2行目FK違反で例外となり、このCSVの登録は0件。
- Search後、別接続で2行目と同一キーを先行登録。2行目UNIQUE違反で例外となり、1行目はロールバック。競合相手の既存1件だけが残ります。

設備削除は競合を再現するテスト前処理であり、登録失敗後の補償DELETEではありません。SpyのAdd／Deleteは例外となるため、取込側が個別登録・補償を行っていないことも検出できます。commit／rollback自体の失敗注入は未実施です。

## 11. 周期結果DTO

PeriodicFileResultDtoにOutcome、TotalCount、RegisteredCount、UnregisteredCount、ValidationErrorRowCount、HeldValidRowCount、LogRecorded、DbOutcomeKnownを追加。OutcomeはSucceeded／Rejected／ReadFailed／DatabaseFailed／Unknown。安全な初期値はUnknown・DbOutcomeKnown=Falseです。

CsvImportExecutionResultDtoの既存プロパティを残し、ValidationErrorRowCount（RowNumberのDistinct）とHeldValidRowCountを追加。Atomicの正常3＋異常2では5 / 0 / 5、エラー行2、正常保留3です。1行の複数エラーも行数としては1。将来のExecutorが既存実行結果から周期DTOへ変換する構成で、Phase 3ではExecutorや変換処理を実装していません。

## 12. ログ方式

共通RecordLogsからIImportLogWriterを呼出し、既存DbLogWriterを再利用。ErrorLogは実際のValidationエラーだけ記録（複数エラーのある同一行では複数レコード）。保留正常行のErrorLogは作りません。手動OperationTypeはCSV取込、PeriodicImportLogWriterで包むと周期CSV取込です。Decoratorは呼出元のOperationLogを変更せずコピーして委譲。Program.vbの接続変更なし。

## 13. ログ部分失敗

MeasurementDataのTransactionとは別にログを保存。WriteErrors成功→WriteOperation失敗でもDB件数・実行結果は維持し、LogRecorded=False、LogErrorMessage="ログの記録に失敗しました。"。手動部分成功、Atomic拒否、Atomic成功の3ケースで確認。WriteErrors／WriteOperationは各1回で、再試行・二重ErrorLogはありません。

## 14. 手動回帰

既存148件を変更せず全成功。新規でも正常3＋異常2の3件登録、PeriodicFolder設定での部分登録、未知設備のFK例外、手動ログ種別、ログ失敗時の結果保持を確認しました。

## 15. Atomic検証一覧

|指示書項目|検証内容|結果|
|---|---|---|
|A|正常3行、AddRange 1回、DB3件|成功|
|B|正常3＋異常2、AddRange 0回、5/0/5、エラー行2・保留3|成功|
|C|全行エラー、登録0件|成功|
|D|CSV内重複で全件拒否|成功|
|E|DB既存重複、行番号付き、追加登録0件|成功|
|F|Atomic未知設備事前検出／手動FK維持|成功|
|G|空CSV／ヘッダーのみ、正常0件|成功|
|H|実AddRangeの2行目FK失敗で全体Rollback|成功|
|I|手動3＋2は3件登録|成功|
|J|両ImportModeと両入口の独立|成功|
|K|ログ部分失敗、結果維持、二重記録なし|成功|
|L|Search／設備／AddRange例外伝播|成功|
|M|手動・周期のOperationType|成功|

追加でParse例外伝播、UNIQUE競合、6種類の構成不足、未知MeasurementItem許容、周期DTOの安全な初期値を検証しています。

## 16–19. テスト件数と最終結果

|区分|件数|Pass|Fail|Skip|
|---|---:|---:|---:|---:|
|既存（Ver1.00 71＋Phase 1 21＋Phase 2 56）|148|148|0|0|
|Phase 3新規（Theory展開後）|31|31|0|0|
|最終総数|179|179|0|0|

着手前に既存148件成功、変更後に新規31件の限定実行成功、最後に全179件成功を確認しました。

## 20. Release Build

最終検証は以下を順に実行し、すべて終了コード0でした。

```text
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --no-restore
```

Release Build: 成功、0エラー・3警告。Release Test: 179 Pass / 0 Fail / 0 Skip。アプリの実GUI操作は実施していません。

## 21. 警告

NU1900がrestoreで3件、buildで3件継続（Infrastructure／Presentation／Testsの3プロジェクト）。内容は https://api.nuget.org/v3/index.json のサービスインデックスへアクセスできず、パッケージ脆弱性データの取得に失敗したものです。権限付きrestore再試行でも継続しました。ビルド・テスト成功とは別に、脆弱性情報のオンライン確認は未完了です。パッケージ更新や警告抑制は行っていません。

## 22. Project／Package／Reference差分

すべてなし。5プロジェクト維持、NuGet追加・バージョン変更なし、DIコンテナ追加なし。新規VBファイルは既存SDKプロジェクトの標準ファイル包含でコンパイルされます。

## 23. Phase 1への変更

なし。PeriodicImportService／Status／Scheduler等は変更していません。累積SkippedCyclesによりMessageにスキップ文言が残る可能性は、指示どおりPhase 5対応として残しました。

## 24. Phase 2への変更

なし。Migration／Schema／Initializer／Config／Repository／ImportMode／Persistence testsは変更していません。新規テストから既存Phase2Databaseの一時DB作成・後始末機能のみ再利用しています。

## 25. 実ユーザーDB

接続していません。全新規結合テストは一意の一時ディレクトリと明示的なtest.dbパスを使用。Using終了時にSqliteConnection.ClearAllPools後、一時ディレクトリを削除します。アプリ本体は起動していません。

## 26. UI／配布物

UI、MainForm、Program.vb、USER_GUIDE、dist、Publish、Package作成は未実施・未変更です。ビルド生成物bin/objのみ通常の検証で更新されています。

## 27. 設計との差分

要件・設計変更なし。実装上の具体化としてPrivate SavePolicy、6引数Constructor、既存実行結果DTOへの互換な2項目追加、周期Outcome Enumの配置・Unknown初期値を採用しました。責務分離を維持し、後工程の例外分類・周期DTO変換・本番DIは実装していません。

## 28. 残リスク／未実装

- 事前Search・設備確認からAddRangeまでの競合は排除できません。最終防御は既存UNIQUE/FKとTransaction。競合をValidation拒否へ偽装しないことを検証済みです。
- commit／rollbackの失敗、プロセス強制終了、実環境I/O障害などは本テストでは再現していません。DB結果が不明な状況を扱うExecutor／Journal／Recoveryは後工程です。
- ログ失敗時の再試行やDB結果の反転はしません。ログが欠落する可能性はLogRecorded=Falseで通知します。
- FileStore／Lease／Journal／Attempt／Order／CorrectionBinding／J-01実処理／success・error移動／UIは未実装のままです。
- NU1900による脆弱性情報未取得、およびPhase 1既知Message事項は残ります。

## 29. Phase 4への判断

Phase 3の指定完了条件は満たしています。おすすめは本報告書と差分のユーザーレビューです。理由は、Phase 4ではファイル操作・順序・復旧という別の責務へ進むためです。今すぐ行うことはレビューであり、承認・次の指示があるまでPhase 4を開始しません。
