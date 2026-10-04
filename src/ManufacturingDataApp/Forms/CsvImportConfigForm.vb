Imports System.Windows.Forms
Imports System.Drawing
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Namespace Forms
    Public Class CsvImportConfigForm
        Inherits Form
        Private ReadOnly _service As CsvConfigService
        Private ReadOnly _configList As New ListBox With {.Dock = DockStyle.Fill}
        Private ReadOnly _nameTextBox As New TextBox()
        Private ReadOnly _encodingComboBox As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
        Private ReadOnly _delimiterComboBox As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
        Private ReadOnly _hasHeaderCheckBox As New CheckBox With {.Text = "ヘッダーあり", .Checked = True}
        Private ReadOnly _mappingGrid As New DataGridView()
        Private _currentConfigId As Integer

        Public Sub New(service As CsvConfigService)
            _service = service
            Text = "CSV設定"
            AutoScaleMode = AutoScaleMode.Font
            ClientSize = New Size(860, 560)
            MinimumSize = New Size(760, 500)
            StartPosition = FormStartPosition.CenterParent
            BuildUi()
            LoadConfigs()
            NewConfig(Nothing, EventArgs.Empty)
        End Sub

        Private Sub BuildUi()
            _encodingComboBox.Items.AddRange({"UTF-8", "SHIFT-JIS", "UTF-16"})
            _delimiterComboBox.Items.AddRange({"カンマ (,)", "セミコロン (;)", "タブ"})
            Dim mainLayout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(8)}
            mainLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 180))
            mainLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            mainLayout.Controls.Add(_configList, 0, 0)
            AddHandler _configList.SelectedIndexChanged, AddressOf SelectConfig
            Dim detail As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(10), .ColumnCount = 2, .RowCount = 6}
            detail.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 100))
            detail.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            For row = 0 To 3
                detail.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            Next
            detail.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            detail.RowStyles.Add(New RowStyle(SizeType.Absolute, 42))
            AddField(detail, 0, "設定名", _nameTextBox)
            AddField(detail, 1, "文字コード", _encodingComboBox)
            AddField(detail, 2, "区切り文字", _delimiterComboBox)
            AddField(detail, 3, "ヘッダー", _hasHeaderCheckBox)
            _mappingGrid.Dock = DockStyle.Fill
            _mappingGrid.AllowUserToAddRows = False
            _mappingGrid.RowHeadersVisible = False
            _mappingGrid.ColumnHeadersVisible = True
            _mappingGrid.ColumnHeadersHeight = 30
            _mappingGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "FieldName", .HeaderText = "項目", .ReadOnly = True, .Width = 180})
            _mappingGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = "ColumnIndex", .HeaderText = "CSV列番号"})
            detail.Controls.Add(_mappingGrid, 0, 4)
            detail.SetColumnSpan(_mappingGrid, 2)
            Dim newButton As New Button With {.Text = "新規", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler newButton.Click, AddressOf NewConfig
            Dim saveButton As New Button With {.Text = "保存", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler saveButton.Click, AddressOf SaveConfig
            Dim deleteButton As New Button With {.Text = "削除", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler deleteButton.Click, AddressOf DeleteConfig
            Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .WrapContents = False}
            buttons.Controls.AddRange({newButton, saveButton, deleteButton})
            detail.Controls.Add(buttons, 0, 5)
            detail.SetColumnSpan(buttons, 2)
            mainLayout.Controls.Add(detail, 1, 0)
            Controls.Add(mainLayout)
        End Sub

        Private Shared Sub AddField(layout As TableLayoutPanel, row As Integer, labelText As String, control As Control)
            control.Dock = DockStyle.Fill
            layout.Controls.Add(New Label With {.Text = labelText, .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, row)
            layout.Controls.Add(control, 1, row)
        End Sub

        Private Sub LoadConfigs()
            _configList.DataSource = _service.GetAll().ToList()
            _configList.DisplayMember = "ConfigName"
        End Sub

        Private Sub NewConfig(sender As Object, e As EventArgs)
            _currentConfigId = 0
            _nameTextBox.Clear()
            _encodingComboBox.SelectedItem = "UTF-8"
            _delimiterComboBox.SelectedIndex = 0
            _hasHeaderCheckBox.Checked = True
            SetMappings(Nothing)
            _configList.ClearSelected()
        End Sub

        Private Sub SelectConfig(sender As Object, e As EventArgs)
            Dim config = TryCast(_configList.SelectedItem, CsvImportConfig)
            If config Is Nothing Then Return
            _currentConfigId = config.ConfigId
            _nameTextBox.Text = config.ConfigName
            _encodingComboBox.SelectedItem = config.Encoding
            Select Case config.Delimiter
                Case "," : _delimiterComboBox.SelectedIndex = 0
                Case ";" : _delimiterComboBox.SelectedIndex = 1
                Case Else : _delimiterComboBox.SelectedIndex = 2
            End Select
            _hasHeaderCheckBox.Checked = config.HasHeader
            SetMappings(_service.GetMappings(config.ConfigId))
        End Sub

        Private Sub SetMappings(mappings As IReadOnlyList(Of CsvColumnMapping))
            _mappingGrid.Rows.Clear()
            Dim fields = {StandardFields.AcquiredAt, StandardFields.EquipmentId, StandardFields.EquipmentName, StandardFields.ItemName, StandardFields.Value, StandardFields.Unit}
            For index = 0 To fields.Length - 1
                Dim fieldName = fields(index)
                Dim columnIndex = index + 1
                If mappings IsNot Nothing Then
                    Dim mapping = mappings.FirstOrDefault(Function(candidate) candidate.FieldName = fieldName)
                    If mapping IsNot Nothing Then columnIndex = mapping.CsvColumnIndex
                End If
                _mappingGrid.Rows.Add(fieldName, columnIndex)
            Next
        End Sub

        Private Sub SaveConfig(sender As Object, e As EventArgs)
            Try
                Dim config As New CsvImportConfig With {.ConfigId = _currentConfigId, .ConfigName = _nameTextBox.Text, .Encoding = CStr(_encodingComboBox.SelectedItem), .Delimiter = SelectedDelimiter(), .HasHeader = _hasHeaderCheckBox.Checked}
                Dim mappings As New List(Of CsvColumnMapping)()
                For Each row As DataGridViewRow In _mappingGrid.Rows
                    Dim columnIndex As Integer
                    If Not Integer.TryParse(CStr(row.Cells("ColumnIndex").Value), columnIndex) Then Throw New ArgumentException("CSV列番号は数値で入力してください。")
                    mappings.Add(New CsvColumnMapping With {.FieldName = CStr(row.Cells("FieldName").Value), .CsvColumnIndex = columnIndex})
                Next
                _service.Save(config, mappings)
                _currentConfigId = config.ConfigId
                LoadConfigs()
                Dim saved = _service.GetById(_currentConfigId)
                _configList.SelectedItem = _configList.Items.Cast(Of CsvImportConfig)().FirstOrDefault(Function(item) item.ConfigId = saved.ConfigId)
                MessageBox.Show("CSV設定を保存しました。", "CSV設定", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As ArgumentException
                MessageBox.Show(ex.Message, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Catch ex As Exception
                MessageBox.Show("CSV設定の保存に失敗しました。", "CSV設定エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub DeleteConfig(sender As Object, e As EventArgs)
            If _currentConfigId = 0 Then Return
            If MessageBox.Show("選択したCSV設定を削除します。よろしいですか？", "CSV設定削除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
            Try
                _service.Delete(_currentConfigId)
                LoadConfigs()
                NewConfig(Nothing, EventArgs.Empty)
            Catch ex As Exception
                MessageBox.Show("CSV設定の削除に失敗しました。", "CSV設定エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Function SelectedDelimiter() As String
            Select Case _delimiterComboBox.SelectedIndex
                Case 0 : Return ","
                Case 1 : Return ";"
                Case Else : Return vbTab
            End Select
        End Function
    End Class
End Namespace
