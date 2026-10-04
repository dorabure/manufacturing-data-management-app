# UIレイアウト確認表 Ver1.10 Phase 7

結論：コード確認済み、一部の自動Control確認済み。**実デスクトップ目視とDPI 100%／125%／150%は未確認（MANUAL）**。STAでFormやハンドルを生成して操作した事実を、実画面の撮影・表示品質のPASSとは扱いません。Program.Mainは起動していません。

## 確認範囲

全Formは動的構築でFont AutoScale。Dock、TableLayoutPanel／FlowLayoutPanel、最小サイズ、列ヘッダー設定をコード確認しています。

| Form | Client／Minimum（コード値） | 自動確認の根拠 | デスクトップ・各DPI |
|---|---|---|---|
| MainForm | 1180×680／1100×640 | Phase5MainFormTestsの状態別Enabledとイベント入口、Phase6MainFormTestsの終了、Phase7GridTests、Phase7EndToEndTestsの検索・クリア・開始 | MANUAL |
| CsvImportForm | 620×200／560×200 | CSV取込サービスの回帰あり。ダイアログ実操作は未確認 | MANUAL |
| CsvImportConfigForm | 860×560／760×500 | 設定・Mapping保存回帰あり。画面実操作は未確認 | MANUAL |
| MeasurementDetailForm | 540×390／500×360 | 編集・選択IDの回帰あり。画面実操作は未確認 | MANUAL |
| MasterForm | 920×640／780×560 | マスタサービス回帰あり。画面実操作は未確認 | MANUAL |
| ErrorLogForm | 900×600／780×480 | Phase7DateFilterTestsで日付チェックなしの実Control検索1件 | MANUAL |
| OperationLogForm | 900×600／720×480 | 同上。Phase5で新旧操作種別入力 | MANUAL |
| PeriodicImportRecoveryForm | 780×410／780×410 | Phase5MainFormTests.T70_RecoveryDialogStartsUnselectedAndCancelDoesNothing | MANUAL |

## 周期UIとハードニング

| 対象 | コード／自動確認 | 目視で残る確認 |
|---|---|---|
| Mode RadioButton／監視設定保存 | 状態別制御、再起動で自動Startなし | ラベル全文、相互間隔 |
| 監視Folder／参照 | 幅530、イベント入口防御 | 長いパス、日本語、右端 |
| 周期秒／Start・Stop | 整数1～604800、開始多重防止、停止待機 | キーボード入力・貼付・フォーカス時の見え方 |
| Status Label | AutoSize、最大幅1050、SafeText 512文字 | 長文折返しでGridを押し出さないか |
| 履歴ComboBox | 幅780、展開幅1000、100件有界・選択維持 | 高DPIで右端／展開領域の欠け |
| 修正版Button | 最小幅200、停止時明示確認 | 履歴と同一行での全文表示 |
| Grid | Header高さ30、上部操作領域とは別行 | 最小サイズで全列・下端が利用可能か |
| Gridデータ列 | Phase7GridTestsで6項目BeginEdit不可、モデル不変 | ダブルクリックから正式な詳細編集 |
| Grid選択列 | 編集可能、選択ID7取得 | チェックと複数削除の操作感 |
| FormClosing | Phase6/7で現在CSV待機、Close連打・Dispose後通知防御 | 実画面の応答・待機案内 |

周期Panelの行はWrapContents=Falseです。長い通知やDPI増加時の見切れリスクはコードだけでは否定できません。実測なしにレイアウト変更や目視PASSの判定は行っていません。

## 実デスクトップ確認手順

1. 本番とは別のWindows試験用アカウント／隔離環境で、架空データ専用DB・監視Rootを準備します。実ユーザーDBの上書き・コピー先としての流用は禁止します。
2. Windowsの表示倍率を100%、125%、150%の順に設定し、必要ならサインインし直してアプリを再起動します。OS・解像度・倍率を記録します。
3. 上表8画面を通常サイズ・最小サイズ・最大化で確認します。文字切れ、ボタン切れ、重なり、Grid Header、右端、下端を撮影します。
4. MainFormを手動／周期、Stopped／Starting／Running／Stopping、書込み待ち、CorrectionPendingの状態にします。長い状態文・履歴選択・修正版Buttonを確認します。
5. 手動取込→検索→条件クリア→詳細編集→再検索→複数削除→出力→CSV設定→マスタ→両ログを操作し、DB・出力CSVと照合します。
6. 監視中は禁止操作ができず、検索・ログ・出力は可能、停止後に復帰することを確認します。
7. ×による終了待ちを確認します。Cleanup失敗時に強制終了せず理由表示を記録します。

各画面×3倍率を個別に記録し、未実施は空欄ではなく「未実施」とします。記録欄：実施者／日時／PC／倍率／画面／手順／期待／実際／PASS・FAIL／画像名。実画像追加前のリンクは作成しません。
