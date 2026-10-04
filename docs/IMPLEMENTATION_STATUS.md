# 実装ステータス Ver1.10

現在：**Ver1.10 Release Freeze完了・追加開発なし・ユーザー最終確認待ち**。実SCADA・DPI等の手動確認項目は未実施です。

| Phase | 範囲 | 状況 |
|---|---|---|
| 0 | 要件・基本設計、元の時系列順序の厳格維持 | 確定済み |
| 1 | 固定周期Scheduler、状態管理、二重実行防止、安全停止 | 実装済み |
| 2 | Config監視3項目、Backup・Migration、新規DB・既存互換 | 実装済み |
| 3 | Atomic入口、ファイル単位All-or-Nothing、例外分類 | 実装済み |
| 4 | Order・Attempt・Journal、File Lease、移動・Recovery・Correction Binding | 実装済み |
| 5 | MainForm周期UI、設定保存、通知・履歴、明示修正版確認 | 実装済み |
| 6 | FormClosingとStopAsync、終了連打・Cleanup失敗防御 | 実装済み |
| 7 | 横断E2E、回帰、公開方式相当、性能、Grid保護、文書更新 | 自動検証完了・実機確認待ち |
| 8 | Publish、self-contained、dist・配布ZIP更新、Release監査 | 完了。win-x64 self-contained single-file、dist 4ファイル、ZIP再展開監査 |
| 8.1 | 配布文書整合、PowerShell 5.1互換、3回連続Release Test | 完了。354 Pass／0 Fail／0 Skip × 3回 |
| Final Audit | 文書整合、最終Build／Test、hash・ZIP・リンク監査 | 完了。Ver1.10 Release Freeze |

Ver1.00の手動取込・検索・編集・削除・出力・設定・マスタ・ログは維持。Phase 7ではMainFormの非チェック列ReadOnlyと、MainForm／両ログ画面の日付条件未指定のNullable推論不具合を最小修正しました。業務仕様・DB設計・5 Project・NuGet・手動DIは変更していません。

## 検証

Phase 7の353件に、Phase 8で一時SQLiteの接続プール後片付けを検証する回帰1件を追加し、合計354件が最終Release Testで成功しました。既存330件のテストコード・Assertionは維持しています。詳細は[Phase 8報告書](V1_10_PHASE8_RELEASE_REPORT.md)を参照してください。

Phase 8.1で3回連続、Final Auditでさらに1回、354 Pass／0 Fail／0 Skipを確認しました。本番コード・テストコード・Project／Packageは変更していません。最終判定と成果物情報は[Final Audit](V1_10_FINAL_AUDIT.md)を参照してください。

実SCADA未実施（環境なし）。実デスクトップ・DPI 100/125/150%は未確認。自動Control確認とは区別します。NU1900が残るため脆弱性監査未完了。1000小CSVは実測約82.9分で、必要な処理量に対する適性評価が残ります。

## 既知事項と次の作業

実SCADA未実施、実デスクトップ・DPI 100/125/150%未確認、隔離環境での配布EXE Smoke Test未実施、NU1900による脆弱性監査未完了、1000小CSV約82.9分の既知制約、INTEGRATION_TEST_MATRIXのMANUAL／PENDINGは残ります。ユーザーレビュー後、[SCADA実機確認](V1_10_SCADA_MANUAL_VERIFICATION.md)、[UI確認](UI_LAYOUT_CHECK.md)、対象PCでの性能目標合意を実施してください。

Phase 8ではdistを新Publishから再構築し、ZIPへ.git、bin、obj、テスト一時データ、ローカルDB、開発キャッシュを含めていません。
