# Ver1.10 SCADA実機確認手順

実SCADA試験：**未実施（環境なし）**。自動試験の装置公開方式相当試験と実機適合性は別です。本番設備へ接続・操作していません。

## 安全な準備

装置管理者の承認を得たオフライン試験環境、試験用Windowsアカウント、架空の設備・測定値、一時DB・ローカル通常Watch Rootを使用します。本番のPLC設定変更・制御出力・CSV供給停止を無断で行わないでください。停止できない本番設備で障害注入しないでください。

装置型式、SCADA版、出力周期、最大CSV行数、同時到着数、ローカル書込み先、文字コード・区切り・ヘッダー・列、実施者・日時を記録します。UNC／NAS／割当ネットワークドライブ／reparseは監視先として非対応です。

## 出力担当者と確定する契約

| 確認事項 | 記録する内容 |
|---|---|
| 生成開始 | 最初から.csvか、.tmp等か |
| Open | write handleの保持期間とFileShare指定。観測方法も記録 |
| 書込み中 | サイズ・mtimeが止まる期間にもhandleを保持するか |
| 完成 | 最終書込み後のClose時点 |
| 公開 | Close後の同一ローカルフォルダ内rename有無 |
| 追記 | 公開後に追記するか、断続open/closeするか |
| 障害時 | 部分ファイルを残すか、再送・同名再利用があるか |

推奨は .tmp書込み→Close→.csvへrename。断続open/closeで.csvを追記する場合は未完成取込の隙間があるため受入不可のまま保留し、安全な公開方式を担当者と確定します。mtime／サイズ安定を完成根拠にしないでください。

## 試験手順と期待結果

| ID | 手順 | 期待 | 実機結果 |
|---|---|---|---|
| S01 | .tmpへ書き、2周期以上保持 | Order／DB／移動対象にならない | 未実施 |
| S02 | S01をCloseして.csvへrename | 次周期以降1回だけ登録、success、Completed | 未実施 |
| S03 | 最古A.csvのwrite handle保持、B/Cを完成公開 | A/B/C全て未登録、errorなし、Running・WaitingForReadable | 未実施 |
| S04 | S03でサイズ・mtimeを2周期以上不変にする | 安定だけで完成扱いしない | 未実施 |
| S05 | S03のまま安全停止・再起動・手動開始 | 保存Order A→B→C、A待ち。自動Startなし | 未実施 |
| S06 | A完成・Close、mtimeをB/Cより新しくする | A→B→Cのまま各1回、success3・DB予定件数 | 未実施 |
| S07 | 実際の装置で公開後追記／断続Close有無を観測 | 未完成公開の隙間がないことを担当者が確認 | 未実施 |
| S08 | A正常B異常C正常、B2を新mtimeで明示確認 | B全件拒否、C保留。手動開始後B2→C | 未実施 |
| S09 | 実運用相当行数・出力頻度で開始、処理中停止 | 並行数1・件数一致・現在CSV完了後停止。時間・メモリ記録 | 未実施 |

自動根拠：Phase7PublicationTests.TmpWriterIsIgnoredUntilClosedAndRenamedAtNextTick、DirectCsvStableWhileOpenRemainsWaitingAcrossRestart（FileShare.None／Read）。本物のFileStreamと一時SQLiteを使っていますが装置の実装を保証するものではありません。

## 判定と保全

DB件数だけでなく元CSV、success／error、JournalのAttempt・Cursor、OperationLogを照合します。失敗時はCSV・DB・.periodic-importを整合した一式で保全します。管理記録を削除して再試行しません。詳細はUSER_GUIDEのRecovery手順を参照してください。

1000小CSV実測約82.9分を踏まえ、対象PCで処理時間と生成量を測定して許容遅延を合意します。独断で合否閾値を設定しません。装置公開契約、UI目視、性能、未実施障害試験は、Ver1.10の実運用適合性を判断するための手動確認項目として残ります。

記録欄：実施者／承認者／日時／環境／S-ID／操作時刻／Open・Close・rename時刻／期待件数／実件数／順序／状態／所要時間／PASS・FAIL／証跡／残課題。現在は全項目未実施です。
