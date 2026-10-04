Imports System.Windows.Forms
Imports System.Drawing
Imports ManufacturingDataApp.Application.Services
Namespace Forms
    Public Class OperationLogForm
        Inherits Form
        Private ReadOnly _service As LogViewService
        Private ReadOnly _grid As New DataGridView()
        Private ReadOnly _type As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDown}
        Private ReadOnly _from As New DateTimePicker With {.Format = DateTimePickerFormat.Short, .ShowCheckBox = True}
        Private ReadOnly _to As New DateTimePicker With {.Format = DateTimePickerFormat.Short, .ShowCheckBox = True}
        Public Sub New(service As LogViewService)
            _service = service : Text = "操作ログ"
            AutoScaleMode = AutoScaleMode.Font
            ClientSize = New Size(900, 600)
            MinimumSize = New Size(720, 480)
            StartPosition = FormStartPosition.CenterParent
            _type.Items.AddRange({"", "CSV取込", "CSV出力", "編集", "削除", "設備マスタ登録", "設備マスタ更新", "設備マスタ削除", "測定項目マスタ登録", "測定項目マスタ更新", "測定項目マスタ削除"}) : _type.SelectedIndex = 0
            _type.Items.AddRange({"周期CSV取込", "周期CSV取込失敗", "周期CSV取込結果不明", "周期監視開始", "周期監視停止", "周期監視異常停止"})
            Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Padding = New Padding(8)}
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100)) : root.RowStyles.Add(New RowStyle(SizeType.AutoSize)) : root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            Dim conditions As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 2, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Margin = New Padding(0, 0, 0, 4)}
            conditions.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            conditions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            conditions.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            conditions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            conditions.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            conditions.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            conditions.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            _from.MinimumSize = New Size(150, 0) : _to.MinimumSize = New Size(150, 0)
            _type.MinimumSize = New Size(180, 0)
            Dim b As New Button With {.Text = "検索", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)} : AddHandler b.Click, Sub(s, e) Search()
            AddCondition(conditions, 0, 0, "From", _from)
            AddCondition(conditions, 0, 2, "To", _to)
            AddCondition(conditions, 1, 0, "操作種別", _type)
            conditions.SetColumnSpan(_type, 3)
            conditions.Controls.Add(b, 4, 1)
            _grid.Dock = DockStyle.Fill : _grid.ReadOnly = True : _grid.AllowUserToAddRows = False : _grid.AllowUserToDeleteRows = False : _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect : _grid.AutoGenerateColumns = False : _grid.ColumnHeadersVisible = True : _grid.ColumnHeadersHeight = 30 : _grid.RowHeadersVisible = False
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "ExecutedAt", .HeaderText = "実行日時", .Width = 140}) : _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "OperationType", .HeaderText = "操作種別", .Width = 150}) : _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "ElapsedMs", .HeaderText = "処理時間(ms)", .Width = 90}) : _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "TotalCount", .HeaderText = "総件数", .Width = 70}) : _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "SuccessCount", .HeaderText = "成功件数", .Width = 70}) : _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "FailureCount", .HeaderText = "失敗件数", .Width = 70})
            root.Controls.Add(conditions, 0, 0) : root.Controls.Add(_grid, 0, 1) : Controls.Add(root) : Search()
        End Sub

        Private Shared Sub AddCondition(layout As TableLayoutPanel, row As Integer, column As Integer, labelText As String, control As Control)
            layout.Controls.Add(New Label With {.Text = labelText, .AutoSize = False, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(3)}, column, row)
            control.Dock = DockStyle.Fill
            control.Margin = New Padding(3)
            layout.Controls.Add(control, column + 1, row)
        End Sub
        Private Sub Search()
            Try
                _grid.DataSource = _service.SearchOperations(If(_from.Checked, CType(_from.Value.Date, DateTime?), Nothing), If(_to.Checked, CType(_to.Value.Date.AddDays(1), DateTime?), Nothing), CStr(_type.Text)).ToList()
            Catch : MessageBox.Show("操作ログの取得に失敗しました。") : End Try
        End Sub
    End Class
End Namespace
