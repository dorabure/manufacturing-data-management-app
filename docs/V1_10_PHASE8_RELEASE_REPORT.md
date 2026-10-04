# Ver1.10 Phase 8 Release Report

## 1. Release概要

Phase 8で、Ver1.10ソースからwin-x64 self-contained single-fileのRelease成果物、dist、配布ZIPを作成しました。

Phase 8時点の状態：**Phase 8完了・Ver1.10 Release成果物作成完了・ユーザー最終レビュー待ち**。以下の1～21節はPhase 8の記録です。修正版の結果・配布ZIP hashは末尾の「Phase 8.1最終配布修正」を参照してください。

実SCADA、実デスクトップDPI、既知の性能制約とPENDING試験はReleaseによって解消したものではありません。

## 2. Phase 8開始Baseline

Phase 8開始時にrestore、Release Build、全テストを実施しました。

- restore：成功。ただしNU1900×3
- Release Build：0 errors
- 初回全テスト：353件中352 Pass／1 Fail

Failは`AtomicCsvImportTests.MixedRows_EntryNotModeDeterminesPolicy(atomic: True, mode: PeriodicFolder)`のFixture後片付けでの`test.db`共有IOExceptionでした。過去に一度記録されていた問題が再現したため、Publishを開始せず原因を調査しました。

## 3. Release Build結果

最終ソースで`dotnet build -c Release --no-restore`成功、0 errors。Phase 8開始時にはPresentation／Infrastructure／Testsの3プロジェクトで`NU1900`が発生し、文書更新後の最終BuildでもTestsプロジェクトで残りました。これ以外の警告はありません。

## 4. Release Test結果

最終Release Test：

```powershell
dotnet test -c Release --no-build --no-restore
```

結果：**354 Pass／0 Fail／0 Skip**（2026-09-28 JST、1分50秒）。

Phase 7の353件に、Phase 8で追加した一時SQLite後片付けの回帰1件を加えています。既存330件の削除、Skip追加、Assertion削除・弱体化はありません。

## 5. 脆弱性監査

```powershell
dotnet list src/ManufacturingDataApp/ManufacturingDataApp.vbproj package --vulnerable --include-transitive
```

このコマンドは既知の脆弱Packageなしと返しました。しかしPhase 8開始時にはNU1900×3（Presentation／Infrastructure／Tests）が発生し、文書更新後の最終BuildでもTestsプロジェクトでNU1900が残りました。NuGet脆弱性情報サービスへ常時安定して到達できる状態は確認できていません。

結論：既知の脆弱性はこのコマンドで検出されなかった一方、**脆弱性監査完了とは扱いません**。Package更新・警告抑制はしていません。

## 6. Publish設定

既存`src/ManufacturingDataApp/Properties/PublishProfiles/win-x64.pubxml`を使用し、変更していません。

| 設定 | 値 |
|---|---|
| RuntimeIdentifier | win-x64 |
| SelfContained | true |
| PublishSingleFile | true |
| PublishTrimmed | false |
| IncludeNativeLibrariesForSelfExtract | true |

## 7. Publish結果

旧`artifacts/publish/win-x64`は安全ポリシーにより削除できなかったため`win-x64.pre-phase8`へ退避し、正規パスに空フォルダを作ってからPublishしました。退避物は配布ZIPに含めていません。

通常restoreはRID資産を持たないため、最初の`--no-restore` PublishはNETSDK1047で停止しました。win-x64向けに明示restoreした後、同一Profile・`--no-restore`で再Publishし成功しました。

```powershell
dotnet restore src/ManufacturingDataApp/ManufacturingDataApp.vbproj -r win-x64
dotnet publish src/ManufacturingDataApp/ManufacturingDataApp.vbproj -c Release -p:PublishProfile=win-x64 -o artifacts/publish/win-x64 --no-restore
```

Exit code 0。PublishディレクトリにEXEとPDBがあり、PDBはdistへコピーしていません。DB、WAL、TestResults、テストCSV、一時Journal、owner.lockはPublish成果物にありません。

## 8. Publish EXE

| 項目 | 値 |
|---|---|
| ファイル | artifacts/publish/win-x64/ManufacturingDataApp.exe |
| サイズ | 164,352,875 bytes |
| SHA-256 | CC735D1270DD4D31AED5F9FAD42945F065625721FF721E1D69EDFFBD033379B2 |

## 9. DISTRIBUTION_README更新

`docs/DISTRIBUTION_README.md`をVer1.10化しました。概要、Windows 10/11 64bit、Runtime不要、起動、手動取込、sample CSV、周期監視、Folder・周期、success/error、書込み中CSV、修正版、Recovery、安全終了、保存先、ネットワーク非対応、SmartScreen、USER_GUIDEへの導線を簡潔に記載しています。

