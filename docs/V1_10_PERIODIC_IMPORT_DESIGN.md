# Ver1.10 CSVフォルダ周期自動取込 基本設計書

状態：Ver1.10 Phase 0 設計確定（J-01=B最終反映、未実装）
初版2026-09-26。Phase 0改訂指示とJ-01最終改訂指示を反映し、旧提案を本書で置き換える。

## 1. 確定構成と設計の境界

手動は既存CsvImportService.ImportAndSaveの部分登録を維持し、周期は新しいImportAndSaveAtomic入口によるファイル単位All-or-Nothingとする。Parse・Mapping・Validation・DB重複・ログを共通化し、周期でエラーを検出したら全件未登録、error移動、後続中断、監視異常停止とする。

設定はCsvImportConfigに3列追加して保存し、Ver1.00 DBへ追加専用Migrationを設計する。処理記録は.periodic-importのみで管理し、DB障害・結果不明は元CSVを保留する。DB・ログ・Journal・ファイルの同時commitを保証する設計ではない。

ユーザー判断は§12.1、J-01=Bの確定内容は§12.2。現時点でユーザー判断待ちの設計項目はない。今回は2文書のみ改訂し、実装・Migration実行・Form変更・テスト実装・Publish・dist更新はしない。

## 2. 現行構成調査（確認した事実）

### 2.1 調査資料と基準

docs/designの以下の5ファイルのセル内容、および現行ソースを調査した。Excelのレイアウト画像・実アプリ画面を今回直接検証したものではない。

| 資料（docs/design配下） | 主な参照箇所・確認内容 |
|---|---|
| 製造業向け_業務データ管理アプリ_要件定義書_v1.0.xlsx | 04_機能要件、05_CSV仕様、07_チェック仕様、08_非機能、09_テスト：Ver1.00の正常行のみ登録、CSV設定化、1万行程度の目安 |
| 製造業向け_業務データ管理アプリ_画面設計書_v1.0.xlsx | 03_データ一覧、04_CSV取込、05_CSV設定、07_ログ：画面とログ項目 |
| 製造業向け_業務データ管理アプリ_DB設計_ER図_テーブル定義_SQL_v1.1.xlsx | 06_T03_測定データ、09_T06_操作ログ、10_T07_エラーログ、12_SQL_テーブル作成：UNIQUE、FK、ログ項目 |
| 製造業向け_業務データ管理アプリ_VB.NETプロジェクト構成_v1.0.xlsx | 01_アーキテクチャ、04_主要クラス、05_依存関係、06_主要処理フロー |
| 製造業向け_業務データ管理アプリ_プロジェクト雛形設計_v1.0.xlsx | 02_vbproj定義、03_NuGetパッケージ、04_ProjectReference、09_パッケージ配置 |

### 2.2 実装経路と再利用

以下のソースパスはリポジトリルートを基準とする。

| 既存ファイル／クラス | 確認した動作 | Ver1.10での扱い |
|---|---|---|
| src/ManufacturingDataApp/Program.vb | Repository、Adapter、Serviceを手動生成、MainFormへ注入 | 新サービス・I/O実装・ログDecoratorの組立を追加 |
| src/ManufacturingDataApp/Forms/MainForm.vb | BuildUiで検索行＋操作行＋Grid。OpenCsvImportでShowDialog | 既存操作を残し、モード／監視パネルと停止待機を追加 |
| src/ManufacturingDataApp/Forms/CsvImportForm.vb | ConfigとMappingsを選びImportAndSaveを同期呼出し、件数をMessageBox表示 | 操作・処理を維持。新機能のための変更は原則不要 |
| src/ManufacturingDataApp.Application/Services/CsvImportService.vb | Importで読込とValidation、ImportAndSaveでDB重複確認、AddRange、ログ | 共通化して周期専用ImportAndSaveAtomicを追加予定。手動は既存入口 |
| src/ManufacturingDataApp.Infrastructure/Csv/CsvHelperAdapter.vb | StreamReader＋CsvHelper。設定値に従い全行をListへ読み込む | そのまま利用。新たなCSV Parserを作らない |
| src/ManufacturingDataApp.Application/Validators/ValidationService.vb | 必須・型・範囲等の検証 | そのまま利用 |
| src/ManufacturingDataApp.Application/Services/CsvConfigService.vb | GetAll／GetById／GetMappings、保存前検証 | 監視3項目の取得／検証／保存を追加予定 |
| src/ManufacturingDataApp.Infrastructure/Repositories/MeasurementDataRepository.vb | Search、AddRange内Transaction。例外時Rollback | 再利用。MeasurementData登録SQLとTransactionを再利用 |
| src/ManufacturingDataApp.Infrastructure/Logging/DbLogWriter.vb | WriteErrorsは独立Transaction、WriteOperationは別接続 | 既存IImportLogWriter経由で利用 |
| src/ManufacturingDataApp.Application/DTOs/CsvImportExecutionResultDto.vb | Total／Success／Failure／ElapsedMs、ValidationResult、LogRecorded | 手動DTOを維持。周期結果は専用DTOで件数内訳を補う |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseSchema.vb | 7テーブル、測定重複UNIQUE、設備FK | CSV設定3列を追加予定 |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseConnectionFactory.vb | LocalAppDataのDB、接続ごとにforeign_keys=ON | 変更不要 |
| src/ManufacturingDataApp.Application/Services/LogViewService.vb、既存ログRepository／Form | 日時・種別等で既存ログを検索表示 | 再利用。OperationLogFormの種別候補のみ追加予定 |

現行処理の実際の順序：

```text
MainForm → CsvImportForm → CsvImportRequestDto
 → CsvImportService.ImportAndSave
   → Import → ICsvFileAdapter.Read → CsvHelperAdapter
   → 列Mapping → 必須／型／CSV内重複／既知項目の範囲チェック
   → 候補ごとにMeasurementDataRepository.Searchで既存重複確認
   → AddRange：正常候補だけを1 Transactionで登録
   → DbLogWriter.WriteErrors → WriteOperation
   → CsvImportExecutionResultDto
```

重要な事実：

- FailureCount=SourceRowCount−保存候補数。1行に複数エラーがあっても失敗は1行。
- DB登録後のログ例外をCatchし、LogRecorded=Falseで戻る。これはDB登録失敗ではない。ErrorLogだけ記録済み、OperationLog未記録の場合もある。
- Parse／DB登録の例外はImportAndSaveのログCatchより前で起きるため、そのまま呼出し元へ伝わる。周期側で補足が必要。
- AddRangeのTransactionとログTransactionは別。ファイル移動機能・周期制御・監視設定永続化は存在しない。
- 現行MeasurementData.ValueはDouble、SQLiteはREAL。設計ExcelのDecimal表記に合わせる型変更は今回行わない。
- 設備IDの整合性はDB FKでも検査される。未知の設備等はAddRange全体を失敗させ得る。既存Validationに新ルールを勝手に追加しない。

### 2.3 設計資料と現行実装の差分

| 論点 | 設計資料 | 現行ソース／今回の方針 |
|---|---|---|
| メイン画面 | 左サイドバー・右詳細などの案 | 現行は上部2行と一覧、詳細は別Dialog。現行を基準に小さく追加 |
| 手動取込 | 読込・確認・登録の段階案 | 現行は1回の取込操作で検証と登録。現行操作を維持 |
| ログ順序 | ErrorLog→正常行登録→OperationLogという案 | 実際は登録→ErrorLog→OperationLog。障害設計は実装の順序を前提 |
| Service名 | Search／Edit／Delete個別クラス案 | 現行MeasurementDataServiceに統合。不要な再分割はしない |
| SQL配置 | Data側へ集約する規則 | 現行Repository／DbLogWriterにもSQLがある。今回その整理はしない。将来新規SQLが必要ならInfrastructure/Dataへ配置 |
| テスト | 添付指示書は71件維持 | 現行テストソースを数え、Fact 60＋InlineData 11＝71ケース。docs/GRID_SELECTION_FIX.mdにも71件合格の既存記録あり。今回再実行はしていない |

現行5 Projectはnet8.0-windows。PackageはMicrosoft.Data.Sqlite 8.0.31、CsvHelper 33.1.0、Microsoft.NET.Test.Sdk 18.10.0、xunit.v3／runner 4.0.0。今回変更せず、提案方式にも新しいNuGet／ProjectReferenceは不要。

### 2.4 Phase 0の再調査

現行ImportはParse／Mapping／行Validation／CSV内重複までを実施し、DB既存重複はImportAndSaveの中にある。Importだけを事前実行してIsValidを調べ、その後既存ImportAndSaveを呼ぶ方式では、DB重複の全件拒否を保証できず、解析も二重になる。

現行AddRangeは全INSERTを1 Transactionで囲み、失敗時Rollbackする。これを周期の全件登録に再利用する。Read→検証→DB重複SearchはTransaction外であり、Search後の競合はDBのUNIQUE／FKとTransactionによって部分登録を防ぐ。制約競合を検出した場合はDB障害の扱いへ進む。

CsvImportConfigはConfigId、ConfigName、Encoding、Delimiter、HasHeader。CsvImportConfigRepositoryのGetAll／Find／MapConfigはこの5列だけを扱う。Saveは新規INSERT、既存UPDATEとMapping再作成をTransaction化している。CsvConfigServiceは6項目Mapping等を検証する。CsvImportConfigForm.SaveConfigは画面値から新しいEntityを生成するため、監視3項目を全項目UPDATEへ安易に追加すると、形式設定を編集しただけで監視値を初期化する危険がある。

DatabaseInitializerはCREATE TABLE IF NOT EXISTS群をTransactionで実行後、Seedを毎回呼ぶ。CREATE TABLE IF NOT EXISTSだけでは既存テーブルに列は増えない。ソース検索でuser_version／Migration等の版管理機構は確認できなかった。実DBを開いて版番号を確認・変更したわけではない。初期化と移行の分岐設計は§9.4。

## 3. MainForm画面設計案

### 3.1 配置

現行rootは上部AutoSize／下部Percent100の2行。上部commandsへモード行と周期パネルを追加し、検索行・操作行の相対順序とGrid列は維持する。ClientSize=1180×680、MinimumSize=1100×640を基準に、周期モード時は一覧の高さを減らす。DPIで収まらない場合は上部領域のスクロール等を検討し、フォーム拡大を無断で決めない。

```text
既存検索行：設備ID [ ] 項目名 [ ] 取得日時 [ ]～[ ] [検索][条件クリア][削除]
既存操作行：[CSV取込] CSV設定 [SCADA_A ▼] [CSV設定][CSV出力][マスタ管理]
            [エラーログ][操作ログ] 検索結果：N件
取込方式： (●) 1ファイル取込   (○) フォルダ周期監視  [監視設定保存]
──────────────────────────────────────────────────────────────────
既存DataGridView（列・チェック・詳細表示の操作を維持）
```

