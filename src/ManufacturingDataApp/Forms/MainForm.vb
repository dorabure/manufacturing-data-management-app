Imports System.Windows.Forms
Imports System.Drawing
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Entities

<Assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ManufacturingDataApp.Tests")>

Namespace Forms
    Public Partial Class MainForm
        Inherits Form

        Friend Const SelectionColumnName As String = "SelectForDelete"

        Private ReadOnly _measurementService As MeasurementDataService
        Private ReadOnly _csvImportService As CsvImportService
        Private ReadOnly _csvExportService As CsvExportService
        Private ReadOnly _csvConfigService As CsvConfigService
        Private ReadOnly _masterDataService As MasterDataService
        Private ReadOnly _logViewService As LogViewService
        Private ReadOnly _csvConfigComboBox As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 150}
        Private _currentResults As IReadOnlyList(Of MeasurementData) = Array.Empty(Of MeasurementData)()
        Private ReadOnly _equipmentIdTextBox As New TextBox()
        Private ReadOnly _itemNameTextBox As New TextBox()
        Private ReadOnly _fromPicker As New DateTimePicker With {.Format = DateTimePickerFormat.Short, .ShowCheckBox = True}
        Private ReadOnly _toPicker As New DateTimePicker With {.Format = DateTimePickerFormat.Short, .ShowCheckBox = True}
        Private ReadOnly _countLabel As New Label()
        Private ReadOnly _grid As New DataGridView()

        Public Sub New(measurementService As MeasurementDataService, csvImportService As CsvImportService, csvExportService As CsvExportService, csvConfigService As CsvConfigService, masterDataService As MasterDataService, logViewService As LogViewService, Optional periodic As Presentation.PeriodicImportComposition = Nothing)
            _measurementService = measurementService
            _csvImportService = csvImportService
            _csvExportService = csvExportService
            _csvConfigService = csvConfigService
            _masterDataService = masterDataService
            _logViewService = logViewService
            _periodic = periodic
            _settings = New Presentation.MonitoringSettingsController(csvConfigService)
            Text = "製造業向け 業務データ管理アプリ"
            AutoScaleMode = AutoScaleMode.Font
            ClientSize = New Size(1180, 680)
            MinimumSize = New Size(1100, 640)
            StartPosition = FormStartPosition.CenterScreen
            BuildUi()
            LoadCsvConfigs()
            InitializePeriodicEvents()
            InitializeClosing()
            Search()
        End Sub

        Private Sub BuildUi()
            Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .Padding = New Padding(8)}
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))

            Dim searchRow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .Margin = New Padding(0, 0, 0, 4)}
            _equipmentIdTextBox.Width = 105
            _itemNameTextBox.Width = 105
            searchRow.Controls.AddRange({CreateLabel("設備ID"), _equipmentIdTextBox, CreateLabel("項目名"), _itemNameTextBox, CreateLabel("取得日時"), _fromPicker, CreateLabel("～"), _toPicker})
            searchButton = CreateButton("検索", 70)
            AddHandler searchButton.Click, Sub(sender, e) Search()
            Dim clearButton = CreateButton("条件クリア", 90)
            AddHandler clearButton.Click, Sub(sender, e) ClearConditions()
            deleteButton = CreateButton("選択したデータを削除", 175)
            AddHandler deleteButton.Click, AddressOf DeleteSelected
            searchRow.Controls.AddRange({searchButton, clearButton, deleteButton})

            Dim commandRow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False}
            exportButton = CreateButton("CSV出力", 80)
            AddHandler exportButton.Click, AddressOf ExportCurrentResults
            importButton = CreateButton("CSV取込", 80)
            AddHandler importButton.Click, AddressOf OpenCsvImport
            configButton = CreateButton("CSV設定", 80)
            AddHandler configButton.Click, AddressOf OpenCsvConfig
            masterButton = CreateButton("マスタ管理", 95)
            AddHandler masterButton.Click, AddressOf OpenMaster
            errorLogButton = CreateButton("エラーログ", 90) : AddHandler errorLogButton.Click, AddressOf OpenErrorLog
            operationLogButton = CreateButton("操作ログ", 90) : AddHandler operationLogButton.Click, AddressOf OpenOperationLog
            _csvConfigComboBox.Width = 160
            _countLabel.AutoSize = True
            _countLabel.Margin = New Padding(8, 9, 0, 0)
            commandRow.Controls.AddRange({importButton, CreateLabel("CSV設定"), _csvConfigComboBox, configButton, exportButton, masterButton, errorLogButton, operationLogButton, _countLabel})

            Dim commands As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
            commands.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            commands.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            commands.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            commands.Controls.Add(searchRow, 0, 0)
            commands.Controls.Add(commandRow, 0, 1)
            AddPeriodicControls(commands)

            _grid.Dock = DockStyle.Fill
            _grid.AutoGenerateColumns = False
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            _grid.ColumnHeadersVisible = True
            _grid.ColumnHeadersHeight = 30
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing
            _grid.RowHeadersVisible = False
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            _grid.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = SelectionColumnName, .HeaderText = "選択", .Width = 45})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = NameOf(MeasurementData.Id), .DataPropertyName = NameOf(MeasurementData.Id), .ValueType = GetType(Long), .Visible = False})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "MeasuredAt", .HeaderText = "取得日時", .DefaultCellStyle = New DataGridViewCellStyle With {.Format = "yyyy/MM/dd HH:mm:ss"}, .Width = 160})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "EquipmentId", .HeaderText = "設備ID", .Width = 95})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "EquipmentName", .HeaderText = "設備名", .Width = 180})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "ItemName", .HeaderText = "項目名", .Width = 105})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "Value", .HeaderText = "測定値", .DefaultCellStyle = New DataGridViewCellStyle With {.Format = "N3"}, .Width = 90})
            _grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = "Unit", .HeaderText = "単位", .Width = 65})
            ' Edit measurements only through the validated detail form. Keep selection editable.
            For Each column As DataGridViewColumn In _grid.Columns
                column.ReadOnly = column.Name <> SelectionColumnName
            Next
            AddHandler _grid.CellDoubleClick, AddressOf GridCellDoubleClick
            root.Controls.Add(commands, 0, 0)
            root.Controls.Add(_grid, 0, 1)
            Controls.Add(root)
        End Sub

        Private Shared Function CreateLabel(text As String) As Label
            Return New Label With {.Text = text, .AutoSize = True, .Margin = New Padding(4, 9, 2, 0)}
        End Function

        Private Shared Function CreateButton(text As String, minimumWidth As Integer) As Button
            Return New Button With {.Text = text, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .MinimumSize = New Size(minimumWidth, 32), .Padding = New Padding(8, 2, 8, 2), .Margin = New Padding(3, 2, 3, 2)}
        End Function

        Private Sub Search()
            Try
                Dim fromDate As DateTime? = If(_fromPicker.Checked, CType(_fromPicker.Value.Date, DateTime?), Nothing)
                Dim toDate As DateTime? = If(_toPicker.Checked, CType(_toPicker.Value.Date.AddDays(1), DateTime?), Nothing)
                Dim results = _measurementService.Search(_equipmentIdTextBox.Text, _itemNameTextBox.Text, fromDate, toDate)
                _currentResults = results.ToList()
                _grid.DataSource = _currentResults
                _countLabel.Text = $"検索結果：{_currentResults.Count}件"
            Catch ex As Exception
                MessageBox.Show("データの取得に失敗しました。", "検索エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub ClearConditions()
            _equipmentIdTextBox.Clear()
            _itemNameTextBox.Clear()
            _fromPicker.Checked = False
            _toPicker.Checked = False
            Search()
        End Sub

        Private Sub GridCellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If Not CanMutate Then Return
            Try
                Dim id = GetDetailMeasurementId(_grid, e)
                If Not id.HasValue Then Return
                Dim measurement = _measurementService.FindById(id.Value)
                If measurement Is Nothing Then
                    MessageBox.Show("対象データが見つかりません。再検索してください。", "詳細表示", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Search()
                    Return
                End If
                Using detailForm As New MeasurementDetailForm(_measurementService, measurement)
                    If detailForm.ShowDialog(Me) = DialogResult.OK Then Search()
                End Using
            Catch ex As Exception
                MessageBox.Show("詳細データの取得に失敗しました。", "詳細表示エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub DeleteSelected(sender As Object, e As EventArgs)
            If Not CanMutate Then Return
            Dim ids As IReadOnlyList(Of Long)
            Try
                ids = GetSelectedMeasurementIds(_grid)
            Catch ex As Exception
                MessageBox.Show("選択データを確認できません。再検索して選び直してください。", "削除エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try
            If ids.Count = 0 Then
                MessageBox.Show("削除するデータを選択してください。", "削除", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            Dim confirmation = MessageBox.Show($"選択された{ids.Count}件を削除します。{Environment.NewLine}よろしいですか？", "削除確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If confirmation <> DialogResult.Yes Then Return
            Try
                _measurementService.Delete(ids)
                MessageBox.Show("データを削除しました。", "削除完了", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Search()
            Catch ex As Exception
                MessageBox.Show("データの削除に失敗しました。", "削除エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Shared Function GetMeasurementId(row As DataGridViewRow) As Long
            Dim item = TryCast(row.DataBoundItem, MeasurementData)
            If item Is Nothing OrElse item.Id <= 0 Then
                Throw New InvalidOperationException("行に有効な測定データが関連付けられていません。")
            End If
            Return item.Id
        End Function

        Friend Shared Function GetDetailMeasurementId(grid As DataGridView, e As DataGridViewCellEventArgs) As Long?
            If e.RowIndex < 0 OrElse e.RowIndex >= grid.Rows.Count OrElse e.ColumnIndex < 0 OrElse e.ColumnIndex >= grid.Columns.Count Then Return Nothing
            If grid.Rows(e.RowIndex).IsNewRow OrElse TypeOf grid.Columns(e.ColumnIndex) Is DataGridViewCheckBoxColumn Then Return Nothing
            Return GetMeasurementId(grid.Rows(e.RowIndex))
        End Function

        Friend Shared Function GetSelectedMeasurementIds(grid As DataGridView) As IReadOnlyList(Of Long)
            ' Commit the most recent checkbox click before reading selection values.
            If Not grid.EndEdit() Then Throw New InvalidOperationException("選択状態を確定できません。")
            Dim ids As New List(Of Long)()
            For Each row As DataGridViewRow In grid.Rows
                If row.IsNewRow Then Continue For
                Dim selected = row.Cells(SelectionColumnName).Value
                If selected Is Nothing OrElse selected Is DBNull.Value Then Continue For
                If Not TypeOf selected Is Boolean Then Throw New InvalidOperationException("選択状態が不正です。")
                If DirectCast(selected, Boolean) Then ids.Add(GetMeasurementId(row))
            Next
            Return ids
        End Function

        Private Sub ExportCurrentResults(sender As Object, e As EventArgs)
            If _currentResults.Count = 0 Then
                MessageBox.Show("出力するデータがありません。", "CSV出力", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            Dim config = TryCast(_csvConfigComboBox.SelectedItem, CsvImportConfig)
            If config Is Nothing Then
                MessageBox.Show("CSV設定を登録して選択してください。", "CSV出力", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            Using dialog As New SaveFileDialog With {.Filter = "CSV ファイル (*.csv)|*.csv", .DefaultExt = "csv", .AddExtension = True, .FileName = $"MeasurementData_{DateTime.Now:yyyyMMdd_HHmmss}.csv"}
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                Try
                    _csvExportService.Export(dialog.FileName, _currentResults, config, _csvConfigService.GetMappings(config.ConfigId))
                    MessageBox.Show("CSVファイルを出力しました。", "CSV出力", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Catch ex As Exception
                    MessageBox.Show("CSVファイルの出力に失敗しました。", "CSV出力エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End Using
        End Sub

        Private Sub OpenCsvConfig(sender As Object, e As EventArgs)
            If Not CanMutate OrElse Not ResolveDirtySettings() Then Return
            Using configForm As New CsvImportConfigForm(_csvConfigService)
                configForm.ShowDialog(Me)
            End Using
            LoadCsvConfigs()
        End Sub

        Private Sub OpenCsvImport(sender As Object, e As EventArgs)
            If Not CurrentPolicy.CanManualImport Then Return
            Using importForm As New CsvImportForm(_csvImportService, _csvConfigService)
                importForm.ShowDialog(Me)
            End Using
            Search()
        End Sub

        Private Sub OpenMaster(sender As Object, e As EventArgs)
            If Not CanMutate Then Return
            Using masterForm As New MasterForm(_masterDataService)
                masterForm.ShowDialog(Me)
            End Using
        End Sub
        Private Sub OpenErrorLog(sender As Object, e As EventArgs)
            Using form As New ErrorLogForm(_logViewService)
                form.ShowDialog(Me)
            End Using
        End Sub
        Private Sub OpenOperationLog(sender As Object, e As EventArgs)
            Using form As New OperationLogForm(_logViewService)
                form.ShowDialog(Me)
            End Using
        End Sub

        Private Sub LoadCsvConfigs()
            _loadingSettings = True
            Try
            Dim selectedId As Integer? = Nothing
            Dim selected = TryCast(_csvConfigComboBox.SelectedItem, CsvImportConfig)
            If selected IsNot Nothing Then selectedId = selected.ConfigId
            Dim configs = _csvConfigService.GetAll().ToList()
            _csvConfigComboBox.DataSource = configs
            _csvConfigComboBox.DisplayMember = "ConfigName"
            If selectedId.HasValue Then
                Dim index = configs.FindIndex(Function(config) config.ConfigId = selectedId.Value)
                If index >= 0 Then _csvConfigComboBox.SelectedIndex = index
            End If
            Dim chosen = TryCast(_csvConfigComboBox.SelectedItem, CsvImportConfig)
            _settings.Load(If(chosen Is Nothing, 0, chosen.ConfigId))
            DisplaySettings()
            Finally
                _loadingSettings = False
            End Try
            RefreshRecoveryContext()
            RefreshPeriodicUi()
        End Sub
    End Class
End Namespace