## 10. USER_GUIDE最終確認

`docs/USER_GUIDE.md`を確認し、SingleFile、PeriodicFolder、監視Folder、周期、Start／Stop、success、error、.periodic-import、CorrectionPending、修正版指定・再確認、RecoveryRequired、安全停止、SCADA公開方式、ネットワークFolder非対応が記載済みであることを確認しました。Phase 8では内容を変更していません。

## 11. dist内容

`scripts/Prepare-Distribution.ps1`でdistを再構築しました。Windows PowerShellの実行は既存スクリプトの構文解釈エラーとなったため、同じスクリプトをPowerShell 7（pwsh）で実行して成功しました。スクリプト本体は変更していません。

`dist/ManufacturingDataApp`は次の4ファイルのみです。

| ファイル | サイズ |
|---|---:|
| ManufacturingDataApp.exe | 164,352,875 bytes |
| README.md | 3,135 bytes |
| USER_GUIDE.md | 10,876 bytes |
| sample_measurement.csv | 229 bytes |

PDB、DLL、deps.json、runtimeconfig.json、.git、bin、obj、TestResults、ソース、DB、WAL、Journal、owner.lockはありません。

## 12. Publish EXEとdist EXEのSHA-256比較

| 配置 | SHA-256 |
|---|---|
| artifacts/publish/win-x64/ManufacturingDataApp.exe | CC735D1270DD4D31AED5F9FAD42945F065625721FF721E1D69EDFFBD033379B2 |
| dist/ManufacturingDataApp/ManufacturingDataApp.exe | CC735D1270DD4D31AED5F9FAD42945F065625721FF721E1D69EDFFBD033379B2 |

完全一致です。

## 13. 配布EXE Smoke Test

**未実施（安全な隔離環境なし）**。

実ユーザーの`%LocalAppData%\ManufacturingDataApp`を試験目的で作成・変更しないため、dist EXEは起動していません。本番コードへDataDir指定等のテスト専用機能も追加していません。

隔離されたWindows Sandboxまたはテスト専用ユーザープロファイルで、初回DB作成、MainForm表示、終了・再起動、sample CSV取込3件、OperationLog、周期モード表示を確認してください。

## 14. Authenticode状態

`Get-AuthenticodeSignature`の結果：**NotSigned**。個人制作の未署名EXEです。署名の追加はしていません。配布READMEにSmartScreenの説明を残しています。

## 15. 配布ZIP

| 項目 | 値 |
|---|---|
| ファイル | artifacts/release/ManufacturingDataApp_Ver1.10_win-x64.zip |
| サイズ | 69,042,686 bytes |
| SHA-256 | C1FE10A279B605A27A2F419923E78D82388D0EF4EEE945BBA6EBA7E13705BD23 |

## 16. ZIP再展開監査

別の一時フォルダへ展開して直接監査しました。構成は次の4ファイルだけです。

```text
ManufacturingDataApp/
├─ ManufacturingDataApp.exe
├─ README.md
├─ USER_GUIDE.md
└─ sample_measurement.csv
```

.git、bin、obj、TestResults、PDB、DB、Journal、キャッシュ、ソースコードはありません。

## 17. Project / Package差分

5 Project構成、TargetFramework、ProjectReference、Package、Package Version、DI構成に変更はありません。新NuGet・新Projectはありません。

## 18. 本番コード変更有無

Phase 8中、リリースを止めたテスト後片付け不具合への最小修正として次を変更しました。

| ファイル | 変更理由・影響 |
|---|---|
| src/ManufacturingDataApp.Infrastructure/Data/DatabaseConnectionFactory.vb | `pooling`任意引数（既定true）を追加。通常の本番接続は既定値で従来と同じくプール有効 |
| tests/.../TestSupport/Phase2Database.vb | 一時DBを必要に応じてプール無効で生成できるようにした |
| tests/.../Infrastructure/AtomicCsvImportTests.vb | 直後に一時DBを削除するFixtureだけをプール無効化 |
| tests/.../Infrastructure/Phase8TestIsolationTests.vb | 8回連続の一時DB生成・破棄を確認する回帰1件を追加 |

この修正はSQLiteの本番DB設定、Schema、Atomic、Journal、Recovery、Migration、CSV仕様、周期取込仕様を変更しません。テスト全354件の最終成功で回帰を確認しました。

## 19. 実ユーザーDB未使用確認

