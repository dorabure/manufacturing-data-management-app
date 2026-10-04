Imports System.Windows.Forms
Imports System.Drawing
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Entities
Namespace Forms
    Public Class MasterForm
        Inherits Form
        Private ReadOnly _service As MasterDataService
        Private ReadOnly _equipmentGrid As New DataGridView()
        Private ReadOnly _itemGrid As New DataGridView()
        Private ReadOnly _equipmentIdTextBox As New TextBox()
        Private ReadOnly _equipmentNameTextBox As New TextBox()
        Private ReadOnly _factoryTextBox As New TextBox()
        Private ReadOnly _lineTextBox As New TextBox()
        Private ReadOnly _itemNameTextBox As New TextBox()
        Private ReadOnly _unitTextBox As New TextBox()
        Private ReadOnly _minValueTextBox As New TextBox()
        Private ReadOnly _maxValueTextBox As New TextBox()
        Private _selectedItemId As Integer

        Public Sub New(service As MasterDataService)
            _service = service
            Text = "マスタ管理"
            AutoScaleMode = AutoScaleMode.Font
            ClientSize = New Size(920, 640)
            MinimumSize = New Size(780, 560)
            StartPosition = FormStartPosition.CenterParent
            BuildUi()
            RefreshEquipment()
            RefreshItems()
        End Sub

        Private Sub BuildUi()
            Dim tabs As New TabControl With {.Dock = DockStyle.Fill}
            tabs.TabPages.Add(BuildEquipmentPage())
            tabs.TabPages.Add(BuildItemPage())
            Controls.Add(tabs)
        End Sub

        Private Function BuildEquipmentPage() As TabPage
            Dim page As New TabPage("設備マスタ")
            ConfigureGrid(_equipmentGrid, {("EquipmentId", "設備ID"), ("EquipmentName", "設備名"), ("FactoryName", "工場名"), ("LineName", "ライン名")})
            AddHandler _equipmentGrid.SelectionChanged, AddressOf EquipmentSelected
            Dim input = CreateInputPanel({("設備ID", CType(_equipmentIdTextBox, Control)), ("設備名", _equipmentNameTextBox), ("工場名", _factoryTextBox), ("ライン名", _lineTextBox)})
            Dim newButton As New Button With {.Text = "新規", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler newButton.Click, Sub(sender, e) ClearEquipment()
            Dim saveButton As New Button With {.Text = "保存", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler saveButton.Click, AddressOf SaveEquipment
            Dim deleteButton As New Button With {.Text = "削除", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler deleteButton.Click, AddressOf DeleteEquipment
            Dim equipmentButtons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 40, .WrapContents = False, .Padding = New Padding(0, 3, 0, 0)}
            equipmentButtons.Controls.AddRange({newButton, saveButton, deleteButton})
            input.Controls.Add(equipmentButtons)
            page.Controls.Add(_equipmentGrid)
            page.Controls.Add(input)
            Return page
        End Function

        Private Function BuildItemPage() As TabPage
            Dim page As New TabPage("測定項目マスタ")
            ConfigureGrid(_itemGrid, {("ItemName", "項目名"), ("Unit", "単位"), ("MinValue", "最小値"), ("MaxValue", "最大値")})
            AddHandler _itemGrid.SelectionChanged, AddressOf ItemSelected
            Dim input = CreateInputPanel({("項目名", CType(_itemNameTextBox, Control)), ("単位", _unitTextBox), ("最小値", _minValueTextBox), ("最大値", _maxValueTextBox)})
            Dim newButton As New Button With {.Text = "新規", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler newButton.Click, Sub(sender, e) ClearItem()
            Dim saveButton As New Button With {.Text = "保存", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler saveButton.Click, AddressOf SaveItem
            Dim deleteButton As New Button With {.Text = "削除", .AutoSize = True, .MinimumSize = New Size(70, 32), .Padding = New Padding(8, 2, 8, 2)}
            AddHandler deleteButton.Click, AddressOf DeleteItem
            Dim itemButtons As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 40, .WrapContents = False, .Padding = New Padding(0, 3, 0, 0)}
            itemButtons.Controls.AddRange({newButton, saveButton, deleteButton})
            input.Controls.Add(itemButtons)
            page.Controls.Add(_itemGrid)
            page.Controls.Add(input)
            Return page
        End Function

        Private Shared Sub ConfigureGrid(grid As DataGridView, columns As (PropertyName As String, Header As String)())
            grid.Dock = DockStyle.Fill
            grid.AutoGenerateColumns = False
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            grid.MultiSelect = False
            grid.ColumnHeadersVisible = True
            grid.ColumnHeadersHeight = 30
            grid.RowHeadersVisible = False
            For Each column In columns
                grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = column.PropertyName, .HeaderText = column.Header, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill})
            Next
        End Sub

        Private Shared Function CreateInputPanel(fields As (LabelText As String, Control As Control)()) As Panel
            Dim panel As New Panel With {.Dock = DockStyle.Bottom, .Height = 170, .Padding = New Padding(8)}
            Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Top, .ColumnCount = 4, .RowCount = 2, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            For index = 0 To fields.Length - 1
                Dim row = index \ 2
                Dim column = (index Mod 2) * 2
                fields(index).Control.Dock = DockStyle.Fill
                fields(index).Control.Margin = New Padding(3)
                layout.Controls.Add(New Label With {.Text = fields(index).LabelText, .AutoSize = False, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(3)}, column, row)
                layout.Controls.Add(fields(index).Control, column + 1, row)
            Next
            panel.Controls.Add(layout)
            Return panel
        End Function

        Private Sub RefreshEquipment()
            _equipmentGrid.DataSource = _service.GetEquipment().ToList()
        End Sub
        Private Sub RefreshItems()
            _itemGrid.DataSource = _service.GetMeasurementItems().ToList()
        End Sub

        Private Sub EquipmentSelected(sender As Object, e As EventArgs)
            Dim equipment = TryCast(If(_equipmentGrid.CurrentRow Is Nothing, Nothing, _equipmentGrid.CurrentRow.DataBoundItem), EquipmentMaster)
            If equipment Is Nothing Then Return
            _equipmentIdTextBox.Text = equipment.EquipmentId
            _equipmentIdTextBox.ReadOnly = True
            _equipmentNameTextBox.Text = equipment.EquipmentName
            _factoryTextBox.Text = equipment.FactoryName
            _lineTextBox.Text = equipment.LineName
        End Sub
        Private Sub ItemSelected(sender As Object, e As EventArgs)
            Dim item = TryCast(If(_itemGrid.CurrentRow Is Nothing, Nothing, _itemGrid.CurrentRow.DataBoundItem), MeasurementItemMaster)
            If item Is Nothing Then Return
            _selectedItemId = item.ItemId
            _itemNameTextBox.ReadOnly = True
            _itemNameTextBox.Text = item.ItemName
            _unitTextBox.Text = item.Unit
            _minValueTextBox.Text = If(item.MinValue.HasValue, item.MinValue.Value.ToString(Globalization.CultureInfo.InvariantCulture), "")
            _maxValueTextBox.Text = If(item.MaxValue.HasValue, item.MaxValue.Value.ToString(Globalization.CultureInfo.InvariantCulture), "")
        End Sub
        Private Sub ClearEquipment()
            _equipmentGrid.ClearSelection()
            _equipmentIdTextBox.ReadOnly = False
            _equipmentIdTextBox.Clear() : _equipmentNameTextBox.Clear() : _factoryTextBox.Clear() : _lineTextBox.Clear()
        End Sub
        Private Sub ClearItem()
            _itemGrid.ClearSelection()
            _selectedItemId = 0
            _itemNameTextBox.ReadOnly = False
            _itemNameTextBox.Clear() : _unitTextBox.Clear() : _minValueTextBox.Clear() : _maxValueTextBox.Clear()
        End Sub
        Private Sub SaveEquipment(sender As Object, e As EventArgs)
            Try
                Dim isNew = Not _equipmentIdTextBox.ReadOnly
                _service.SaveEquipment(New EquipmentMaster With {.EquipmentId = _equipmentIdTextBox.Text, .EquipmentName = _equipmentNameTextBox.Text, .FactoryName = _factoryTextBox.Text, .LineName = _lineTextBox.Text}, isNew)
                RefreshEquipment() : ClearEquipment()
                MessageBox.Show("設備マスタを保存しました。", "マスタ管理", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As ArgumentException
                MessageBox.Show(ex.Message, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Catch ex As Exception
                MessageBox.Show("設備マスタの保存に失敗しました。", "マスタ管理", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub
        Private Sub DeleteEquipment(sender As Object, e As EventArgs)
            If Not _equipmentIdTextBox.ReadOnly Then Return
            If MessageBox.Show("選択した設備を削除します。よろしいですか？", "設備削除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
            Try
                _service.DeleteEquipment(_equipmentIdTextBox.Text)
                RefreshEquipment() : ClearEquipment()
            Catch ex As Exception
                MessageBox.Show(ex.Message, "設備削除", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub
        Private Sub SaveItem(sender As Object, e As EventArgs)
            Try
                Dim minValue = ParseOptionalDouble(_minValueTextBox.Text, "最小値")
                Dim maxValue = ParseOptionalDouble(_maxValueTextBox.Text, "最大値")
                _service.SaveMeasurementItem(New MeasurementItemMaster With {.ItemId = _selectedItemId, .ItemName = _itemNameTextBox.Text, .Unit = _unitTextBox.Text, .MinValue = minValue, .MaxValue = maxValue}, _selectedItemId = 0)
                RefreshItems() : ClearItem()
                MessageBox.Show("測定項目マスタを保存しました。", "マスタ管理", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As ArgumentException
                MessageBox.Show(ex.Message, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Catch ex As Exception
                MessageBox.Show("測定項目マスタの保存に失敗しました。", "マスタ管理", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub
        Private Sub DeleteItem(sender As Object, e As EventArgs)
            If _selectedItemId = 0 Then Return
            If MessageBox.Show("選択した測定項目を削除します。よろしいですか？", "測定項目削除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
            Try
                _service.DeleteMeasurementItem(_selectedItemId)
                RefreshItems() : ClearItem()
            Catch ex As Exception
                MessageBox.Show("測定項目マスタの削除に失敗しました。", "測定項目削除", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub
        Private Shared Function ParseOptionalDouble(value As String, fieldName As String) As Double?
            If String.IsNullOrWhiteSpace(value) Then Return Nothing
            Dim parsed As Double
            If Not Double.TryParse(value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, parsed) Then Throw New ArgumentException($"{fieldName}は数値で入力してください。")
            Return parsed
        End Function
    End Class
End Namespace
