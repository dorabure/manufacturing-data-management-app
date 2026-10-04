Imports System.Windows.Forms
Imports ManufacturingDataApp.Forms
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase7GridTests
    <Fact>
    Public Sub DataCellsCannotEditBoundExportRowsButCheckboxCanEdit()
        RunSta(Sub()
                   Using db As New Phase2Database()
                       db.Initialize()
                       Using form = CreateMain(db, Nothing)
                           Dim grid = Field(Of DataGridView)(form, "_grid")
                           Dim row As New MeasurementData With {.Id = 7, .MeasuredAt = DateTime.Today, .EquipmentId = "EQ001", .EquipmentName = "keep", .ItemName = "温度", .Value = 10, .Unit = "℃"}
                           Dim rows As New List(Of MeasurementData) From {row}
                           grid.DataSource = rows
                           Dim handle = form.Handle
                           grid.CreateControl()
                           For Each column As DataGridViewColumn In grid.Columns
                               Assert.Equal(column.Name <> MainForm.SelectionColumnName, column.ReadOnly)
                           Next
                           For Each name In {"MeasuredAt", "EquipmentId", "EquipmentName", "ItemName", "Value", "Unit"}
                               Dim column = grid.Columns.Cast(Of DataGridViewColumn)().Single(Function(c) c.DataPropertyName = name)
                               grid.CurrentCell = grid.Rows(0).Cells(column.Index)
                               Assert.False(grid.BeginEdit(False))
                           Next
                           Assert.Equal("keep", row.EquipmentName)
                           Assert.Equal(10, row.Value)
                           grid.CurrentCell = grid.Rows(0).Cells(MainForm.SelectionColumnName)
                           Assert.True(grid.BeginEdit(False))
                           grid.CurrentCell.Value = True
                           Assert.True(grid.EndEdit())
                           Assert.Equal(New Long() {7}, MainForm.GetSelectedMeasurementIds(grid))
                       End Using
                   End Using
               End Sub)
    End Sub
End Class