テストはGUID付き一時SQLite、一時Watch Root、一時Journalを使用しています。Program.Mainを試験目的で起動していません。配布EXE Smoke Testも隔離環境がないため未実施であり、実ユーザーDBを使用していません。

## 20. 既知制約

- 実SCADA／PLC出力環境：未実施。
- 実デスクトップDPI 100%／125%／150%：未確認。
- 1000小CSV×1行：Phase 7実測約82.9分。Phase 8で性能改善・安全チェック削除はしていません。
- NU1900：脆弱性監査未完了。
- INTEGRATION_TEST_MATRIXのMANUAL、PENDING、PASS / MANUAL、PASS / PENDINGはReleaseによってPASSへ変更していません。
- 配布EXEの隔離Smoke Test：未実施。
- 未署名EXEのためSmartScreen等の警告があり得ます。

## 21. 最終Release判定

**Ver1.10 Release成果物作成完了**。

Release Build成功、最終354 Pass／0 Fail／0 Skip、win-x64 self-contained single-file Publish成功、distは4ファイル、Publish EXEとdist EXEのhash一致、ZIP作成・再展開監査・ZIP hash取得、Project／Package不要変更なし、実ユーザーDB未使用を確認しました。

おすすめ：配布前にZIPを独立レビューし、隔離環境でSmoke TestとSCADA／DPI手動確認を実施してください。理由：自動検証・配布構成は確認済みですが、実装置の公開契約と実画面の品質はこの環境では検証できないためです。今すぐ行うこと：`artifacts/release/ManufacturingDataApp_Ver1.10_win-x64.zip`と本報告書を確認してください。

## Phase 8.1最終配布修正

### 変更内容と範囲

- `docs/USER_GUIDE.md`：冒頭をVer1.10利用者向けに変更。Phase 7途中状態の説明と、配布されないSCADA／UI検証文書へのリンクを削除。tmp→rename推奨、直接.csvへ書く場合の完成までの書込みハンドル保持、利用PCの100%／125%／150%表示倍率での表示切れ確認を、冒頭と末尾で自己完結させました。
- `docs/DISTRIBUTION_README.md`：既知事項の参照先を同梱の`USER_GUIDE.md`へ変更しました。
- `scripts/Prepare-Distribution.ps1`：UTF-8 BOMを追加しました。配布ロジックに変更はありません。
- 本報告書：Phase 8の履歴を残し、Phase 8.1の結果を追記しました。ソース版のSCADA／UI詳細検証文書は保持しています。

作業前後で`src`・`tests`のソース、Project、Publish Profile、およびSolution／Directory.Build.propsの計143ファイルのSHA-256一致を確認しました。本番コード・テスト・Project／Package／Reference・DB Schema・CSV仕様・UI仕様・設計に変更はありません。`pooling`任意引数と既定`True`も維持しています。Timeout延長、Retry追加、Skip、Performanceテスト除外、Assertion弱体化は行っていません。

### PowerShell互換性

修正前にWindows PowerShell 5.1で32行目の`Unexpected token '}' in expression or statement.`を再現し、PowerShell 7では成功しました。5.1.26100.9549の既定コードページは932、ファイル先頭は`70-61-72`（BOMなし）でした。同じ5.1のParserで、通常の`ParseFile`は1 error、UTF-8を明示した`ReadAllText`＋`ParseInput`は0 errorsでした。この差から、スクリプトのロジックではなくUTF-8 BOMなしの読み込みが原因と確認しました。

