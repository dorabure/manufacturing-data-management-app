# Ver1.10 Phase 2 実装報告書

状態：Phase 2完了・ユーザーレビュー待ち（Phase 3未着手）
検証日：2026-09-27

## 結論

CsvImportConfigの監視設定保存と、Ver1.00からVer1.10への追加専用Migrationを実装した。
Release build成功、全148ケース成功（既存92＋新規56、Fail 0、Skip 0）。
検証は一時DBのみ。実ユーザーDBへ接続・適用していない。Publish・dist更新なし。

## 1. 変更／追加ファイル（報告項目1）

リポジトリルートからの相対パス。

### 変更7ファイル

| ファイル | 変更 |
|---|---|
| src/ManufacturingDataApp.Domain/Entities/CsvImportConfig.vb | 監視3プロパティ・既定値 |
| src/ManufacturingDataApp.Application/Interfaces/ICsvImportConfigRepository.vb | SaveMonitoringSettings契約 |
| src/ManufacturingDataApp.Application/Services/CsvConfigService.vb | 監視設定入口・検証・パス正規化 |
| src/ManufacturingDataApp.Infrastructure/Repositories/CsvImportConfigRepository.vb | SELECT／Map拡張・監視UPDATE・SQL参照 |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseSchema.vb | 新規DB用3列追加 |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseInitializer.vb | Migratorへ状態判定・初期化を委譲 |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseConnectionFactory.vb | Open／PRAGMA失敗時にも接続をDisposeして例外を再送出 |

### 追加11ファイル

| ファイル | 内容 |
|---|---|
| src/ManufacturingDataApp.Domain/Constants/ImportMode.vb | SingleFile／PeriodicFolder |
| src/ManufacturingDataApp.Infrastructure/Data/CsvImportConfigSql.vb | Config／Mappingの対象SQL集約 |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseMigrator.vb | 初期化・バックアップ・Migration調整 |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseMigrations.vb | 旧Config定義・3列追加＋user_versionのSQL |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseSchemaValidator.vb | Schema・整合性検証 |
| src/ManufacturingDataApp.Infrastructure/AssemblyInfo.vb | テストAssemblyへのFriend境界公開 |
| tests/ManufacturingDataApp.Tests/TestSupport/LegacyDatabaseSchema.vb | 変更前のVer1.00 Schema／Seedを固定したfixture |
| tests/ManufacturingDataApp.Tests/TestSupport/Phase2Database.vb | 一時DB作成・全7表スナップショット・片付け |
| tests/ManufacturingDataApp.Tests/Infrastructure/DatabaseMigrationTests.vb | 32ケース |
| tests/ManufacturingDataApp.Tests/Infrastructure/MonitoringConfigPersistenceTests.vb | 24ケース |
| docs/V1_10_PHASE2_IMPLEMENTATION_REPORT.md | 本報告書 |

既存テストファイル・既存92ケースのAssertionは変更していない。
Phase 0の要件定義書・基本設計書とPhase 1の報告書は変更していない。

## 2. 設定・保存契約（報告項目2～11）

| 項目 | 実装 |
|---|---|
| 2 Phase 2範囲 | Entity、Mode、設定Repository／Service、新規Schema、追加Migration、バックアップ・検証、一時DB試験 |
| 3 未実装 | Atomic／All-or-Nothing、CSV列挙・ロック、success/error、Journal／Order／CorrectionBinding、修正版、周期UI、本番周期ログ。Schedulerへの接続なし |
| 4 ImportMode | Domain Enum。RepositoryのSelect Caseで文字列SingleFile／PeriodicFolderへ明示変換。未知Enum／DB文字列を拒否 |
| 5 新3項目 | ImportMode=SingleFile、WatchFolderPath=Nothing、WatchIntervalSeconds=60 |
| 6 DB列 | 指示書どおりTEXT／NULL／INTEGER、DEFAULT、ModeのIN、周期のtypeofと1～604800 CHECK |
| 7 読込 | GetAll／FindById／FindByNameで8項目＋typeofを取得。Mode未知、周期範囲・型不正、フォルダ非TEXT／非NULLはInvalidDataException |
| 8 既存Save | UPDATEはConfigName／Encoding／Delimiter／HasHeaderのみ。Mappingの既存Transaction維持。監視3列はSQLに含めず、新しいEntityの既定値で上書きしない |
| 9 監視Save | ConfigId指定の1 UPDATEで3列のみ更新。更新0件はInvalidOperationException。形式とMappingには触れない |
| 10 パス | Service／Repository入口でWhiteSpace→Nothing、それ以外はTrim→Path.GetFullPath。存在・権限・ローカル判定はしない。PeriodicFolder＋NULL保存可 |
| 11 独立性 | ConfigIdごとにUPDATE。SingleFileへ変えても、渡されたフォルダ・周期をモードを理由に消去／初期化しない |

