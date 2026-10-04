# 製造業向け 業務データ管理アプリ Ver1.10

## アプリ概要

PLC／SCADAから出力されるCSV形式の測定データを、検証してローカルSQLite DBへ保存するWindowsアプリです。検索・詳細編集・削除・CSV出力、エラーと操作履歴の確認に加え、Ver1.10ではフォルダ周期監視を利用できます。付属データはすべて架空のデモ用です。

## 動作環境

- Windows 10／11 64bit
- .NET Runtimeの別途インストールは不要です（win-x64 self-contained single-file版）。
- 監視先はローカル通常フォルダだけです。UNC／NAS／ネットワークドライブ／junction等には対応していません。

## 起動方法

1. ZIPを展開します。
2. 展開した `ManufacturingDataApp` フォルダ内の `ManufacturingDataApp.exe` を実行します。
3. 初回起動時は、デモ用マスタとDBが自動作成されます。

データ保存先：`%LocalAppData%\ManufacturingDataApp\ManufacturingDataApp.db`

## まず試す：手動CSV取込

1. 「CSV設定」で初期設定 `SCADA_A` を確認します。
2. 「CSV取込」で同梱の `sample_measurement.csv` を選びます。
3. 取込後にメイン画面で「検索」を押し、3件を確認します。
4. 「操作ログ」で `CSV取込` の履歴を確認します。

同じCSVを再度取り込むと重複検証の対象です。これは同一の設備・項目・取得日時の二重登録を防ぐ仕様です。

## フォルダ周期監視

CSV設定を選び「フォルダ周期監視」へ切り替え、ローカル監視Folderと1～604800秒の周期を保存してから開始します。最初の周期は準備後に実行され、以後は固定周期です。停止やアプリ終了時は、現在のCSVを安全に完了してから終了します。

- 正常CSVは `success`、行検証で拒否されたCSVは `error` へ移動します。
- `.periodic-import` はOrder・Journalの管理領域です。削除・編集しないでください。
- 先頭CSVが書込み中なら後続も処理しません。
- 推奨公開方式は「.tmpへ書込 → Close → .csvへrename」です。
- CorrectionPendingでは修正版を明示確認し、確認後もユーザーが改めて開始します。
- RecoveryRequired／MovePendingでは推測で再取込せず、CSV・DB・管理領域を一式保全してください。

詳細な操作・保全手順は[USER_GUIDE.md](USER_GUIDE.md)を参照してください。

## セキュリティ警告

本EXEは個人制作の未署名ファイルです。Windows SmartScreen等の警告が表示される場合があります。配布元とファイルを確認したうえで利用者自身の判断で実行してください。セキュリティ機能を無効化する必要はありません。

## 既知事項

実SCADA、実デスクトップDPI（100%／125%／150%）は未確認です。1000個の小CSVは試験環境で約82.9分を要しました。詳しい操作・保全手順は同梱の[USER_GUIDE.md](USER_GUIDE.md)を参照してください。
