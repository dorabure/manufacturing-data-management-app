Imports System.Globalization
Imports System.Windows.Forms
Imports System.Drawing
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Entities

Namespace Forms
    Public Class MeasurementDetailForm
        Inherits Form

        Private ReadOnly _service As MeasurementDataService
        Private ReadOnly _original As MeasurementData
        Private ReadOnly _measuredAtPicker As New DateTimePicker With {.Format = DateTimePickerFormat.Custom, .CustomFormat = "yyyy/MM/dd HH:mm:ss", .Width = 190}
        Private ReadOnly _equipmentIdTextBox As New TextBox()
        Private ReadOnly _equipmentNameTextBox As New TextBox()
        Private ReadOnly _itemNameTextBox As New TextBox()
        Private ReadOnly _valueTextBox As New TextBox()
        Private ReadOnly _unitTextBox As New TextBox()

        Public Sub New(service As MeasurementDataService, measurement As MeasurementData)
            _service = service
            _original = measurement
            Text = "測定データ詳細・編集"
            AutoScaleMode = AutoScaleMode.Font
            ClientSize = New Size(540, 390)
            MinimumSize = New Size(500, 360)
            StartPosition = FormStartPosition.CenterParent
            BuildUi()
            BindMeasurement()
        End Sub

        Private Sub BuildUi()
            Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(15), .ColumnCount = 2, .RowCount = 7}
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 100))
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            For row = 0 To 5
                layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            Next
            layout.RowStyles.Add(New RowStyle(SizeType.Absolute, 42))
            AddRow(layout, 0, "取得日時", _measuredAtPicker)
            AddRow(layout, 1, "設備ID", _equipmentIdTextBox)
            AddRow(layout, 2, "設備名", _equipmentNameTextBox)
            AddRow(layout, 3, "項目名", _itemNameTextBox)
            AddRow(layout, 4, "測定値", _valueTextBox)
            AddRow(layout, 5, "単位", _unitTextBox)
            Dim saveButton As New Button With {.Text = "保存", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler saveButton.Click, AddressOf SaveClicked
            Dim cancelButton As New Button With {.Text = "閉じる", .AutoSize = True, .MinimumSize = New Size(80, 32), .Padding = New Padding(8, 2, 8, 2), .DialogResult = DialogResult.Cancel}
            Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft}
            buttons.Controls.AddRange({cancelButton, saveButton})
            layout.Controls.Add(buttons, 0, 6)
            layout.SetColumnSpan(buttons, 2)
            Controls.Add(layout)
            CancelButton = cancelButton
        End Sub

        Private Shared Sub AddRow(layout As TableLayoutPanel, rowIndex As Integer, labelText As String, control As Control)
            control.Dock = DockStyle.Fill
            layout.Controls.Add(New Label With {.Text = labelText, .AutoSize = True, .Anchor = AnchorStyles.Left}, 0, rowIndex)
            layout.Controls.Add(control, 1, rowIndex)
        End Sub

        Private Sub BindMeasurement()
            _measuredAtPicker.Value = _original.MeasuredAt
            _equipmentIdTextBox.Text = _original.EquipmentId
            _equipmentNameTextBox.Text = _original.EquipmentName
            _itemNameTextBox.Text = _original.ItemName
            _valueTextBox.Text = _original.Value.ToString(CultureInfo.InvariantCulture)
            _unitTextBox.Text = _original.Unit
        End Sub

        Private Sub SaveClicked(sender As Object, e As EventArgs)
            Dim value As Double
            If Not Double.TryParse(_valueTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, value) Then
                MessageBox.Show("測定値は数値で入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            Dim updated As New MeasurementData With {.Id = _original.Id, .MeasuredAt = _measuredAtPicker.Value, .EquipmentId = _equipmentIdTextBox.Text.Trim(), .EquipmentName = _equipmentNameTextBox.Text.Trim(), .ItemName = _itemNameTextBox.Text.Trim(), .Value = value, .Unit = _unitTextBox.Text.Trim()}
            Dim errors = _service.ValidateForUpdate(updated)
            If errors.Count > 0 Then
                Dim messages = String.Join(Environment.NewLine, errors.Select(Function(errorItem) $"{errorItem.FieldName}: {errorItem.Message}"))
                MessageBox.Show(messages, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            Try
                _service.Update(updated)
                MessageBox.Show("データを更新しました。", "更新完了", MessageBoxButtons.OK, MessageBoxIcon.Information)
                DialogResult = DialogResult.OK
                Close()
            Catch ex As Exception
                MessageBox.Show("データの更新に失敗しました。", "更新エラー", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub
    End Class
End Namespace
