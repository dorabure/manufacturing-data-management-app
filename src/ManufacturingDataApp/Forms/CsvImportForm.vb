Imports System.Windows.Forms
Imports System.Drawing
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Entities
Namespace Forms
    Public Class CsvImportForm
        Inherits Form
        Private ReadOnly _importService As CsvImportService
        Private ReadOnly _configService As CsvConfigService
        Private ReadOnly _configComboBox As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 180}
        Private ReadOnly _filePathTextBox As New TextBox With {.Width = 300}

        Public Sub New(importService As CsvImportService, configService As CsvConfigService)
            _importService = importService
            _configService = configService
            Text = "CSV取込"
            AutoScaleMode = AutoScaleMode.Font
            ClientSize = New Size(620, 200)
            MinimumSize = New Size(560, 200)
            StartPosition = FormStartPosition.CenterParent
            BuildUi()
            _configComboBox.DataSource = _configService.GetAll().ToList()
            _configComboBox.DisplayMember = "ConfigName"
        End Sub

        Private Sub BuildUi()
            Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(12), .ColumnCount = 3, .RowCount = 3}
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 80))
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90))
            layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            layout.Controls.Add(New Label With {.Text = "CSV設定", .AutoSize = True}, 0, 0)
            layout.Controls.Add(_configComboBox, 1, 0)
            layout.Controls.Add(New Label With {.Text = "CSVファイル", .AutoSize = True}, 0, 1)
            layout.Controls.Add(_filePathTextBox, 1, 1)
            Dim browseButton As New Button With {.Text = "参照", .AutoSize = True, .MinimumSize = New Size(76, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler browseButton.Click, AddressOf BrowseFile
            layout.Controls.Add(browseButton, 2, 1)
            Dim importButton As New Button With {.Text = "取込", .AutoSize = True, .MinimumSize = New Size(76, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler importButton.Click, AddressOf ImportFile
            layout.Controls.Add(importButton, 2, 2)
            Controls.Add(layout)
        End Sub

        Private Sub BrowseFile(sender As Object, e As EventArgs)
            Using dialog As New OpenFileDialog With {.Filter = "CSV ファイル (*.csv)|*.csv"}
                If dialog.ShowDialog(Me) = DialogResult.OK Then _filePathTextBox.Text = dialog.FileName
            End Using
        End Sub

        Private Sub ImportFile(sender As Object, e As EventArgs)
            Dim config = TryCast(_configComboBox.SelectedItem, CsvImportConfig)
            If config Is Nothing Then
                MessageBox.Show("CSV設定を選択してください。", "CSV取込", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            If String.IsNullOrWhiteSpace(_filePathTextBox.Text) OrElse Not IO.File.Exists(_filePathTextBox.Text) Then
                MessageBox.Show("取込むCSVファイルを選択してください。", "CSV取込", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            Try
                Dim request As New CsvImportRequestDto With {.FilePath = _filePathTextBox.Text, .Config = config}
                request.ColumnMappings.AddRange(_configService.GetMappings(config.ConfigId))
                Dim result = _importService.ImportAndSave(request)
                MessageBox.Show($"CSV取込が完了しました。{Environment.NewLine}成功: {result.SuccessCount}件 / 失敗: {result.FailureCount}件", "CSV取込", MessageBoxButtons.OK, MessageBoxIcon.Information)
                DialogResult = DialogResult.OK
                Close()
            Catch ex As Exception
                MessageBox.Show("CSV取込に失敗しました。", "CSV取込エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub
    End Class
End Namespace