```text
既存検索行・操作行（配置は同じ。CSV取込は無効）
取込方式： (○) 1ファイル取込   (●) フォルダ周期監視  [監視設定保存]
┌ フォルダ周期監視（上のCSV設定を使用）────────────────────────┐
│ 監視フォルダ [C:\CSV\Input                         ][参照]    │
│ 監視周期     [60       ↕] 秒    [周期監視開始／停止]           │
│ 現在の状態：監視中・次回確認 10:01:00                        │
│ 監視履歴（閲覧専用）                                        │
│ [10:00:00 A.csv：書込み中。後続CSVも次周期へ延期         ▼]  │
└────────────────────────────────────────────────────────────┘
既存DataGridView
```

RadioButtonは2つの選択肢と排他関係が常に見えるため推奨。ComboBoxは省スペースだが現在の選択肢を隠すため第2候補。フォルダはTextBox＋FolderBrowserDialog、周期はNumericUpDownとする。

手動モードでは周期パネル（開始Buttonを含む）を非表示にし、不要な設定の無効表示で画面を占有しない。既存CSV設定ComboBoxは出力にも使われるため残す。手動CsvImportFormのCSV設定は現行どおり独立選択とし、勝手に連動させない。

### 3.2 操作可否

| 操作 | 手動・Stopped | 周期・Stopped | Starting／Running | Stopping |
|---|---|---|---|---|
| 取込方式変更 | 可 | 可 | 不可 | 不可 |
| 手動CSV取込 | 可 | 不可 | 不可 | 不可 |
| CSV設定選択／編集 | 可 | 可 | 不可 | 不可 |
| 監視フォルダ／周期 | 非表示 | 可 | 不可 | 不可 |
| 開始／停止Button | 非表示 | 開始 | Starting中不可、Runningで停止 | 停止表示のまま不可 |
| 検索／ログ閲覧 | 可 | 可 | 可 | 可 |
| CSV出力 | 可 | 可 | 可（固定した設定、現在の検索結果） | 可 |
| 編集／削除／マスタ更新 | 可 | 可 | 不可 | 不可 |

周期停止後に手動RadioButtonへ切替えるとCSV取込が再び有効になる。「停止しただけで周期モードのまま手動取込も有効」は2モード排他に反するので採用しない。

MainFormのimportButton／configButton等は現行ローカル変数なので、操作状態反映用にフィールド化する。UI無効化に加えてイベント入口とService側の状態も検証する。現行手動DialogはモーダルでMainFormの開始操作をできなくするため、CsvImportForm自体の改修は不要。ほかの呼出し口を増やす場合は排他の再検討が必要。

周期処理後の一覧自動再検索は行わず、現在の選択・検索結果を維持する。状態へ「N件登録、一覧は検索で更新」と表示し、検索操作で反映する。

### 3.3 監視状態ComboBox

- DropDownStyle=DropDownList。テキスト入力不可、Enabled=Trueで過去項目の閲覧は可能。「監視履歴（閲覧専用）」のラベルと説明ToolTipを付ける。
- SelectedIndexChangedは閲覧のみ。Start／Stop／状態遷移へ結び付けない。ComboBoxにReadOnlyプロパティがあるとは想定しない。
- 最大100件、新しい順。追加時に末尾を削除。開いていないとき最新を選択し、閲覧中は項目IDで選択維持。閉じたら最新表示へ戻す。
- 現在状態Labelは履歴選択と独立。「監視停止中／監視開始／CSV検出／CSV取込中／取込成功／取込失敗／先行書込み中・後続延期／修正版の確認待ち／修正版取込中／前回処理中のため周期スキップ／監視停止処理中／監視停止」を表示可能にする。
- 日時、FileName、件数、必要な理由を表示。1件最大512文字、改行／制御文字を整形。同種連続スキップは回数を更新し、重要な結果が毎秒押し出されないようにする。
- ワーカー通知は容量100の待ち行列と最新状態1件へ集約し、UIへの未処理通知を無制限に積まない。UI更新は最大毎秒4回を目安とし、停止／重大障害は直ちに反映。詳細な行エラーをすべてComboBoxへ流さない。

### 3.4 DB保存UIと既存画面

監視3項目は選択中ConfigIdに紐づく。MainFormの「監視設定保存」は手動／周期の両モードで停止中だけ有効。手動モードでは隠れたフォルダ・周期を消さず保持する。CSV設定が未選択なら保存／開始不可。起動時の選択順は現行GetAllのConfigName順を維持し、最後の選択ConfigIdを別途永続化しない。選択された設定の保存モードを表示しても状態はStopped。

Config切替／設定編集Dialogを開く前に未保存変更を確認し、保存・破棄・取消を選べる。開始は入力検証→3項目保存→再読込→スナップショット→開始準備の順。DB保存失敗なら開始しない。通常終了時に未保存値を自動上書きせず、未保存変更を確認する。

CsvImportConfigFormの画面項目・サイズは維持する。既存Save契約は形式／Mappingだけを更新し、保存した監視3列に触れない。監視列は独立したSaveMonitoringSettingsで更新する。手動CsvImportFormは保存モードが周期のConfigでも、明示的な手動モードから開けば手動の部分登録入口を呼ぶ。Configの保存モードから取込保存方針を暗黙に決めない。

監視中は詳細表示へのダブルクリックを無効とし、編集画面へ入れない。削除Button、マスタ管理入口も無効。開始前に開いていたDialogは現行モーダルのため開始操作と重ならない。停止完了で編集・削除・マスタ等を有効に戻す。CSV取込だけは手動RadioButton選択を条件とする。

### 3.5 修正版の確認UI（Phase 4の契約、Phase 5の画面接続）

StoppedかつCorrectionPending（修正版待ち）または未使用のBoundCorrectionの場合だけ「修正版を指定／確認し直す」操作を表示する。簡潔なPeriodicImportRecoveryFormを開き、停止原因の元AttemptId、元名、Config名、元の検出順位、エラー概要、保留中後続件数を表示する。再指定時は未使用の対応付けを無効化してから同じ確認条件を適用する。

ユーザーは監視直下のCSVを1つ選ぶ。候補のパス・サイズ・更新日時・指紋を取得し、「このCSVを表示された元Attemptの修正版として扱う」ことを確認して対応付けを保存する。元名と候補名は異なってもよい。候補一覧は案内であり、自動選択・同名自動承認をしない。現在の設定が元AttemptのCSV形式・Mappingと異なる場合は一致する設定へ戻す必要を表示し、黙って別条件で修正版を取り込まない。

「対応付けを保存」はStartではない。Dialogを閉じたあと「周期監視開始」で明示開始する。開始押下時に修正版未確定ならStoppedのまま確認UIを案内し、Dialogから自動的に監視へ戻らない。取消・不一致・候補なしでは後続も未処理のまま。成功確定済み原本を再登録する操作や、保留を無条件解除するButtonは設けない。

現在状態Labelは「停止：B.csvの修正版確認待ち」または「停止：DB結果確認が必要」を明確に分ける。CorrectionPending／WaitingForReadable等はJournal上の進行状態であり、監視ライフサイクルのStopped／Starting／Running／Stoppingを増やすものではない。

## 4. All-or-Nothing実現方式・クラス責務

### 4.1 比較と選定

| 案 | 共通化 | 手動へのリスク・費用 | 選定 |
|---|---|---|---|
| 既存ImportAndSaveに公開Optional Boolean追加 | 内部共有しやすい | 呼出し元の省略／フラグ反転で保存方式を誤る | 不採用 |
| 周期専用Serviceに取込ロジックを複製 | 手動コードを触らず開始可能 | Mapping・DB重複・ログの二重保守が発生 | 不採用 |
| Importだけ再利用、DB重複・ログを共通部品へ抽出して別Serviceへ | 共有可能 | 多数の依存・型・移動が必要 | 将来拡大時の候補 |
| 既存Serviceに明示的なImportAndSaveAtomic入口を追加し内部共通化 | Parse・検証・DB重複・ログを1実装にする | 既存Service内部の限定的リファクタリングと回帰試験が必要 | 採用 |

将来実装では既存ImportAndSave(request)の署名と部分登録契約を維持し、内部共通コアをAllowPartialで呼ぶ。新規ImportAndSaveAtomic(request)だけがAllOrNothingを指定する。保存方針は非公開のEnum等で固定し、可変の共有フィールドに保持しない。手動Formは既存入口、周期ExecutorはAtomic入口を明示呼出しする。

同じクラスを周期用依存で構築しても、CSV形式設定のImportModeを保存方針のスイッチにしない。誤配線に対して「手動入口で3行／周期入口で0行」のテストを必須とする。

### 4.2 共通コアとTransaction

1. 既存Importで全行Parse・Mapping・行Validation・CSV内重複を行う。
2. 既存DB重複Searchループを共通の検証処理へ抽出し、行番号付きの実エラーと保存候補を得る。手動では現行の順序・検索条件・件数を保持する。
3. 周期側だけ、IEquipmentRepositoryによる設備存在確認等、現在の登録制約から判定できる業務エラーを事前検証する。既存コンストラクタを維持して新しい依存付き構築を追加し、Atomic入口で必須依存が欠けていれば登録前に構成エラーとする。未知測定項目を新しく必須マスタ扱いする等、既存要件にないValidationは加えない。
4. 手動は有効候補を従来どおりAddRangeへ渡す。周期はエラー1件以上ならAddRangeを呼ばずRejectedとする。全件有効のときだけ1回AddRangeする。0行正常の場合は書込みTransactionを省略できる。
5. AddRangeのBeginTransaction～全INSERT～CommitがMeasurementDataの原子性の境界。1行でもINSERT失敗なら全体Rollback。CSV検証・ログ・ファイル移動はこのTransactionの外側。登録後DELETEによる疑似Rollbackは禁止。
6. 全件拒否またはcommit成功の結果を構築し、既存ログ変換・WriteErrors／WriteOperationを共通処理で呼ぶ。ログ失敗は登録結果を反転させずLogRecorded=False。DB障害は確定結果として返さずExecutorで分類する。

DB重複の事前SearchからAddRangeまでに別プロセスがINSERTする競合窓はある。UNIQUE／FKが最後の防御となり、1 Transactionにより部分commitはしない。この競合によるDB例外も元CSV保留・RecoveryRequired・停止とし、単なる事前Validation失敗にすり替えない。現行Repositoryに新しい行単位登録／削除の処理は加えない。

ErrorLogとOperationLogは別Transaction／接続なので、MeasurementData・ログ・Journal・ファイルをひとまとめに原子的に確定できるとはしない。強制終了時はImportStartedを不明状態として止める。

### 4.3 結果と件数の契約