SaveMonitoringSettingsは4引数すべてを保存する契約であり、Nothingを指定すれば明示的な未設定になる。将来UIがモードだけを切り替える際は、現在のフォルダ・周期をそのまま渡す。SingleFileだからといって内部で値をリセットする処理はない。

新規Configの形式Saveは新3列をINSERTから省き、DB DEFAULTを使う。引数Entityに監視値が入っていても新規の形式Saveでは保存せず、再読込するとSingleFile／NULL／60となる。

SQLiteのINTEGER affinityで数値文字列が整数に変換される場合は許容される。小数1.5・非数値文字列・NULL・範囲外値はCHECK／NOT NULLで拒否する。厳密な入力文字列Parserは追加していない。

## 3. 初期化・Schema判定（報告項目12～16）

| 項目 | 実装 |
|---|---|
| 12 新規DB | user_version=0かつユーザーSchemaオブジェクト0件を確認し、7表・4明示Index・Seed・user_version=110を1 Transactionで作成 |
| 13 版 | user_versionのみ使用。0は旧版候補、110は現行。schema_versionへの代入なし |
| 14 旧版判定 | 7表・旧Config5列・他の列・型・NULL／DEFAULT・PK・UNIQUE・FK・必要Index・CHECK・AUTOINCREMENTを既知Schemaと比較 |
| 15 現行判定 | Config8列を含むVer1.10 Schemaを同じ仕組みで検証。Migration・Seedなし。整合性も検査 |
| 16 不完全／未知 | 0に1～3新列、110で列不足、0/110以外の版、未知表／列／View／Trigger／Index差分等は明示エラー。修復・降格しない |

SchemaValidatorは既知DDLでメモリ内の参照Schemaを作り、実DBとメタデータ比較する。table_xinfo（隠し列も含む）、foreign_key_list、index_list／index_xinfo、sqlite_masterを使用。sqlite_で始まる内部オブジェクトはアプリの7表として数えない。自動Indexの名前そのものではなく、UNIQUE・由来・列順・照合順序等を比較する。

CHECKはSQL全文の一致でなく、引用文字列を保持しつつ空白・改行・SQLキーワードの大小文字を正規化して式を抽出・比較する。空白／改行だけを変えた旧DDLを受理するテストがある。任意の等価DDLを証明する汎用SQLパーサーではない。未対応のSchema修飾（STRICT、GENERATED、DEFERRABLE、COLLATE、WITHOUT ROWID、ON CONFLICT等）は安全側で拒否し、自動変更しない。

旧テストDBは本番DatabaseSchemaを参照せず、変更前に固定したLegacyDatabaseSchemaで生成する。本番の旧版比較では、今回変更しない6表・Indexは現行既知DDL、旧Configは固定旧5列DDLで参照Schemaを作る。将来別のSchema改訂を行う場合は、版ごとの参照定義も見直す必要がある。

## 4. バックアップとMigration（報告項目17～24）

