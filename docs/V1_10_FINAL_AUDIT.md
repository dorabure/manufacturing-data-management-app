# Ver1.10 Final Audit

## 1. 最終状態

監査日：2026-09-28 JST。**Ver1.10 Final Audit完了・Ver1.10 Release Freeze完了・追加開発なし・ユーザー最終確認待ち**。実運用適合性の手動確認は残ります。

## 2. 最終ソース変更範囲

- `docs/V1_10_SCADA_MANUAL_VERIFICATION.md`：Phase 8移行前の表現を、実運用適合性判断のために手動確認が残る表現へ変更。
- `docs/IMPLEMENTATION_STATUS.md`：Phase 8.1、Final Audit、Release Freezeと未実施項目を反映。NU1900の件数を固定せず監査未完了を明記。
- `README.md`：ソースZIPに同梱しない配布ZIPへのリンクをパス表記へ変更。保管済み過去画像と未取得のVer1.10最新画像を区別。
- `docs/V1_10_FINAL_AUDIT.md`：本報告書を追加。

配布README、USER_GUIDE、配布Script、設計・業務仕様は変更していません。

## 3. 本番コード変更なし確認

開始時、承認済み`ManufacturingDataApp_Ver1.10_Phase8.1_source.zip`の全180ファイルを現在のファイルとSHA-256で比較し、すべて一致しました。このZIPのSHA-256は`9F64B7C43986915EDF728364F75070C22D5933A5BFABDE65247791FC74D25EB1`です。変更後も同じ基準と比較し、既存ファイル差分は2節のソース専用文書3ファイルだけでした。本報告書とFinal AuditのTRXが新規ファイルです。

本番コード、DB Schema、Migration、Atomic、Journal、Recovery、Scheduler、FormClosing、CSV仕様を変更していません。既定`pooling=True`を維持しています。

## 4. テストコード変更なし確認

既存テストコードの変更なし。静的属性集計はFact 163、InlineData 191、合計354、Skip指定0。Timeout、Retry、並列設定、Assertionは変更していません。

## 5. Release Build

`dotnet build -c Release --no-restore`：成功、0 errors、NU1900警告1件（Tests）。Package更新・警告抑制なし。Build出力でPublish EXE／dist EXEを置換していません。

## 6. Release Test

```powershell
dotnet test -c Release --no-build --no-restore --logger "trx;LogFileName=final-audit.trx" --results-directory tests/TestResults/FinalAudit
```

結果：**354 Pass／0 Fail／0 Skip**。2026-09-28 14:49:47～14:51:51 JST、コンソール表示約2分。TRXのCountersとテスト結果を確認しました。`TenThousandRows_UiRespondsAndCloseWaitsForAtomic`はPassed／**3.433秒**、Timeout再発なし。最終ソースZIPへ`tests/TestResults/FinalAudit/final-audit.trx`を収録しています。

## 7. Phase 8.1 3回連続試験の証跡

`tests/TestResults/Phase81/phase81-run1.trx`、`phase81-run2.trx`、`phase81-run3.trx`を保持し、最終ソースZIPにも収録します。

| Run | Pass | Fail | Skip | 1万行UI応答・終了待ちテスト |
|---|---:|---:|---:|---|
| 1 | 354 | 0 | 0 | Passed／4.085秒 |
| 2 | 354 | 0 | 0 | Passed／3.545秒 |
| 3 | 354 | 0 | 0 | Passed／4.444秒 |

## 8. Publish EXE

`artifacts/publish/win-x64/ManufacturingDataApp.exe`：164,352,875 bytes。

SHA-256：`CC735D1270DD4D31AED5F9FAD42945F065625721FF721E1D69EDFFBD033379B2`。

Phase 8.1基準値を維持。再Publishせず、dist EXEも同じ値です。

## 9. Release ZIP

`artifacts/release/ManufacturingDataApp_Ver1.10_win-x64.zip`：69,042,654 bytes。

SHA-256：`76B44EC85AA4F720D566CCCE7ABDE966458F521434B3677835AB19BFF06C4944`。

Phase 8.1基準値を維持。再生成していません。

## 10. Release ZIP内容

`ManufacturingDataApp/`内にEXE、README.md、USER_GUIDE.md、sample_measurement.csvの4ファイルのみ。配布Markdownの相対リンクはREADMEからUSER_GUIDEへの2箇所で、両方実在します。配布されない検証文書へのリンクはありません。

## 11. 最終source ZIP

ファイル：`artifacts/release/ManufacturingDataApp_Ver1.10_source.zip`。

サイズ・SHA-256：ZIP確定後に提供する別添の本報告書確定版に記載します。