PeriodicFileResultDtoはOutcome（Succeeded／Rejected／ReadFailed／DatabaseFailed／Unknown）、TotalCount、RegisteredCount、UnregisteredCount、ValidationErrorRowCount、HeldValidRowCount、LogRecorded、DbOutcomeKnownを持つ。既存CsvImportExecutionResultDtoは手動契約を保持し、Atomic側からの既存件数は「実登録／未登録」の意味で返す。

正常3＋不正2なら周期Total=5、Success=0、Failure=5、ValidationErrorRowCount=2、HeldValidRowCount=3。手動は5／3／2。エラー行数は行番号の重複を除いて計数する。正常保留行をErrorLogに偽のエラーとして追加しない。候補一覧に残る正常行を保存済み一覧と解釈しない。

### 4.4 構成

```text
手動CsvImportForm → CsvImportService.ImportAndSave ── AllowPartial
周期MainForm → PeriodicImportService → Executor → ImportAndSaveAtomic ── AllOrNothing
                                                   ↓
                           共通Parse／Mapping／Validation／DB重複確認
                              ↓ 保存方針判定
                           MeasurementDataRepository.AddRange
                              ↓ 1 Transaction
                           既存ErrorLog／OperationLog
```

| クラス／Interface | 層 | 責務 |
|---|---|---|
| PeriodicImportService | Application | 状態、固定周期、排他、順序、エラー後の停止、Journal／移動調整 |
| CsvImportService（変更） | Application | 明示した2入口と共通コア。手動部分登録／周期全件拒否の保存方針を分離 |
| PeriodicImportOptionsDto／State／StatusDto | Application | 実行設定コピー、Stopped等、ファイル結果とUI通知 |
| PeriodicFileResultDto／ImportProcessingRecordDto | Application | 件数・結果確定性、Attempt・段階・指紋・移動先、Order位置・元失敗Attempt |
| PeriodicImportOrderDto／CorrectionBindingDto | Application | 単一未完了順序・進行位置・復旧待ち、ユーザーが確認した修正版対応 |
| IPeriodicImportFileStore／IImportProcessingJournal | Application | 列挙、Lease、指紋、移動／永続Attempt・Order・修正版対応付けの契約 |
| IPeriodicCsvImportExecutor | Application | Atomic入口呼出しと具体例外分類の抽象契約 |
| PeriodicImportFileStore | Infrastructure | ローカル通常フォルダ検査、Windows共有、上書きなし移動 |
| JsonImportProcessingJournal | Infrastructure | .periodic-import内の永続記録。LocalAppData利用登録は持たない |
| PeriodicCsvImportExecutor | Infrastructure | Atomic入口呼出し。CsvHelper／SQLite例外を型で分類、ロジック複製なし |
| PeriodicImportLogWriter | Infrastructure | IImportLogWriter Decoratorで周期種別を付与。DbLogWriterへ委譲 |
| PeriodicImportStatusPresenter | Presentation | 有界通知、履歴、UIスレッド反映 |
| CsvConfigService／ICsvImportConfigRepository／Repository（変更） | Application／Infrastructure | CSV形式保存とは別に監視3項目を保存・取得 |
| DatabaseMigrator（新規）／DatabaseInitializer（変更） | Infrastructure/Data | DB版検証、バックアップ、追加専用Migrationと新規初期化の分岐 |

既存Adapter／ICsvFileAdapterはFileShare.Readのガードと共存させ、変更しない。FileShare.Noneで握って既存Adapterを再オープンさせる方式は採らない。既存ValidationService、MeasurementDataRepository、DbLogWriterを再利用する。新NuGet・DIコンテナ不要。

## 5. 周期・非同期・二重実行防止

### 5.1 方式比較

| 方式 | 長所 | 課題 | 結論 |
|---|---|---|---|
| 単調時刻基準のTask.Delay＋別ワーカー | t0+nPを明示でき、遅延時の欠落周期を管理できる | 時刻計算とStop競合をテストする必要 | 推奨 |
| PeriodicTimer＋短いtick監視＋別ワーカー | .NET標準、周期制御が簡潔 | tickが合流するため遅延時方針が別途必要 | 代替可 |
| 処理完了後にTask.Delay(P) | 実装が簡単 | 15秒処理＋10秒待ちなら次は25秒。要求20秒と不一致 | 不採用 |
| WinForms.Timer内に同期取込 | UI接続が簡単 | UI停止、責務混在 | 不採用 |
| Timer callbackで無制御にTask起動 | 短い実装 | 重複・終了待機・例外管理が難しい | 不採用 |

PeriodicTimerは待機間の複数tickを合流する仕様である。長い取込をtick待機ループで直接awaitし続ける設計は採用しない。[Microsoft Learn: WaitForNextTickAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer.waitfornexttickasync?view=net-8.0)

### 5.2 スケジューラ契約

1. StartAsyncは状態遷移用の短いロックでStopped→Startingを確保。二重Startは拒否する。
2. バックグラウンドで入力、所有Lease、success/error、管理記録、設定を検証。開始準備Taskも追跡する。失敗ならLeaseを解放してStoppedへ戻す。準備完了時は同じ状態ロックでStartingかつ停止要求なしを再確認し、停止が先ならRunningへ進まず準備で得たLeaseを解放する。
3. 準備完了時をt0とし、Runningへ遷移。処理SemaphoreSlim(1,1)を非待機取得して即時の1バッチを起動・追跡する。
4. スケジューラはTimeProviderの単調時刻から次期日t0+nPを計算して、キャンセル可能なTask.Delayで待つ。秒数はTimeSpan.FromSecondsで扱う。
5. 期日到達時は短い状態ロック内でRunning／停止要求なしを再確認し、SemaphoreSlim.Wait(0)で取得できたときだけTaskを登録する。取得不能ならそのtickを捨て、スキップ回数を通知する。待機列を作らない。
6. ワーカーは同期I/O、順次ファイル処理、結果通知を担当。例外を観測し、FinallyでSemaphoreを解放。実行Taskは必ずServiceが保持し、fire-and-forgetにしない。
7. スケジューラが遅れて複数期日を通過したときは、過去分を再生せず1回だけ実行可否を判定。次はt0から計算した未来の期日に進める。時計の手動変更は周期に影響させない。

```text
周期10秒、バッチ15秒：
t0=0   初回開始
   10  Semaphore取得不可 → 周期スキップ
   15  前回完了、Semaphore解放（ここでは次回を開始しない）
   20  次バッチ開始
```

Stopとtickは同じ短い状態ロックで開始可否を確定する。ロック中にI/Oやawaitを実行しない。停止が先ならtickは起動不可、起動確定が先ならStopがそのTaskを捕捉して待つ。この競合を試験する。

## 6. 周期ファイル処理と公開契約

### 6.1 バッチ処理

```text
Journal確認 → 未完了順序／修正版対応を優先 → 未完了順序がなければ直下CSVをソートして順序保存
 → 停止要求確認 → 読取Lease
   ├ 共有違反：対象と後続を残して当該周期終了、Running維持、次周期も同じ先頭
   └ 読取可能：指紋取得 → ImportStarted永続化
       → ImportAndSaveAtomic
         ├ 全件成功／0行正常：ResultKnown → MovePending → success → Completed
         │                    → 次ファイル
         ├ 通常エラー：停止要求を確定（以降の開始禁止）
         │             → ResultKnown（全件未登録）→ MovePending → error → Completed
         │             → 順序を修正版待ちへ保持 → バッチ終了 → 監視異常停止
         └ DB障害／結果不明／Journal不整合
                       → 元CSV保留 → RecoveryRequired → 監視異常停止
```

状態表示は通常エラーで「CSV取込失敗：エラーを検出したため全件未登録」。B異常後のCは直下に残す。次周期へ進まず、ユーザーが修正・必要に応じて再配置し再開始する。共有違反は異常停止ではない。その周期の後続をすべて保留し、監視継続で次周期に同じ先行CSVから再試行する。「A.csv：書込み中のため今回の処理を保留。時系列維持のため後続CSVも次周期へ延期」と表示する。毎周期OperationLogへ記録せずUIで集約する。

### 6.2 列挙・対象境界

SearchOption.TopDirectoryOnly相当。Path.GetExtensionをOrdinalIgnoreCaseで.csvと比較し、.CSV／.CsVは対象、.csvx／.txtは対象外。LastWriteTimeUtc昇順、FileNameのOrdinalIgnoreCase、Ordinalの順で安定ソート。未完了の確定順序がない場合にこのキーで並び順を作る。周期途中の追加は現在の未完了順序を追い越さず、現在順序完了後の次周期に通常キーで処理する。停止・再起動・再開始時も未完了順序を先に復元し、現在時刻順への再ソートで先行CSVを後ろへ送らない。

列挙後の消失・置換・メタデータ取得不可は、無言で時系列を飛ばさず状態表示して安全停止する。DB未実行ならその旨を記録する。元がない場合にerror移動を成功扱いしない。

UNC／SMB／NAS／ネットワーク割当ドライブを拒否。ルート・祖先・対象・保存先のreparse point／junctionを拒否し、ローカル通常フォルダであることを確認する。クラウド同期・特殊仮想領域は通常ローカルと同等の保証を前提にしない。ACLを出力担当・運用担当へ限定する。

対象親ディレクトリの一致と区切りを確認し、単純なStartsWithで兄弟フォルダを許さない。悪意ある管理者の同時パス差替えやハードリンク等まで完全に防ぐセキュリティ境界とはしない。

### 6.3 読取Lease

公開後は内容不変を正式な運用契約とする。推奨は.tmp等へ書く→完了→Close→.csvへrename。直接.csvへ書く場合は書込み終了までwriteハンドルを保持する。

FileMode.Open／FileAccess.Read／FileShare.ReadのガードをImportAndSaveAtomicが戻るまで保持する。既存CsvHelperAdapterの読取りは許可し、書込み／削除を共有しない。共有違反・ロック違反だけをRetryNextCycleへ分類し、Thread.Sleepによる固定待機で解決しない。[FileShare公式仕様](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-8.0)

ガード下で正規化相対名、長さ、LastWriteTimeUtc、SHA-256を記録する。指紋は復旧照合用であり、同一内容の新しいCSVを永久に除外するキーではない。断続的なClose→追記は完成と区別できないため契約外。

### 6.4 success/error移動

両フォルダと管理領域を取込前に準備し、各バッチでも確認する。準備後の権限変化・容量不足等は移動失敗として扱う。

同名がなければ元名、衝突時は元名_yyyyMMdd_HHmmssfff_連番.csv（UTC）。上書きなしMoveを使用し、存在確認後の競合も捕捉して最大100候補まで変更。長名は識別子付きで短縮、元名はJournalに残す。衝突以外のIOExceptionを無条件に再試行しない。