先頭にBOM（`EF-BB-BF`）だけを追加後、次の両コマンドはexit code 0で成功しました。前回レビュー用`ポートフォリオ用_業務データ管理アプリ_8.zip`内のスクリプトともバイト比較し、追加した先頭3バイト以外は完全一致しました。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Prepare-Distribution.ps1
pwsh -NoProfile -File .\scripts\Prepare-Distribution.ps1
```

検証環境はWindows PowerShell **5.1.26100.9549**／PowerShell **7.6.5**です。生成したdistは指定の4ファイルだけで、USER_GUIDEはソースからそのままコピーする方針を維持しました。

### Release Build・3回連続テスト

`dotnet build -c Release --no-restore`は0 errors／NU1900警告1件（Tests）で成功しました。監査未完了の扱いは継続します。

最終テストは次のコマンドをRun 1からRun 3まで順番に実行し、各TRXを保存しました。テスト実行中のZIP圧縮や別テストプロセスの起動は行っていません。

```powershell
dotnet test -c Release --no-build --no-restore --logger "trx;LogFileName=phase81-run1.trx" --results-directory tests/TestResults/Phase81
dotnet test -c Release --no-build --no-restore --logger "trx;LogFileName=phase81-run2.trx" --results-directory tests/TestResults/Phase81
dotnet test -c Release --no-build --no-restore --logger "trx;LogFileName=phase81-run3.trx" --results-directory tests/TestResults/Phase81
```

2026-09-28 JSTの結果（所要時間はdotnet test表示、1万行テストはTRXのduration）：

| Run | Pass | Fail | Skip | 全体の所要時間 | 1万行UI応答・終了待ちテスト |
|---|---:|---:|---:|---|---|
| 1 | 354 | 0 | 0 | 2分2秒 | Passed／4.085秒 |
| 2 | 354 | 0 | 0 | 1分52秒 | Passed／3.545秒 |
| 3 | 354 | 0 | 0 | 1分54秒 | Passed／4.444秒 |

TRXは`tests/TestResults/Phase81/phase81-run1.trx`、`phase81-run2.trx`、`phase81-run3.trx`です。レビュー用ソースZIPにも収録しています。

Phase 8の`phase8-diagnostic.trx`で`TenThousandRows_UiRespondsAndCloseWaitsForAtomic`の`UI test timed out`を確認しました。今回の3回では再発しませんでした。既存25秒Timeout・テスト並列設定・STA dispatcher・Assertionは変更していません。今回の成功だけで過去のTimeoutの原因を断定したり、あらゆる負荷での安定性を保証したりはしていません。

### dist・配布ZIP・内部リンク監査

既存Publish EXEを使用し、再Publishは行っていません。両PowerShellで配布Scriptを成功させ、distを再作成しました。

| distファイル | サイズ |
|---|---:|
| ManufacturingDataApp.exe | 164,352,875 bytes |
| README.md | 3,130 bytes |
| USER_GUIDE.md | 11,395 bytes |
| sample_measurement.csv | 229 bytes |

`artifacts/release/ManufacturingDataApp_Ver1.10_win-x64.zip`を再作成し、別一時フォルダへ展開しました。`ManufacturingDataApp/`内の上記4ファイルのみです。.git、bin、obj、TestResults、PDB、DB／WAL／SHM、.periodic-import、owner.lock、ソースコード、開発用Reportはありません。

展開後READMEの相対リンク2箇所はいずれも同梱`USER_GUIDE.md`に解決し、USER_GUIDEには外部ファイルへのMarkdownリンクがありません。参照形式・HTMLリンクもなく、配布されないSCADA／UI／Release報告書のファイル名やPhase 7途中状態の記述も残っていません。展開した全4ファイルのhashを、それぞれPublish EXE・ソース文書・sample CSVと照合しました。

| 対象 | SHA-256 |
|---|---|
| Publish EXE | CC735D1270DD4D31AED5F9FAD42945F065625721FF721E1D69EDFFBD033379B2 |
| dist EXE | CC735D1270DD4D31AED5F9FAD42945F065625721FF721E1D69EDFFBD033379B2 |
| 最終Release ZIP | 76B44EC85AA4F720D566CCCE7ABDE966458F521434B3677835AB19BFF06C4944 |

最終Release ZIPは**69,042,654 bytes**です。EXE hashはPhase 8と同じで、ZIP hashは文書更新により旧`C1FE10A279B605A27A2F419923E78D82388D0EF4EEE945BBA6EBA7E13705BD23`から変わりました。

### レビュー用成果物と最終判定

- 修正版ソースZIP：`artifacts/release/ManufacturingDataApp_Ver1.10_Phase8.1_source.zip`。ソース・設計文書・操作文書・Script・sample／テストデータ・今回の3 TRXを収録。bin／obj／.git／DB／旧配布物を除外し、Release ZIPは別途提供します。
- 更新Report：`docs/V1_10_PHASE8_RELEASE_REPORT.md`（本書）。
- 最終Release ZIP：`artifacts/release/ManufacturingDataApp_Ver1.10_win-x64.zip`。

実ユーザーDBを使用していません。既存テストは一時DBを使用し、配布EXE／Program.Mainの起動は行っていません。隔離Smoke Test、実SCADA、実デスクトップDPIの未確認状態、未署名、NU1900による監査未完了、既知の1000小CSV性能制約は継続します。

**Phase 8.1完了・Ver1.10配布物修正版作成完了・ChatGPT再レビュー待ち**。

おすすめ／今すぐ行うこと：上記ソースZIP・本報告書・Release ZIPをChatGPTの再レビューへ提出してください。理由：配布文書・PowerShell互換性・3回連続の回帰試験・配布ZIP監査が揃ったためです。次Phaseや追加機能開発は開始していません。