| 項目 | 実装・確認 |
|---|---|
| 17 Backup | Microsoft.Data.Sqlite 8.0.31のBackupDatabase。単純File.Copyではない。原DBのSchema／integrity／FK確認後に実行 |
| 18 名前・場所 | 原DBと同じディレクトリ。元DBフルパス＋.pre-v110.＋UTC yyyyMMdd_HHmmssfff＋GUID＋.db.bak。CreateNewで確保し衝突時は失敗、既存ファイルを上書きしない |
| 19 integrity_check | バックアップ接続を閉じて再度開き、全結果が1行のokであることを確認。実CHECK違反を注入した異常バックアップは拒否 |
| 20 foreign_key_check | 0行を要求。バックアップで親を削除して実FK違反を注入した場合も拒否 |
| 21 Transaction | 検証済みBackupの後、BeginTransaction(deferred:=False)。3 ALTER＋user_version=110を同一Transactionに参加させ、検証後Commit |
| 22 Rollback | 1列後・2列後・版設定後・Commit直前の例外を注入。未CommitのUsing終了でRollback、旧5列・版0・旧データを維持 |
| 23 Seed | CreateNewのTransaction内だけ。Migration／現行再InitializeはSeedを呼ばない |
| 24 データ保持 | 7表の件数・各ID・各列値をスナップショット比較。ConfigId／MappingId、測定値、マスタ、OperationLog／ErrorLog、削除済みEQ003の非復活を確認 |

例：test.db.pre-v110.20260927_123456789.<GUID>.db.bak

バックアップ完了と移行Transaction取得の間には外部更新の可能性があるため、同じ原DB接続のdata_versionを移行前に記録し、Immediate取得後に版・Schemaとともに再確認する。外部接続による更新を注入すると移行を中止する。他アプリを閉じる単独運用前提も維持する。

Migrationは元7表のDROP／再作成／データコピーを行わない。旧データを作り直して整合させる処理はない。Commit前に新Schema・CHECK・FK・integrity・user_versionを検証する。Commit障害は呼出し元へ伝え、自動的な再試行を行わない。次回起動時も版・Schemaを検証する。

失敗したバックアップは診断用としてその場に残す。存在するだけで有効なバックアップと扱わず、検証成功前にはMigrationへ進まない。今回は一時DB fixtureとそのバックアップだけを作成し、テスト終了時にプールを解放して一時フォルダを片付けた。

### バックアップ再オープンの実測上の注意

CHECK違反を入れたバックアップは、作成接続ではintegrity_checkが異常を返したが、ReadOnly再接続ではokとなる挙動を今回の導入済みランタイムで観測した。そこで再検証はPooling=False・ReadWriteで再オープンし、PRAGMA／SELECTだけを実行する。バックアップへのUPDATEや修復はしない。この方式で異常バックアップを拒否する回帰試験が成功している。すべてのSQLite版・環境のReadOnly接続が同じ挙動とは断定しない。