移動先候補をMove前に永続化。読取Lease解放後に移動し、移動前後の属性・指紋を照合する。解放からMoveの競合窓は公開後不変の契約を前提とする。不一致はRecoveryRequired。success/error内を監視せず、自動で直下へ戻さない。

通常エラーの移動失敗もMovePendingで止める。移動が遅れても停止要求を取り消さず、Cや次周期を始めない。

### 6.5 修正版の安全な対応付けと優先処理

同名・サイズ一致・ハッシュ差分・更新日時だけで「修正版」と証明できない。自動判定方式は採らず、**元Attemptに対するユーザーの明示確認＋確認したファイルの指紋固定**を採用する。指紋は確認した実体がすり替わっていないことの検証であり、内容が業務上正しい修正かを証明するものではない。意味上の対応をユーザーが確定し、内容は通常の全行Validationで確認する。

対応付けに必要な条件：

1. 元AttemptはDbOutcomeKnown=Trueかつ全件未登録のRejected／ReadFailed。error移動がCompletedで、OrderId・位置・停止原因Attemptが一致しCorrectionPendingであること。DB不明、移動未解決、Journal破損では対応付け不可。
2. 所有Leaseを取得し、停止中に一度に1つの対応付けだけを保存する。候補は同じ監視ルート直下の通常.csvで、error/success内やルート外・リンクは選べない。ConfigId・CSV形式・列Mappingスナップショットが元Attemptと一致すること。表示名だけ一致していても不可。
3. 候補を読取Lease下で確認し、正規化相対パス、サイズ、LastWriteTimeUtc、SHA-256、取得可能なWindowsファイル識別情報を保存する。共有違反なら確認を完了せず待つ。
4. 保持済み後続項目のパス／識別情報に一致するCSV、ほかの未解決Attemptが所有するCSV、成功確定済みAttemptの実体を候補にしない。利用可能な識別情報で区別できない場合も選択を承認しない。同内容の成功CSVをコピーして戻したケースは成功Journalとの指紋照合とDB重複検証で再登録を防ぐ。保持期限後のコピー再投入まで恒久検出する仕組みは追加しない。
5. 確認画面で元Attemptと候補を明示し、ユーザーが対応を承認する。CorrectionBindingId、元Attempt、Order位置、候補指紋、確認時刻、確認済み印をJournalへ保存する。これはユーザーの確認記録であって認証監査システムではない。
6. ユーザーがStartを押したらJournalと候補を再検証。指紋不一致／Config変更／消失なら対応付けを無効にし、Stoppedで再確認を求める。共有違反なら同じ修正版を先頭に保持し、その周期を終えRunningを維持する。
7. 一致した候補を新規Attemptとして、RootFailedAttemptIdと直前の失敗AttemptId、BindingIdを関連付け、**元のOrder位置**でAtomic取込する。DB呼出し前に関連を永続化する。
8. 全件登録または正常0行→success移動→Completed確認後に順序の先頭を次へ進め、C以降を許可する。正常0行も既存確定仕様どおり完了と表示する。通常エラーが再発したらerror→新Attemptを最新停止原因としてCorrectionPending→異常停止。DB障害はRecoveryRequiredであり、新たな修正版の投入で解除しない。

一度使用した対応付けを再度登録に使わない。Start連打、再起動、結果保存失敗時もImportStarted以降の同Attemptを再呼出ししない。修正版優先中の通常新着CSVは後続を追い越せない。アプリは元ファイルの更新日時を書き換えず、C以降のCSVを移動せず、Journal削除で順序を解除しない。

## 7. Journal・障害復旧

### 7.1 確定した範囲

管理場所は<監視フォルダ>/.periodic-import/のみ。Attempt JSON、順序Order JSONと所有ロックを保存する。LocalAppDataの監視フォルダ利用登録・管理フォルダ削除検出用の二重管理は作らない。成功ファイル名／AttemptIdの恒久履歴専用DBも追加しない。

起動前に所有ロックをFileShare.Noneで取得し、停止完了まで保持する。存在だけで実行中と判定せず取得可否で判断。同じ監視フォルダに対する複数プロセスの周期開始を禁止する。既存ロックファイルをtruncateしない。

JournalはVersion、AttemptId、ルート、DBパス、元名、指紋、ConfigId、設定スナップショット／ハッシュ、開始時刻、段階、Outcome、登録件数、エラー行数、ログ成否、移動先を保持する。CSV本文は保存しない。パス・版・形式を検証し、監視ルート外を指す記録を拒否。

同じ管理領域の一時ファイルへ書いてflush後に置換する。不完全な一時記録や破損を検出したら空の台帳として継続しない。Journal保存成功を確認する前にDB登録を始めない。

### 7.2 段階と再起動

| 段階 | 意味 | 復旧 |
|---|---|---|
| ImportStarted | Atomic呼出し前に永続化済み | 未実行／拒否／commit後のどこで落ちたか不明。再取込せずRecoveryRequired |
| ResultKnown | 全件成功、全件拒否、読込失敗などDB結果が確定 | 登録再実行禁止。保存されたOutcomeでsuccess/errorの移動だけ復旧 |
| MovePending | 宛先を保存済み、移動未確認 | 元あり・先なしは指紋一致で移動のみ。元なし・先ありは一致でCompleted。両方あり／なし／不一致は要復旧 |
| Completed | 所定配置を確認済み | 同じAttemptを再登録しない。原則30日、未完了順序の参照中は延長 |
| RecoveryRequired | DB障害、結果不明、Journal不整合等 | 元CSVをerrorへ移動しない。照合・解決まで新規取込不可 |

ResultKnownは成功だけではなく0件登録のRejectedも含む。以降のJournal更新失敗では、その確定結果を失敗や未実行に書き換えない。記録を安全に確定できなければ停止する。

DB結果不明／Journal不整合は復旧処理だけを行い、新規CSVも修正版も処理しない。移動保留の復旧が解決した場合も一度Stoppedへ戻り、解決内容を表示する。全件未登録確定の修正版待ちは別扱いで、ユーザーが対応付けを確定して開始を押した場合に限り、その修正版を処理できる。修正版が正常完了すれば同じ実行内で保持済み後続へ進める。対応付けや移動復旧だけで監視を自動再開しない。

Move成功後Completed記録前の停止は、保存済み宛先と指紋を照合して解決できる。CompletedでもOutcomeがRejected／ReadFailedなら順序上は修正版待ちを残す。同名の再配置だけでは修正版と断定しない。明示対応付けした修正版に新Attemptを発行し、元の順序位置へ関連付ける。未解決のまま同名置換されたら推測せず停止。

Completed記録は30日保持。ただし未完了順序、修正版待ち、対応付け履歴から参照されるAttemptは30日を過ぎても保持する。順序全体が正常完了し未解決参照がなくなった時点とCompleted日時の遅い方から30日後に、検証済みの自アプリ管理JSONだけを削除できる。未解決記録、CSV、既存フォルダは自動削除しない。

### 7.3 削除・改変の保証範囲

利用者による.periodic-importの削除・編集の完全な自動検出は保証しない。再起動前に痕跡なく削除されれば初回利用と区別できない場合がある。外部の利用登録との突合は行わない。実行中に保持するAttemptが消えた、JSONが不正、指紋が不一致等、検出可能な不整合はRecoveryRequiredとして停止する。

将来USER_GUIDEへ「管理フォルダを削除・編集しない」「DB復元時はCSV・Journalも保全・照合する」「管理記録削除で再開始を強制しない」を記載する。DBパスが同じDB差替えも自動検出を保証しない。これは承認された運用前提であり、新しい判断待ちに戻さない。

### 7.4 例外境界

| 原因 | 登録・移動・停止 |
|---|---|
| 共有違反 | 対象・後続とも未実行で直下残置、その周期終了、Running維持、次周期は同じ先頭 |
| Validation／CSV内・DB既存重複／登録前業務エラー | 全件未登録、実エラーを記録、error移動、後続中断、異常停止 |
| CSV解析／通常読込エラーでDB未実行が確定 | error移動を試み、異常停止。移動不能は保留 |
| DB接続／Search／設備確認／AddRange／commit／Rollback障害 | 元CSV保留、error移動禁止、RecoveryRequired、停止。Rollbackして0件と分かる場合もDB障害として保留 |
| Journal不整合／書込み不可 | 新規登録禁止、元CSV保留、RecoveryRequired、停止。記録不能ならUIにも保留理由を表示 |
| 予期しない例外 | DB実行有無を推測しない。RecoveryRequired、停止 |
| 確定後Move失敗 | MovePendingで停止、次回は移動のみ |
| ログ失敗のみ | 確定した結果を維持し所定の移動／保留まで完了、その後停止。ログの全件再書込みはしない |
| フォルダ消失／権限変更 | 未処理例外でアプリを終了せず、理由記録と安全停止 |

### 7.5 復旧手順

通常Validationエラーは0件登録が確定するため、ユーザーがファイル全体を修正して再投入できる。error内の原本保管を推奨するが、利用者がerror内を編集して再配置する場合も元指紋で修正版を自動判定しない。§6.5の明示対応付けを必須とする。Aの登録を取り消さず、Bの修正で既存DBと重複する値にした場合は再度全件拒否し、後続を保留して異常停止する。

DB障害は0件か全件かを一律に断定しない。監視を止め、CSV・Journal・DBを保全し、Attemptと登録データ・ログを照合する。既存キーがあるだけでそのAttemptによるcommitの証明にしない。結果を証明できなければ保留を維持する。保守担当の復旧内容と根拠を手順書へ残し、台帳を削除して再取込しない。

### 7.6 最小限の順序記録とクラッシュ時の復元

追加するのは**監視フォルダごとに1つの未完了Order記録**と、既存Attemptへの関連情報である。常駐Queue Server、多フォルダ並列キュー、後続CSVのコピーは追加しない。列挙で得たファイル一覧の順序を記録し、進行位置を1つだけ進める。保存先は既存.periodic-import内。

| 保存情報 | 目的 |
|---|---|
| OrderId、Version、ルート、DB識別、ConfigId・形式／Mappingスナップショット | どの運用・設定で確定した順序かを固定 |
| EntriesのPosition、検出時相対名、元ソートキー、観測時属性、取得できたファイル識別情報 | LastWriteTimeUtc変化後も先行／後続関係を保持。先頭を飛ばさない |
| Cursor、各位置に関連するAttemptId | どこまで正常確定したか。Attempt結果と突合して復元 |
| HeadDisposition | Ready／WaitingForReadable／CorrectionPending／BoundCorrection／RecoveryRequired／Resolved |
| RootFailedAttemptId、LatestFailedAttemptId、未解決の元名・指紋 | 最初のエラーと繰返し修正を同じ位置に結び付ける |
| CorrectionBindingId、候補パス・指紋、確認時刻・確認印、使用AttemptId | ユーザーが指定した修正版とその1回の実行を対応付ける |

