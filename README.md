# 製造業向け 業務データ管理アプリ Ver1.10

**Ver1.10 Release成果物を作成済み**です。最終Releaseテストは354件すべて成功（Fail 0／Skip 0）。実SCADAと実デスクトップの表示倍率別確認は未実施のまま、手動確認項目として残しています。

PLC / SCADA等から出力されるCSV形式の測定データを取り込み、検証、SQLite保存、検索・編集・削除・CSV出力・ログ確認まで行うWindowsデスクトップ業務アプリです。すべて架空の設備・測定データを使用しています。

## 想定する業務課題

- CSVを表計算ソフトで手作業管理しており、形式不正や重複の確認に時間がかかる。
- 過去の測定データや取込エラー、操作履歴を追跡しづらい。

## 主な機能

- CSV取込と、必須・型・重複・範囲の検証
- 正常行のみのSQLite登録、検索、詳細表示、編集、複数削除
- 現在の検索結果のCSV出力、CSV仕様設定、設備・測定項目マスタ管理
- ErrorLogによる異常確認、OperationLogによる取込・出力・編集・削除・マスタ操作の履歴確認
- ローカルフォルダの固定周期自動取込（1秒～7日）、二重実行防止、書込み中CSVと後続の保留
- 周期取込のAll-or-Nothing、永続Journalによる処理記録、再起動時のRecovery
- 修正版の明示対応付けと元の順序での処理、停止・終了時の安全完了待機

## データチェック

手動取込は異常行を除いた正常行を登録します。周期取込は1行でも異常があればファイル全体を登録せず後続を停止します。CSV内重複、DB既存重複、MeasurementItemMasterのMin/Max範囲を確認します。手動のFailureCountは異常行数、周期全件拒否時は全行数で、不正行数と正常保留行数を別に保持します。

## CSV設定

文字コード、区切り文字、ヘッダー有無、標準6項目（取得日時・設備ID・設備名・項目名・測定値・単位）の列番号を設定できます。列順が異なるCSVにも対応します。

## アーキテクチャ

レイヤー分離を意識し、Presentation → Application → Domain、およびInfrastructure → Application / Domainの依存方向を維持しています。Program.vbがComposition Rootとして手動DIを行います。

| Project | 役割 |
|---|---|
| ManufacturingDataApp | WinForms画面 |
| ManufacturingDataApp.Application | Service、Interface、Validation |
| ManufacturingDataApp.Domain | Entity、定数、例外 |
| ManufacturingDataApp.Infrastructure | SQLite、CSV、Repository、ログ |
| ManufacturingDataApp.Tests | xUnitテスト |

## 技術スタック

| 技術 | 用途 |
|---|---|
| VB.NET / .NET 8 | アプリケーション |
| Windows Forms | デスクトップUI |
| SQLite / Microsoft.Data.Sqlite | ローカルDB |
| CsvHelper | CSV読込・出力 |
| xUnit | 自動テスト |

## データベース

EquipmentMaster（設備）、MeasurementItemMaster（項目と範囲）、MeasurementData（測定値）、CsvImportConfig / CsvColumnMapping（CSV仕様）、ErrorLog（異常）、OperationLog（処理履歴）を管理します。詳細は[DB設計](docs/design/製造業向け_業務データ管理アプリ_DB設計_ER図_テーブル定義_SQL_v1.1.xlsx)を参照してください。

本番DBは`%LocalAppData%\ManufacturingDataApp\ManufacturingDataApp.db`に保存されます。

## テスト

Domain、Validation、Repository、CSV取込・出力、検索、編集、削除、マスタ、ログ、結合フローを自動テストしています。BusinessFlowIntegrationTestsは一時SQLite DBと実CSVを使用し、CSV取込→DB→検索→ログ等を確認します。対応表は[結合テスト対応表](docs/INTEGRATION_TEST_MATRIX.md)です。

## 起動方法

前提：Windows 10 / 11、.NET 8 SDK。

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/ManufacturingDataApp/ManufacturingDataApp.vbproj
```

基本操作は[操作説明](docs/USER_GUIDE.md)、要件・画面設計は[設計資料](docs/design/)を参照してください。

## 配布版について

Ver1.10のwin-x64 self-contained single-file配布物を作成しました。`.NET Runtime`の別途インストールは不要です。配布ZIPは`artifacts/release/ManufacturingDataApp_Ver1.10_win-x64.zip`です。ソースZIPには含めず、別成果物として提供します。実SCADA・DPI・性能上の既知事項は下記制約を確認してください。

## AIを活用した開発プロセス

要件整理、設計案検討、実装指示書作成、コード生成支援、テストケース検討、レビュー、不具合修正をAIと反復して進めました。最終的な仕様判断と修正判断は人間側で行っています。

## このポートフォリオで示したいこと

- 製造業データを想定したCSV / SQLite連携、Validationとログ設計
- Repository / Serviceによる責務分離とトランザクションを意識したDB処理
- xUnitと実SQLite DBを用いた自動・結合テスト

## 画面イメージ

Ver1.10最新の実デスクトップスクリーンショットは未取得です。[docs/screenshots](docs/screenshots/)には過去画像を保管していますが、Ver1.10最新画面の証跡としては扱っていません。新しい画像への参照は、実際に撮影・確認してから追加します。

## 制約・今後の拡張候補

監視先はローカル通常フォルダのみで、UNC／NAS／ネットワークドライブ／junctionは非対応です。DB結果不明時は推測で再登録しません。`.periodic-import`を削除・編集しないでください。

1000 CSVの実測は約82.9分で、多数の小ファイル処理には性能上の課題があります。安全性を維持して対象PC・出力頻度で運用適性を判断する必要があります。サーバーDB化、ページング、外部システム連携、可視化、分析支援は将来の拡張候補です。

検証範囲・測定値は[Phase 7報告書](docs/V1_10_PHASE7_IMPLEMENTATION_REPORT.md)、Release内容は[Phase 8報告書](docs/V1_10_PHASE8_RELEASE_REPORT.md)、進捗は[実装状況](docs/IMPLEMENTATION_STATUS.md)、装置側の確認は[SCADA実機確認手順](docs/V1_10_SCADA_MANUAL_VERIFICATION.md)を参照してください。