integrity_checkの対象とforeign_key_checkを別途必要とする仕様は[SQLite公式PRAGMA資料](https://www.sqlite.org/pragma.html#pragma_integrity_check)でも確認した。

## 5. テスト結果（報告項目25～38）

| 項目／設計ID | 検証した内容 | 結果 |
|---|---|---|
| 25 T43 | Repository／Service作り直し後の復元、他Config非変更 | Pass |
| 26 T44 | 本物の旧5列fixture、全7表の値・ID、既定監視値、Backup整合性、削除済みSeed | Pass |
| 27 T55 | 2回目Initialize無変更・Backup増加なし、4地点のMigration例外で部分確定なし | Pass |
| 28 T56 | Backup前例外、Backup CHECK／FK違反、破損ファイル、未知版、部分列、全新列＋版0、制約差分、外部更新 | Pass |
| 29 T57 | 未作成／空DBの初期化、7表・4Index・DEFAULT・Seed・版110、再実行非復活、初期化途中Rollback | Pass |
| 30 T58 | 新Entityで形式Saveしても監視値保持、監視Saveで形式・MappingId維持、新規DB DEFAULT | Pass |
| 31 T59 | 不正Enum、ID、周期、不正DB型・値の拒否、境界1／604800、NULLフォルダ保存 | Pass |
| 32 T60 Phase 2 | Config独立性、SingleFileへの変更時のフォルダ・周期保持 | Pass |
| 33 新規数 | Migration32＋監視保存24 | 56 |
| 34 既存92 | ファイル／Assertion無変更で全成功 | 92 |
| 35 総数 | 92＋56 | 148 |
| 36 Pass | 全件 | 148 |
| 37 Fail | なし | 0 |
| 38 Skip | なし | 0 |

追加で、WAL接続を開いたままのコミット済み行がBackupへ含まれること、旧DDLの空白／改行差分を許容することを確認した。旧UNIQUE／FK／CHECK／PK／DEFAULT／ON CONFLICT変更は拒否する。

OS強制終了や電源断そのものの試験はしていない。Transaction途中・Commit直前での例外注入によるRollback試験と区別する。バックアップの容量不足・実ACL拒否は実機で再現せず、境界でIOExceptionを注入した。GUI部分はPhase 5へ保留。

## 6. コマンド・依存・差分（報告項目39～47）

変更前と最終実装後に実行：

```text
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build --no-restore
```

| 項目 | 結果 |
|---|---|
| 基準 | 92 Pass／0 Fail／0 Skip、Release build成功 |
| 39 restore | 終了コード0 |
| 40 Release build | 成功、0エラー、NU1900警告3 |
| 41 Release test | 成功、148 Pass／0 Fail／0 Skip、終了コード0 |
| 42 NU1900 | 継続。制限外restoreでも脆弱性情報の取得失敗。抑制・Package更新なし |
| 43 Project／Package／Reference | 変更なし。Microsoft.Data.Sqlite 8.0.31、CsvHelper 33.1.0等を維持。新Project／NuGetなし |
| 44 設計差分 | 仕様差分なし。Validator分離とFriend fault checkpointを具体化。接続失敗時のDisposeを追加 |
| 45 Phase 1 | 対象8ファイル無変更。DBをSchedulerへ接続していない |
| 46 実ユーザーDB | アプリを起動せず、デフォルトパスへ接続しない。一時DBの明示パスだけで検証。本番DBはハッシュ確認目的でも開いていない |
| 47 Publish／dist | Publish未実行、dist・artifacts/publish・配布ZIP・既存Excelは未変更 |

DatabaseConnectionFactoryの変更は、破損DBでCreateConnectionが例外となった際に接続が呼出し元へ返らず解放漏れになり得ることへの最小限の対処。接続先・foreign_keys設定・成功時の契約は変更しない。接続プールがファイルを保持することとDispose漏れは別なので、テストのバイト照合前はClearAllPoolsも実施する。

既存一式はGit未追跡のためgit diffだけでの監査はできない。前工程開始時の既存ファイルハッシュとの比較では、本項に列挙した既存7ファイル以外の差分はなかった。Phase 1ファイルは今回の編集対象に含めていない。bin/objはBuild/Testに伴う生成物として更新される。

途中に新規テストのVB.NET配列Assertionのオーバーロード解決エラーと、バックアップCHECK検証・プール中ファイル照合の失敗があった。修正後の最終全件成功を完了判定に用いた。既存期待値を弱めて回避していない。

## 7. 残リスク・次工程（報告項目48～50）

### 48 残リスク

- 本番DBへの移行・GUI起動・実機障害復旧は未実施。実適用前に単独運用・空き容量・権限・バックアップ保管を確認する。
- データ量が大きいDBではBackup／integrity_checkに時間がかかる。今回性能上限は測定していない。
- Schema検証は既知のVer1.00／Ver1.10を対象とし、任意に改造されたDBを自動修復しない。
- バックアップとの間の外部書込みをdata_versionで検出するが、外部ツールによる原DBファイルの強制差替えまで保証する仕組みではない。
- NU1900によりオンライン脆弱性監査は未完了。依存更新は勝手に行わない。
- 旧EXEでのVer1.10 DB継続利用は保証外。自動Downgradeなし。
- CSV周期取込・全件拒否・時系列順序・Journal・UIはまだ未完成。

### 49 Phase 3へ進めてよいか

**Phase 2の完了条件を満たし、レビュー後にPhase 3へ進めてよい状態。Phase 3は開始していない。**

おすすめ：本報告書、Migration／検証処理、保存分離テストをレビューする。
理由：DB互換性を確定させたうえで、次のAtomic取込を独立して追加できる。
今すぐ行うこと：ユーザーのレビュー結果またはPhase 3実装指示を待つ。

### 50 既知の後工程レビュー事項

PeriodicImportStatusDto.Messageは累積SkippedCyclesとIsProcessingで通常Cycle中にもスキップ文言になり得るため、Phase 5で現在状態とイベント通知を分離する。指示どおりPhase 2では修正していない。