Order全体を原子的置換で保存し、世代番号を進める。開始時に複数の未完了Order、欠落参照、不整合世代を検出した場合は勝手に片方を選ばずRecoveryRequired。運用状態のDB保存はしない。

**順序の確定点：** 未完了Orderがない周期に、通常キーでソートした候補リストを保存し終えてから最初のファイルに触れる。空リストはOrderを増やさない。完了したOrderのあとに新着を次の周期で取り込む。後着した古い時刻のCSVも既存の確定Orderを追い越さない。これが「既に確定している処理順序を優先」の適用範囲であり、未検出の将来ファイルまで含む世界全体の時刻順を保証するものではない。

**書込み中の先頭：** ガード取得前でも先頭の順番はOrderに保存済み。共有違反で当該バッチを終了し、Cursorは進めない。次周期・手動停止後の再開始・再起動でも同じ位置を確認する。ファイル内容のSHA-256は書込み中に取得せず、読取可能になってガード取得後に確定する。未着手ファイルが書込み継続でサイズ・更新日時を変えてもPositionを維持する。先頭になった時点で読取Leaseを取り、完成した内容を確定する。後ろへ再ソートしない。

未読取の待機ファイルの識別には観測情報と取得可能なファイル識別情報を用いる。最初から識別情報が取得不能な場合は、公開契約と「待機中の同名ファイルを差し替えない」運用前提の下で同じ位置の候補を再確認する。消失・識別子変化・ガード下で指紋を確定済みのファイルの属性変化など置換を検出したらRecoveryRequiredとし、通常の修正版と推測しない。改変の完全検出は既存の保証範囲を拡大しない。**明示的なエラー修正版の識別は、この待機再確認で代用せず必ず§6.5に従う。**

**登録・移動・順序の境界：** 新AttemptにOrderId／Positionを記録し、Orderの当該位置へAttemptIdを結び付ける。両記録の整合とflushを確認してからAtomicを呼ぶ。DB処理後は既存ResultKnown→MovePending→Completedを使用する。Cursorを進められるのはOutcomeがSucceededで、success配置とCompletedが確認できる位置だけ。CompletedでもRejectedならCursorを止めてCorrectionPending。Move失敗はその位置で止める。

別JSONへの更新は同一Transactionではないため、再起動時はCursorだけを信じず関連Attemptを突合する。Attempt Completed成功・Cursor未更新なら再登録せずCursorだけ修復。Attempt Completed拒否・CorrectionPending更新前なら修正版待ちを復元。ImportStartedならDB結果不明としてRecoveryRequired。Cursorが未完了／拒否Attemptを越えていたら記録不整合として止める。途中保存・孤立Attempt・対応付け消失を見つけたら後続を開始しない。

拒否されたBのerror内原本を利用者が編集・再配置しても、保存された全件未登録確定結果と順序位置は変えない。再起動で完了済みerror原本のハッシュ一致を修正版認定の条件にしない。一方、MovePendingのままの原本変更は未解決なのでRecoveryRequired。

順序保持の仕組みは、成功確定済みのAを巻き戻すものではない。Order復元・修正版再失敗のいずれでもAのAtomicを再呼出ししない。通常のStopでも未完了Orderを残し、設定・モードを変えて周期再開してもそのルートの保留を無視しない。元設定と一致しなければ開始を拒否して案内する。手動取込の既存動作自体は変更しないため、保留中の監視対象を手動取込で迂回しないことをUSER_GUIDEに記載する。

## 8. 状態遷移・Cancellation・終了

```text
Stopped --Start--> Starting --準備成功--> Running
                     |                    |
                 準備失敗                Stop／障害／終了
                     |                    v
                     +--> Stopped <---- Stopping
                                      現在ファイル安全終了
```

ProcessingはRunningの中のフラグとし、状態数を増やさない。障害はLastError／RecoveryRequired情報として保持し、停止後も理由を消さない。Start失敗後に「監視開始成功」と表示しない。

| 処理 | キャンセル可否 |
|---|---|
| 次期日のDelay、未着手周期 | 即座にキャンセル可 |
| 開始前の検証／ファイル間 | 中断可能な境界で停止。作成済み管理記録があれば状態を保つ |
| 現在ファイルのCSV読込、Validation、DB Transaction | 途中キャンセルしない。既存同期処理を最後まで待つ |
| 結果Journal、ログ、移動または移動保留の確定 | 原則完了させる。失敗なら再実行不可状態を保って終了 |
| 同周期内の未着手ファイル | 開始しない。直下に残す |

StopAsyncは冪等とし、複数呼出しは同じ停止Taskを待つ。状態ロックでStoppingと新規開始禁止を確定→周期TokenをCancel→開始準備Task／スケジューラTaskをawait→追跡済みワーカーTaskをawait→所有Lease／CTS／Semaphore解放→Stoppedの順とする。処理中のTask.Runに周期Tokenを渡して勝手に打切り扱いにしない。ワーカーで通常CSVエラーまたは重大障害を検出した場合は停止要求を通知して自身を終了させ、別の監督Taskで停止を完了する。ワーカー自身から自分を待つStopAsyncをawaitしてデッドロックさせない。

FormClosingは初回e.Cancel=Trueとし、終了要求フラグで二重処理を防止。AsyncイベントからStopAsyncをawaitし、終了許可フラグを立てUIスレッドでCloseを再実行する。2回目は許可する。await完了前にFormや通知先をDisposeしない。バックグラウンド例外を必ず観測する。

停止に時間がかかる場合は「停止処理中：現在のCSV完了待ち」を表示し、30秒を目安に説明を追加するが、タイムアウトによる強制終了はしない。ローカルI/Oの停止時間上限も現行同期APIでは保証できない。OS強制終了・電源断は§7の再起動復旧で扱う。

## 9. ログ・設定保存・DB Schema／Migration

### 9.1 OperationLog／ErrorLog

既存OperationLogは種別、日時、経過時間、総件数、成功件数、失敗件数のみ。ファイル名／AttemptIdの恒久保存用の列・表は追加しない。ファイル特定は異常時ErrorLogと30日内のCompleted Journalを利用する。

| イベント | 記録 |
|---|---|
| 準備成功 | 周期監視開始を1件、件数0 |
| Atomic結果 | Decoratorで周期CSV取込を1件。成功5行は5／5／0、全件拒否5行は5／0／5、0行正常は0／0／0 |
| CSV解析例外 | 周期CSV取込失敗を1件。不明な総行数を捏造せず0、ErrorLogで読込失敗を説明 |
| DB結果不明 | 周期CSV取込結果不明を1件記録可能なら保存。件数0は未計数を意味し、未登録確定の意味にしない。Journal／状態で不明を表示 |
| 通常停止／異常停止 | 周期監視停止／周期監視異常停止を1件、件数0、ElapsedMsは停止待ち時間 |
| 空フォルダ／通常tick／通常スキップ | 毎秒DB記録しない。状態履歴で集約 |
| Move再試行 | 取込ログを再作成しない。同じMove障害を無制限に記録しない |

Validation ErrorLogは共通Serviceが実際の不正行を記録する。周期側で二重記録しない。ErrorTypeの既存分類を使い、追加一般障害はErrorTypes.System＋ErrorMessageの[CSVRead]／[DB]／[FileMove]／[FolderAccess]／[RecoveryRequired]等で区別。RowNumber／FieldNameは不明ならNULL、ConfigIdは存在するID、FileNameは対象があれば設定する。CSV本文・認証情報は保存しない。

ログ保存失敗を理由にDB結果を失敗へ変更しない。ログ保存を再帰的に試みない。WriteErrors成功→WriteOperation失敗のケースでも行エラー全体を再保存しない。通常CSVエラーのErrorLog保存だけが失敗した場合は0件拒否という事実を維持し、error移動と停止を完了する。

### 9.2 CsvImportConfigの拡張

| 列 | SQL定義案 | Entity | 旧行／新規の初期値 |
|---|---|---|---|
| ImportMode | TEXT NOT NULL DEFAULT 'SingleFile' CHECK (ImportMode IN ('SingleFile','PeriodicFolder')) | ImportMode Enum（Domain）、Repositoryで文字列変換 | SingleFile |
| WatchFolderPath | TEXT NULL DEFAULT NULL | String（Nothing許容） | NULL |
| WatchIntervalSeconds | INTEGER NOT NULL DEFAULT 60 CHECK (typeof(WatchIntervalSeconds)='integer' AND WatchIntervalSeconds BETWEEN 1 AND 604800) | Integer | 60 |

ConfigIdとの対応は既存行そのものによる。別設定テーブルや選択ConfigIdの保存先を追加しない。実行状態を永続化しない。フォルダ存在・ローカル判定はDB CHECKではなくApplication／Infrastructureで開始時検証する。NULLは未設定を表し、周期開始は拒否。手動へ切り替えても以前のフォルダ・周期は保持する。

SQL設計例（今回実行しない）：

```sql
ALTER TABLE CsvImportConfig ADD COLUMN ImportMode TEXT NOT NULL
 DEFAULT 'SingleFile' CHECK (ImportMode IN ('SingleFile','PeriodicFolder'));
ALTER TABLE CsvImportConfig ADD COLUMN WatchFolderPath TEXT NULL DEFAULT NULL;
ALTER TABLE CsvImportConfig ADD COLUMN WatchIntervalSeconds INTEGER NOT NULL
 DEFAULT 60 CHECK (typeof(WatchIntervalSeconds)='integer'
                  AND WatchIntervalSeconds BETWEEN 1 AND 604800);
```

