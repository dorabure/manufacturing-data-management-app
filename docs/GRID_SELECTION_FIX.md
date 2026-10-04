# 選択行削除・詳細表示のId列例外 調査結果（2026-09-26）

## 原因と追跡結果

修正前の `MainForm.vb` 136行目（GridCellDoubleClick）および158行目（DeleteSelected）の `row.Cells("Id")` が原因。列名参照はNameを検索するが、非表示列はDataPropertyName="Id"だけでNameが空だった（指示書パターンB）。両箇所は従来のTryブロックの外側にあった。

修正前の列定義は以下。Name未設定の列のNameは空文字。ValueTypeは列定義で明示されておらず、バインド後はデータ型に従う。

| Name | DataPropertyName | HeaderText | Visible | バインド型 |
| --- | --- | --- | --- | --- |
| SelectForDelete | 空 | 選択 | True | Boolean |
| 空 | Id | 空 | False | Long |
| 空 | MeasuredAt | 取得日時 | True | DateTime |
| 空 | EquipmentId | 設備ID | True | String |
| 空 | EquipmentName | 設備名 | True | String |
| 空 | ItemName | 項目名 | True | String |
| 空 | Value | 測定値 | True | Double |
| 空 | Unit | 単位 | True | String |

DBの主キーは `MeasurementData.Id INTEGER PRIMARY KEY AUTOINCREMENT`。Repository.SearchはIdをSELECTし、Mapで `reader.GetInt64(0)` をDomainの `MeasurementData.Id As Long` に格納する。Serviceは同じ型の一覧を返し、MainFormはToListした結果をDataSourceに設定している。FindById / Update / Deleteも同じIdを使用する。途中でIdは欠落していない。

Id列自体は存在しており、UI修正で列が消えたケースではない。Gitにコミット履歴がないため、Name未設定が導入された時点は断定できない。

## 修正

- 非表示列のNameとDataPropertyNameをNameOf(MeasurementData.Id)、ValueTypeをLongに明示。
- 詳細・削除のId取得は共通のGetMeasurementIdで、DataBoundItemをMeasurementDataへTryCastして取得。Idセルの型変換に依存しない。
- 選択列名はSelectionColumnName定数へ集約。他の表示列をCells(name)で参照する処理はない。
- 削除前にEndEditで直前のチェックを確定。未選択・Nothing・DBNullは除外。不正な選択型、関連付けのない行、不正Idでは削除全体を中止してユーザー向けメッセージを表示する。
- ダブルクリックはヘッダー、行外、新規行、チェックボックス列を除外。Id取得も既存の例外表示の対象に含めた。
- 0件案内、削除確認、Service.Delete、成功後の再検索、詳細保存後の再検索を維持。
- DB Schema、Repository、Service、Domain、Program/DI、UI配置・列幅・フォームサイズは変更なし。

## 回帰テスト

実際のWinForms DataGridViewをSTAスレッド上で生成し、PresentationのId取得処理を直接テスト。テストプロジェクトにPresentation参照とUseWindowsFormsを追加し、InternalsVisibleToで内部メソッドを公開。NuGet追加・更新なし。

- SelectedRows_ReturnBoundIdsWithoutAnIdColumn: 0件・1件・2件の3ケース。
- Detail_ReturnsBoundIdForEachRowWithoutAnIdColumn: Id列なしで行ごとの対象Idを確認。
- Detail_IgnoresHeadersCheckboxAndNewRow: ヘッダー・選択列・新規行除外。
- Selection_IgnoresNullValuesAndRejectsInvalidSelection: Nothing / DBNull / 不正な型。
- InvalidBoundId_RejectsEntireSelectionAndDetail: 無効なIdを含む場合の拒否。
- UnboundRow_IsRejectedWithoutCellIdConversion: データ関連付けがない行の拒否。
- SearchIds_FindDetailsAndDeleteOneOfThreePersistedRecords: CSV3件取込、Idの一意性と詳細一致、1件削除後の2件残存と削除Idの取得不可。

既存のDeleteMultipleSearchAndLog_DeletesOnlySpecifiedRecordsおよびEditSearchAndLog_PersistsOnlyTargetChangeも成功。既存テストは削除・無効化・Skip・Assertion弱体化なし。

restore成功、Release build成功（エラー0）、Release testは71件中71成功・0失敗・0Skip。restore/buildにはNuGet脆弱性情報取得のNU1900警告が3件。既存win-x64 Publish ProfileでPublish成功。

## GUI検証範囲

今回Computer Useによる配布版の起動、条件クリア後の検索0件、同梱CSVの取込3件成功・0件失敗まで直接確認。既存DBの再作成・削除はしていない。

取込完了ダイアログ以降、操作基盤が `element ... is not available in cached app state`、再取得後も `window is not a usable app window` を返したため、直接操作を中断した。サンプル3件とCSV取込ログは残っている。

配布版での1件削除・複数削除・行ダブルクリック・詳細編集保存後の再検索はユーザー実機で再確認が必要。自動テストの成功はPublish版GUIの操作完了を意味しない。

dist内にあった5枚のスクリーンショットは削除せず、`docs/screenshots/preserved-from-dist-20260926/` へ退避した。

検証用に起動したプロセスを終了後、Prepare-Distribution.ps1は成功。distはManufacturingDataApp.exe、README.md、USER_GUIDE.md、sample_measurement.csvの4ファイルのみで、Publish成果物とdistのEXEのSHA-256が一致した。