**別添確定版への追記（ZIP作成・再展開監査後）**：

- 最終source ZIPサイズ：**3,930,689 bytes**。
- 最終source ZIP SHA-256：`031C116BFF706EAE16DA0D0640547E1EC6E22FDBB104839D7A61A04C136C2EE6`。
- 再展開監査：**PASS**。全182ファイルのSHA-256が作成元と一致し、必要な全フォルダ・Solutionを確認。禁止された.git／bin／obj／DB／管理領域／旧Publish／dist／ZIP類の混入なし。
- 展開後Markdown監査：**20文書・19相対リンクすべて正常**。外部HTTP(S)参照は存在検査の対象外。
- ZIP内報告書との差分はこの確定情報の追記のみ。最終ZIPの再生成は行っていません。

本報告書自身を含むZIPにそのZIP自身のhashを埋め込むとhashが変化するため、ZIP内の報告書はこの説明を保持します。ZIP作成・再展開監査後、作業フォルダの同名報告書にZIPのサイズ・SHA-256を追記し、別添の確定版として提供します。その追記だけはZIP内版との差分になります。ZIP自身のhashを埋めるための再圧縮は行いません。

Phase 8.1ソースZIPは履歴として保持します。Release ZIPは別成果物で、最終ソースZIPには同梱しません。

## 12. source ZIP再展開監査

必要なREADME、docs、src、tests、scripts、data、Solution、Directory.Build.props、.gitignore、AGENTS.md、CLAUDE.mdおよびPhase 8.1の3 TRXとFinal AuditのTRXを収録。構成は`ManufacturingDataApp-source/`配下です。

別一時フォルダへ展開して全収録ファイルのhashを作成元と照合し、.git、bin、obj、DB／WAL／SHM、.periodic-import、owner.lock、開発キャッシュ、旧Publish、dist、Release ZIP、ソースZIP自身の混入を検査することをFreeze条件としました。ZIP確定後の監査結果は11節の別添確定版に追記します。

## 13. Markdownリンク監査

READMEとdocs配下のMarkdownの相対ファイル／ディレクトリリンクを検査しました。外部HTTP(S)リンクと見出しアンカーの内容は今回の検査対象外です。最終ソースZIP再展開先でも同じ検査を行い、結果を11節の別添確定版に追記します。配布ZIPを参照するREADMEの記述は、別成果物を示すパス表記にしています。

## 14. PowerShell互換

Phase 8.1でWindows PowerShell 5.1.26100.9549／PowerShell 7.6.5の配布Script実行成功を確認済みです。BOM `EF BB BF`追加のみで、配布ロジックに変更なし。今回もScriptはPhase 8.1ソースZIPとバイト一致し、dist再生成は不要のため再実行していません。

## 15. Package / Project監査

5 Project構成、PackageReference／Version、ProjectReference、TargetFramework、Solution、Directory.Build.props、Publish Profileに変更なし。win-x64、SelfContained=true、PublishSingleFile=true、PublishTrimmed=false、IncludeNativeLibrariesForSelfExtract=trueを維持しています。

## 16. 実ユーザーDB未使用確認

既存テストの一時SQLite DB／一時Watch Rootを使用。Program.Mainや配布EXEを起動していません。実ユーザーのDBへ接続・変更を行っていません。

## 17. 未実施手動確認

- 実SCADA／PLC：未実施。
- 実デスクトップDPI 100%／125%／150%：未確認。
- 隔離環境での配布EXE Smoke Test：未実施。
- INTEGRATION_TEST_MATRIXのPENDING／MANUAL：維持。対応表自体を変更していません。
- Ver1.10最新スクリーンショット：未取得。保管済み過去画像を最新画面の証跡として使用していません。

## 18. 既知制約

- 1000小CSV：Phase 7で約82.9分。対象PC・CSV出力頻度で運用適性の評価が必要。
- NU1900：脆弱性監査未完了。Phase 8の既知脆弱Packageなしという結果を完全監査とは扱いません。
- EXE未署名。SmartScreen等の警告が出る場合があります。

## 19. Release Freeze判定

**Ver1.10 Release Freeze完了**。本番コード・テスト・Project／Package変更なし、354 Pass／0 Fail／0 Skip、EXE／Release ZIP基準hash維持、4ファイル構成・配布リンク正常、ソース文書整合、未実施項目の維持を確認しました。最終ソースZIPの再展開監査結果とhashは別添確定版の11節を参照してください。

おすすめ／今すぐ行うこと：本報告書確定版・最終ソースZIP・Release ZIPを最終確認してください。理由：自動検証と配布内容を固定し、実機未確認事項も明示したためです。新機能・性能改善・Ver1.20・別Phaseは開始しません。