ADD COLUMNの制約・DEFAULTの条件は[SQLite ALTER TABLE公式仕様](https://www.sqlite.org/lang_altertable.html)を参照。導入済みSQLite実行時バージョンで上記DDLをPhase 2試験し、NuGetバージョンを無断変更しない。

### 9.3 Repository／Service／Form

GetAll／Find／MapConfigのSELECTを3列拡張し、未知のModeや範囲外値は明示エラーにする。既存Save(config,mappings)は新規時にDB DEFAULTを利用し、既存行では従来5列のうち形式項目とMappingだけを更新する。読み出した監視値があっても形式保存で上書きしない。

ICsvImportConfigRepositoryにSaveMonitoringSettings(configId,mode,folder,seconds)を追加し、3列だけをパラメータUPDATEする。CsvConfigServiceも同操作を提供し、ConfigId存在・Mode・整数範囲等を検証する。更新0件なら削除済み設定として失敗。フォルダ値は保存時に正規化、周期開始時に実在・書込み・公開契約を再確認する。

単一UPDATEの原子性で3項目を一緒に保存する。既存形式保存のMapping Transactionとは混ぜない。新しいSQL定数はInfrastructure/Data/CsvImportConfigSql.vbへ配置し、変更するSELECT等もそこへ集約する。無関係なRepository SQLの移動は行わない。

CsvImportConfigFormは現行レイアウト・操作を維持する。新規設定の監視値はDB DEFAULT。既存設定の形式変更で監視値が消えない契約を回帰試験する。MainFormが監視値の編集／保存を担当するため、同じ設定入力を両画面に重複実装しない。

### 9.4 Migration設計

現行コードに版管理がないため、アプリ管理用PRAGMA user_versionを導入する。0＝未版管理（Schema確認必須）、110＝本設計のVer1.10 Schemaとする。SQLite内部用schema_versionは書き換えない。[SQLite user_version公式仕様](https://www.sqlite.org/pragma.html#pragma_user_version)

| 開いたDB | 動作 |
|---|---|
| DBファイルなし／完全な空DB | 拡張済み7テーブルとIndex、Seedを新規作成しuser_version=110を同じ初期化Transactionで確定 |
| user_version=0、Ver1.00の既知Schema、追加3列なし | バックアップ後に追加Migration |
| user_version=110、期待Schema／制約一致 | 何も追加せず利用。Migration／Seedを再実行しない |
| user_version=0なのに追加列の一部／全部がある | 手作業変更・不完全移行として自動推測しない。起動を止め保守確認 |
| 未知の版／110より新しい版／版とSchema不一致 | 自動修復・降格せず停止。対応版が必要と表示 |

Ver1.00移行の順序：

1. 起動時、MainFormやRepositoryの新列SELECTより前に実行する。監視開始イベントからMigrationを呼ばない。他の旧版／DB編集ツールを閉じた状態を運用前提とする。
2. sqlite_master／PRAGMA table_info等で既存7表・列・Index・FK・UNIQUE・CHECKを検証する。user_version=0という値だけで別アプリのDBを移行しない。未知の表・列・制約差分は停止して確認する。
3. 整合した移行前バックアップを上書きしない名前で保存する。Microsoft.Data.SqliteのBackupDatabase相当のオンラインバックアップAPIを用い、開いたDBファイルだけを単純コピーしない。バックアップ接続を閉じ、存在・読込・integrity_check／foreign_key_checkを確認。保存／検証に失敗したら原本を変更しない。
4. 非遅延の書込みTransactionを取得し、版と構造を再確認。3つのALTER TABLE ADD COLUMNとPRAGMA user_version=110を同じTransactionに参加させる。各Command.Transactionを設定し、明示COMMITは最後だけ。
5. 追加列・旧データ保持・foreign_key_check等を確認してCommit。途中例外ではRollbackし、アプリ起動を止める。commit結果が不明なら、新しい接続で版／構造を確認するまで再試行しない。
6. 既存DBへの移行時にはSeedを再投入しない。削除済みデモデータを復活させない。旧7表をDROP／再作成しない。次回起動は110の確認だけを行う。

DatabaseInitializerは「新規作成」「既存移行」「現行版検証」を分岐する構成へ将来変更する。DDLはDatabaseSchema／DatabaseMigrations、手順はDatabaseMigratorに置く。BeginTransactionのネストや、途中のDDLだけがcommitされる分割は避ける。

バックアップAPIの挙動は[Microsoft.Data.Sqlite: backup](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)を参照する。バックアップ取得とMigrationの間に外部更新が入らないよう単一アプリ運用を前提にし、ロック取得失敗は開始中止。外部プロセスを強制終了しない。

### 9.5 互換性と停止中のDB操作

Ver1.00 DBからVer1.10への前方移行を保証対象にする。既存ConfigId、Mapping、MeasurementData、マスタ、ログの行と値を保持し、CSV形式編集で監視値を失わない。既存新規INSERTの列省略は新列DEFAULTで互換性を保つ。

更新済みDBを旧EXEで使い続ける後方互換は保証しない。旧EXEは新列を理解せず、既存初期化がSeedを再投入する可能性もある。旧版へ戻す場合は退避済みVer1.00成果物・移行前DBを使用し、移行後の測定データを失わない移送は別途保守判断とする。自動downgradeはしない。

監視中は編集／削除／マスタ／CSV設定を禁止し、検索・ログ閲覧・CSV出力を許可する。接続は操作ごとに生成し共有しない。検索全体の非同期化等、無関係な機能改修は範囲外。監視フォルダへCSV出力すると自分の出力も取込対象となるため、USER_GUIDEで出力先を分ける運用を説明する。

## 10. テスト計画

今回は設計のみ。下表は将来実装時の試験であり、合格済みではない。単体＝代替I/Oと手動TimeProvider、結合＝一時ディレクトリ＋実CSV＋一時SQLite、GUI＝Windows実機で操作・画面記録。既存71ケースの削除／期待値緩和はしない。

| ID | レベル | 操作・条件 | 期待結果 |
|---|---|---|---|
| T01 | 単体 | 周期60秒で開始 | 準備後、時刻を進めず初回1回。二重Start不可 |
| T02 | 単体 | 周期1秒／604800秒を各実施 | t0即時、次は指定秒境界。7日実待機せず時刻注入 |
| T03 | 単体・GUI | 0／604801／負数／小数／空入力 | 開始拒否。DB／列挙を呼ばない |
| T04 | 単体 | 10秒周期、15秒かかるバッチ | 0秒開始、10秒スキップ、15秒完了、20秒開始。最大並行数1 |
| T05 | 単体 | 負荷で複数期日遅延／壁時計変更／復帰 | 追いつき連続実行なし。未来の固定期日へ |
| T06 | 単体 | 0ファイルで1秒周期を長時間 | 正常待機、OperationLog毎秒増加なし |
| T07 | 結合 | 1CSV／複数CSV、同時刻・異名 | 更新日時昇順、同時刻は名前順で安定、1ファイルずつ |
| T08 | 結合 | .csv／.CSV／.CsV／.csvx／.txt／サブフォルダ | 前3種類だけ対象。success/error／管理領域を再帰しない |
| T09 | 結合 | 周期途中にCSV追加・削除・置換 | 追加は既存未完了順序を終えた次周期。消失・検出できた置換はDB実行せずRecoveryRequiredで停止 |
| T10 | 結合 | 全行正常 | 期待件数登録、successへ、正常ログ1件 |
| T11 | 結合 | 周期で正常3行＋異常2行、1行に複数エラー | AddRange未呼出し、DB0行、5／0／5、不正行2・正常保留3。error、後続未処理、監視停止 |
| T12 | 結合 | 全行エラー／CSV内重複／既存DB重複 | 周期は全件未登録、errorへ、後続中断、監視停止 |
| T13 | 結合 | 空／ヘッダーのみ | 0件正常としてsuccess。空フォルダと区別 |
| T14 | 結合 | successなし／errorなしを各実施 | 取込前に両方作成。準備が完了するまでDB呼出しなし |
| T15 | 結合 | success／errorに同名、同時衝突 | 元ファイルを上書きせず別名、100回上限で停止可能 |
| T16 | 結合 | 書込みハンドル保持（共有設定を複数試す） | 対象と後続を直下保留、DB／error移動なし。当該バッチ終了、監視Running |
| T17 | 結合 | T16後ハンドルを閉じ次期日 | 同じ先頭から1回正常取込。更新日時が新しくなっても追越しなし |
| T18 | 結合 | ガード保持中に追記／削除／rename試行 | 共有規則により拒否。既存Adapterは読める |
| T19 | 実機 | 対象SCADAの実際の書込み手順 | 公開契約を検証。断続open/closeなら安全な公開方法を確定 |
| T20 | 単体 | tickとStopを同時競合 | Stopping確定後に新規Taskなし。確定済みTaskは待つ |
| T21 | GUI | 手動／周期切替、開始→停止 | パネル・ラジオ・同一Buttonが状態表どおり |
| T22 | GUI | Running／Stopping中に設定・手動入口操作 | 編集・削除・マスタ・手動取込・設定・モード・フォルダ・周期は禁止。検索・ログ・出力は利用可、停止後復帰 |
| T23 | GUI | 履歴選択／101件通知／大量通知 | 状態は変化しない。履歴100件以下、通知キュー有界、現在状態は最新 |
| T24 | 単体・GUI | 待機中に停止／開始準備中に終了 | 未着手周期なし、開始準備の所有リソース解放、再開始可能 |
| T25 | 結合・GUI | CSV読込中／DB処理中／移動中に停止 | 現在ファイルは確定まで継続、次ファイルなし、DBを強制中断しない |
| T26 | GUI | Stopped／Running／ProcessingでFormClosing | 非同期停止後に終了。UI応答、二重Close、Dispose後通知を検証 |
| T27 | 結合 | ローカルフォルダ削除／アクセス権変更 | 未処理例外なし、理由表示、安全停止、ログ可能なら記録 |
| T28 | 結合 | success作成失敗／error作成失敗 | Atomic入口呼出し0、元CSV維持 |
| T29 | 結合 | INSERT途中FK／UNIQUE競合／DB locked／容量不足 | 全体Rollback、部分commitなし。元CSV保留、error移動禁止、RecoveryRequired、後続停止 |
| T30 | 結合 | DB成功直後Moveを失敗させる | 登録件数は1回分、MovePending。再開始して移動のみ |
| T31 | 結合 | T30でアプリ再起動 | Journalで抑止、Atomic入口再呼出し0 |
| T32 | 結合 | ImportStarted保存後・DB呼出し前に強制終了 | 未実行と断定せずRecoveryRequired、自動再登録なし |
| T33 | 結合 | DBcommit後・ResultKnown保存前に強制終了 | 結果不明として停止。重複チェックで強行しない |
| T34 | 結合 | Move成功後・Completed保存前に強制終了 | 移動先指紋一致で完了扱い、DB再実行なし |
| T35 | 結合 | Journal破損・実行中Attempt消失・書込み不可／元CSV置換 | 検出できる不整合は停止・保留。利用登録との突合はない。痕跡なき削除の完全検出は保証外と手順に記載 |
| T36 | 結合 | 元と移動先の両方あり／両方なし／ハッシュ不一致 | 自動推測・上書きなし、RecoveryRequired |
| T37 | 結合 | Validationログ成功後OperationLog失敗 | LogRecorded=False、登録結果維持、同じ行ログ再書込みなし |
| T38 | 結合 | DB全体使用不可でErrorLogも失敗 | 再帰ログなし、状態表示、停止、管理記録で可能な限り保全 |
| T39 | 結合 | A正常、BがValidation／解析不正、C正常 | Aのみ登録＋success、Bは0登録＋error、C直下未処理、監視異常停止。次周期もC未処理 |
| T40 | 結合 | A正常、B致命DB障害、C未処理 | A確定、B保留、C直下のまま。後続開始しない |
| T41 | 結合 | 同じ監視ルートへ2インスタンス開始 | 所有Leaseは1つのみ。所有側停止後のみ別側が開始可能 |
| T42 | 結合 | ../、兄弟prefix、junction／reparse対象 | ルート外操作なし。CSV以外の移動・既存フォルダ削除なし |
| T43 | 結合・GUI | Configごと監視3項目DB保存→再起動／削除ConfigId | 選択したConfig値を復元、必ずStopped。未選択／保存失敗で開始不可 |
| T44 | 結合 | Ver1.00 DBコピーに追加Migration | 3列と版だけを追加し、既存CSV設定・Mapping・MeasurementData・全マスタ・ログの値／ID保持、Seed再投入なし |
| T45 | 性能・GUI | 1万行CSV、1000CSV、低速I/O | 順次・UI応答・メモリ・停止待ち・ログ増分を実測。時間閾値は対象PCで合意 |
| T46 | 回帰 | 既存71ケースすべて | 既存期待値のまま合格。新規ケースで置換しない |
| T47 | GUI回帰 | 手動取込、検索、詳細、編集、1件／複数削除 | Ver1.00操作維持、選択ID・検索条件・件数が正しい |
| T48 | GUI回帰 | CSV出力、CSV設定、マスタ、ErrorLog、OperationLog | 内容・形式・検索が維持される |
| T49 | 結合 | 全件拒否CompletedのBを修正して同名再投入、DBバックアップ復元手順 | 同名だけでは開始不可。明示対応付け後に元Attemptと関連する新Attempt。DB差替えの自動検出は保証せず手順で照合 |
| T50 | 結合 | 長いセッション、大量の正常スキップ | UI／通知有界、スキップ毎秒DB保存なし、経過時間overflowなし |
| T51 | 手動回帰 | 既存ImportAndSaveで正常3＋異常2 | DB3行、既存5／3／2。周期用Configでも入口が手動なら変わらない |
| T52 | 結合 | B修正再投入・明示対応付け後、ユーザーが開始 | Bの新時刻にかかわらずB修正版→C。正常確定後だけC開始。自動再開なし |
| T53 | 単体・結合 | 周期の未知設備を事前検出／DB重複のみ／CSV内重複のみ | 各ケースAddRange0回、登録0、error、停止。手動の未知設備挙動は変更しない |
| T54 | 結合 | 最初の行INSERT後、次のINSERT失敗を注入 | 1 TransactionがRollbackし、このファイルの行は0。DELETEによる補償なし |
| T55 | 結合 | Migration2回実行／途中失敗／commit前強制終了 | 反復は無変更。中断時は旧版全体または新版全体、途中3列だけ確定しない |
| T56 | 結合 | バックアップ失敗／破損DB／未知版／版と列不一致 | 原本の変更や自動修復なし、アプリ起動を停止し理由表示 |
| T57 | 結合 | 新規DB初期化 | 拡張Schema＋初期値＋版110、Seed初回のみ、次回データ不変 |
| T58 | 結合・GUI | 形式設定編集／新規／Mapping変更／監視設定保存 | 形式編集で監視3列保持、監視保存で形式とMapping保持。新規はSingleFile／NULL／60 |
| T59 | 結合 | Mode不正、秒0／604801／小数／NULL、フォルダNULL | DB／Service制約を確認。手動は未設定可、周期開始はフォルダ必須 |
| T60 | GUI | 未保存値のConfig切替、保存失敗、手動へ変更 | 保存・破棄・取消、別Config混入なし。手動へ変更してもフォルダ値を消さない |
| T61 | 結合 | UNC／SMB／NAS経由／ネットワーク割当ドライブ | 開始拒否、DB登録なし、ローカル通常フォルダだけ許可 |
| T62 | 単体・結合 | B異常検出中にtick、error移動失敗、再開始で移動復旧 | Cは常に未処理。移動のみ復旧しStopped、CorrectionPending。修正版確認と再度のユーザー開始が必要 |
| T63 | 結合 | 正常0行のCSVと空フォルダ | 前者success・0行結果ログ、後者取込ログなし、両方継続 |
| T64 | 結合 | J-01=B：B修正でCより新時刻／古いAが使用中でBが読取可能 | 確定済み順序優先。B修正版→C、A使用中は後続保留。現在時刻で再ソートしない |
| T65 | 結合 | A書込み中、B/C読取可能で初回周期 | A/B/C全てDB未登録・直下、監視Running。次周期もAから確認、同種履歴を集約 |
| T66 | 結合 | T65のAが次周期で完成しmtimeがB/Cより新しくなる | A→B→C。確定済みPositionを維持、mtime変更をアプリから行わない |
| T67 | 結合 | A成功、B全件拒否、C保留後に停止・再起動 | AをRollback／再登録しない。Bの位置でCorrectionPending、Cを移動／登録しない |
| T68 | 結合 | B修正版をCより新しいmtimeで再配置し明示確認 | 元Attempt／BindingIdと関連する新AttemptでB→C。success移動・正常Completed前はC不可 |
| T69 | 結合 | T68の修正版が再度Validationエラー | DB0、error、異常停止。根本と直前の失敗Attemptを保持し、C保留 |
| T70 | 単体・GUI | 修正版未選択／同名CSVだけ存在／確認取消／対応不明 | 推測しない。Stoppedで修正版確認待ち、CのAtomic呼出し0 |
| T71 | 結合 | BのDBcommit結果不明で修正版を指定しようとする | 元B保留、error移動なし、RecoveryRequired。CorrectionPendingに分類せず対応付け拒否、C未処理 |
| T72 | 結合 | 書込み中Aの保留後に通常停止・アプリ再起動 | 永続OrderのA→B→Cを復元。A完成時刻で並べ直さず、開始はユーザー操作 |
| T73 | 結合 | B拒否Completed直後・Order更新前に強制終了 | 記録を突合しBのCorrectionPendingを復元。B再取込・C先行なし |
| T74 | 結合 | 修正版success移動・Completed後、Cursor更新前に強制終了 | メタデータのみ復旧しCursorを前進。修正版・AのDB再呼出し0、手動再開始後にC |
| T75 | 結合・GUI | 確認後の候補内容／設定変更、後続C・別未解決・成功済みを候補指定 | 指紋／設定差分は再確認待ち、禁止候補は拒否。C先行なし。確認済み候補のロックのみならRunningで再試行 |
| T76 | 結合 | 未解決Orderが参照する失敗Completedが30日超 | 参照記録・対応付け・Orderは削除しない。Order解決から30日後にまとめて保持期限判定 |
| T77 | 結合 | 既存Orderの途中で古いmtimeの新規CSVが到着 | 確定済み順序を追い越さない。既存完了後の次周期に新規群を通常キーで確定 |
| T78 | 結合 | Order破損、参照Attempt欠落、複数未完了Order、Cursorの不正前進 | 検出可能な不整合はRecoveryRequired、元CSV保留、DB再呼出し・後続処理なし |

TestDataは一時領域に作成し本番DBを使わない。SQLiteプールを解放してから一時DBを片付ける。クラッシュ注入はテスト専用子プロセスで行い、ユーザーが起動中のアプリを停止しない。UNC／SMBは正式対応外として開始拒否を試験する。

## 11. 実装Phase最新版

| Phase | 対象 | 完了条件・次工程／残リスク |
|---|---|---|
| 0（今回） | 2文書最終改訂、D-01～D-07・J-01=B確定反映 | 設計確定、ユーザー判断待ちなし。既存ソース・DB・71テスト・配布物を変更しない |
| 1 | Application DTO／Interface、時刻注入、状態・周期・停止の基礎 | T01～06・20、代替I/Oで検証。実CSV・実DBに接続しない。順序・復旧の実I/O実装は予定どおりPhase 4。二重実行防止を含む基礎のみ |
| 2 | CsvImportConfig Entity／SQL／Repository／Service、Migration／新規初期化 | T43～44・55～60。一時DB／Ver1.00コピーで保持・中断・反復試験。本番DBへ適用しない |
| 3 | CsvImportServiceの共通化、Atomic入口、周期結果・ログ | T10～13・29・37～38・51・53～54。手動既存71テストと3＋2の差を確認 |
| 4 | ファイルLease・Journal・移動・復旧、先行書込み中の後続保留、エラー停止時の順序保持、修正版優先、Journal順序復旧・追越し禁止 | T07～09・14～19・28～42・49・52・61～78。J-01=B確定済み。明示対応付けのService契約を含む。DB処理済み再呼出し0 |
| 5 | MainFormモード・保存・状態履歴、修正版確認画面接続、Program DI、ログ種別 | T21～23・43・58・60・70・75、禁止／許可操作、既存UI回帰 |
| 6 | FormClosing／StopAsync統合と設定未保存確認 | T20・24～26・62、現在ファイルの確定と新規禁止 |
| 7 | 全結合・既存71回帰・性能・実SCADA・USER_GUIDE | 全試験＋実画面・復旧手順記録。公開契約の装置確認 |
| 8 | 別Ver1.10配布作成、移行を含む実機確認 | 承認済み実装をPublish、Ver1.00成果物保持。今回は行わない |

実装前の基準取得・Ver1.00退避は将来Phase 1着手時に行う。今回Build／Testを実行したとは扱わない。各Phaseで「変更ファイル、実装内容、Project／Package／Reference差分、Build／Test結果、次工程、残リスク、設計変更」を報告する。

実装時の検証順はrestore→Release build --no-restore→Release test --no-build --no-restore。新規試験を追加し、既存71件の期待値を緩めない。Schemaに依存した期待値の変更が必要なら、旧DB互換試験を残した上で理由を個別報告する。

## 12. ユーザー判断の反映・残る要判断事項

### 12.1 D-01～D-07確定結果

完了報告ではJournal、公開方式、空CSV、操作制限、ストレージ、長期履歴の6項目を整理し、DB設定保存への変更を別途報告する。漏れを防ぐため、旧D-01～D-07の7IDはすべて追跡する。

| ID | 確定仕様 | 旧案からの変更 |
|---|---|---|
| D-01 | .periodic-importに永続Journal、未解決再取込禁止 | LocalAppData利用登録との二重管理を削除。削除・改変の完全検出は保証外 |
| D-02 | 公開CSVは不変、tmp→close→rename推奨、直接書込みはwriteハンドル保持 | 選択待ちから正式な運用前提へ |
| D-03 | CsvImportConfigへ3列追加、既存DBを追加Migration | 監視設定JSON案を廃止、実行状態保存なし |
| D-04 | Parser上正常な0行CSVはsuccess | 確定。「データ0件で正常終了」 |
| D-05 | Windowsローカル通常フォルダのみ | UNC／SMB／NASは正式対応外 |
| D-06 | 監視中・停止処理中の編集／削除／マスタ／設定／手動取込等は禁止 | 検索・ログ・CSV出力は利用可、停止後復帰 |
| D-07 | 既存OperationLog＋Journal、Completed保持30日 | 成功履歴専用のDB追加はしない |

追加の重要変更：周期だけAll-or-Nothing、通常エラーはerror移動後に監視異常停止、DB障害は元CSV保留・RecoveryRequired。手動部分登録を維持する。

### 12.2 J-01：B「元の時系列順序を厳格に維持」（確定）

通常順序はLastWriteTimeUtc昇順→FileName OrdinalIgnoreCase昇順→Ordinal昇順。Journalで確定した未完了順序を、現在の更新日時による並べ直しより優先する。

- 先行CSVが書込み中なら後続も保留し、当該周期だけ終了。監視を継続し次周期は同じ先頭から確認する。
- 通常エラーはそのCSVを全件未登録としてerrorへ移動し異常停止。先行成功は維持、後続は直下保留。
- 原因Attemptの位置を修正版待ちとして保持する。ユーザーが元Attemptと候補を明示確認し、指紋・設定を再照合して修正版を優先する。正常確定後のみ後続へ進む。
- DB結果不明／Journal不整合はRecoveryRequiredで停止。修正版待ちとは別扱いとする。
- 時刻の書換え、後続の勝手な移動、名前だけの推測、Journal削除による解除、確定済み成功の再登録、自動再開は禁止。

方式は§6.5・7.6の既存Journal拡張を採用する。利点は後続の追越し防止と再起動時の順序復元。負担は先頭待ちによる処理遅延と修正版の明示確認操作であり、製造順序を優先するJ-01=Bのトレードオフとして受け入れる。汎用永続キューや新DBテーブルは追加しない。

### 12.3 Phase 0確定・Phase 1への移行可否

**Ver1.10 Phase 0 設計確定。Phase 1の実装へ進めてよい状態。** D-01～D-07およびJ-01=Bは確定済みで、新たな重大な要判断事項はない。公開契約の実SCADA確認、障害注入、GUI確認、性能測定は将来の検証であり、未決の仕様選択とは区別する。

Phase 1は予定どおりApplication DTO／Interface、時刻注入、状態管理、固定周期、二重実行防止、Stop制御の基礎に限定する。Phase 2・3も範囲を広げない。順序・修正版・Journal復旧の実装はPhase 4、画面接続はPhase 5で行う。

今回は設計書の最終改訂のみという指示に従い、Phase 1の実装を開始しない。

## 13. 影響ファイル一覧

### 13.1 今回変更するファイル

- docs/V1_10_PERIODIC_IMPORT_REQUIREMENTS.md
- docs/V1_10_PERIODIC_IMPORT_DESIGN.md

以下は将来実装予定であり、今回変更していない。パスはリポジトリルート基準。

### 13.2 新規予定

| ディレクトリ | ファイル・用途 |
|---|---|
| src/ManufacturingDataApp.Application/Services/ | PeriodicImportService.vb |
| src/ManufacturingDataApp.Application/DTOs/ | PeriodicImportOptionsDto.vb、PeriodicImportState.vb、PeriodicImportStatusDto.vb、PeriodicFileResultDto.vb、ImportProcessingRecordDto.vb、PeriodicImportOrderDto.vb、CorrectionBindingDto.vb |
| src/ManufacturingDataApp.Application/Interfaces/ | IPeriodicImportFileStore.vb、IImportProcessingJournal.vb、IPeriodicCsvImportExecutor.vb |
| src/ManufacturingDataApp.Domain/Constants/ | ImportMode.vb（設定のSingleFile／PeriodicFolder。保存方針Enumとは別） |
| src/ManufacturingDataApp.Infrastructure/PeriodicImport/ | PeriodicImportFileStore.vb、JsonImportProcessingJournal.vb、PeriodicCsvImportExecutor.vb |
| src/ManufacturingDataApp.Infrastructure/Data/ | DatabaseMigrator.vb、DatabaseMigrations.vb、CsvImportConfigSql.vb |
| src/ManufacturingDataApp.Infrastructure/Logging/ | PeriodicImportLogWriter.vb |
| src/ManufacturingDataApp/Presentation/ | PeriodicImportStatusPresenter.vb |
| src/ManufacturingDataApp/Forms/ | PeriodicImportRecoveryForm.vb（元Attemptと修正版の確認） |
| tests/ManufacturingDataApp.Tests/Application/ | PeriodicImportServiceTests.vb、AtomicCsvImportTests.vb |
| tests/ManufacturingDataApp.Tests/Infrastructure/ | PeriodicImportFileStoreTests.vb、ImportProcessingJournalTests.vb、DatabaseMigrationTests.vb、MonitoringConfigPersistenceTests.vb |
| tests/ManufacturingDataApp.Tests/Integration/ | PeriodicImportIntegrationTests.vb、PeriodicImportRecoveryTests.vb |
| tests/ManufacturingDataApp.Tests/Presentation/ | PeriodicImportUiStateTests.vb |
| tests/ManufacturingDataApp.Tests/TestSupport/ | ManualTimeProvider.vb、PeriodicImportFakes.vb |

設定JSON用Store／Interface、LocalAppDataの監視ルート利用登録用クラスは作成しない。

### 13.3 変更予定

| ファイル | 将来の変更内容 |
|---|---|
| src/ManufacturingDataApp.Application/Services/CsvImportService.vb | 手動入口維持、Atomic入口、共通検証／重複／ログ処理、周期専用事前検証 |
| src/ManufacturingDataApp.Domain/Entities/CsvImportConfig.vb | 監視3プロパティと初期値 |
| src/ManufacturingDataApp.Application/Interfaces/ICsvImportConfigRepository.vb | 監視設定だけ保存する契約 |
| src/ManufacturingDataApp.Application/Services/CsvConfigService.vb | 監視設定保存・検証、既存形式保存の分離 |
| src/ManufacturingDataApp.Infrastructure/Repositories/CsvImportConfigRepository.vb | 拡張読取、監視3項目UPDATE、既存形式保存で監視値を保持 |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseSchema.vb | 新規DBの3列定義 |
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseInitializer.vb | 新規・移行・現行検証の分岐、既存DBへSeedを再投入しない |
| src/ManufacturingDataApp/Forms/MainForm.vb | モード／周期／保存パネル、操作制限、修正版確認入口、状態、FormClosing |
| src/ManufacturingDataApp/Program.vb | Migrator連携、周期用Serviceと設備Repository依存の手動DI |
| src/ManufacturingDataApp/Forms/OperationLogForm.vb | 周期用ログ種別候補 |
| tests/ManufacturingDataApp.Tests内の既存Fake | Interface追加に必要なメソッド対応があれば機械的変更。既存71ケースの意味は維持 |
| docs/USER_GUIDE.md等 | 周期全件拒否と手動部分登録の違い、管理フォルダ保全、移行・復旧・公開契約を実装後に記録 |

CsvImportConfigFormは既存Save契約を保つことで原則コード／レイアウト変更不要。将来監視入力欄をそこにも追加する案は採用しない。現行ソースの新Entity生成が監視値リセットを起こさないことをテストする。

### 13.4 変更不要の範囲

| 範囲 | 理由 |
|---|---|
| CsvImportForm／MeasurementDetailForm／MasterForm | 手動導線・業務挙動を維持、監視中の制限はMainForm入口で行う |
| CsvHelperAdapter／ICsvFileAdapter／ValidationService | 既存Parse・Mapping・行Validation再利用 |
| MeasurementDataRepository／IMeasurementDataRepository | 既存AddRangeの1 Transactionを利用 |
| DbLogWriter／ログEntity／LogViewService／ErrorLogForm | 既存列・保存契約利用 |
| 既存MeasurementData／マスタEntity／標準フィールド | 新たな行仕様を導入しない |
| DatabaseConnectionFactory | 既存接続とforeign_keys設定を利用 |
| Solution／Project／Package／Reference | 新規依存を前提としない |
| data／docs/designの既存Excel／Publish Profile／dist | Ver1.00基準を保全。配布更新は将来Phase 8 |

既存71件に対して、周期All-or-Nothingの期待値を手動テストへ上書きしない。追加列に影響するSchema試験は追加試験で検証し、必要な既存試験修正は旧動作の保証を残して個別レビューする。今回テストコードは一切変更しない。

## 14. Phase 0最終完了報告・改訂差分

| 最終改訂指示書の報告項目 | 反映結果 |
|---|---|
| 1 変更ファイル | §13.1の要件定義書・基本設計書のみ |
| 2 J-01=B | 通常キーで確定した順序をJournal保存。未完了順序は現在mtimeより優先（§6.1・7.6） |
| 3 書込み中の後続 | 先行と後続を直下保留、そのバッチ終了、Running継続、次周期も同じ先頭（§6） |
| 4 Validation後の後続 | 原因CSVは全件未登録→error→異常停止。後続直下、先行成功をRollbackしない（§4・6） |
| 5 修正版優先 | 元OrderのPositionを維持し新Attemptを関連付け。正常Completed後のみCursor前進（§6.5・7.6） |
| 6 安全な識別 | 利用者が元Attemptと候補を明示確認。指紋・Config／Mapping・ルートを実行前再照合。同名だけの推測禁止（§3.5・6.5） |
| 7 Journal追加 | OrderId／Entries／Position／Cursor／HeadDisposition、根本・直前失敗Attempt、設定スナップショット、CorrectionBinding、参照保持（§7.6） |
| 8 DB障害との区別 | 全件未登録確定＋error移動完了のみCorrectionPending。結果不明／Journal不整合はRecoveryRequired、元CSV保留、再登録禁止（§6～7） |
| 9 テスト | T01～T64のID維持、T09・16・17・49・52・62・64更新。T65～T78を追加、計78計画ケース。今回実行なし（§10） |
| 10 Phase 4 | 先行書込み中の後続保留、エラー順序保持、修正版優先、Journal順序復旧、追越し禁止。Phase 1～3は拡大なし（§11） |
| 11 Phase 0確定 | Ver1.10 Phase 0 設計確定。D-01～D-07・J-01確定済み（§12） |
| 12 新たな要判断 | なし。実装・実機検証は今後必要であり、実装完了を意味しない |
| 13 保全・未実行 | ソース・実DB・既存71テスト・dist・Excel・Publish Profile・Project／Package／Referenceは変更対象外。Build／Test／Migration／Publishは実行しない |

前回確定した周期All-or-Nothing、手動部分登録維持、CsvImportConfigの3列追加・追加専用Migration、監視操作制限、.periodic-import単独管理は維持する。変更は時系列保証とそれに必要な順序・修正版復旧設計である。

おすすめ：次の作業単位はPhase 1の基礎実装。理由：確定した型・周期・排他・停止制御を代替I/Oで独立検証できる。今すぐ行うこと：本最終設計を確認し、Phase 1着手を指示する。**Phase 1の実装へ進めてよい状態だが、今回は開始していない。**
