Imports System.ComponentModel
Imports System.Runtime.ExceptionServices
Imports System.Threading
Imports System.Windows.Forms
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Forms
Imports Xunit

Public Class MainFormSelectionTests
    <Theory>
    <InlineData(0)>
    <InlineData(1)>
    <InlineData(2)>
    Public Sub SelectedRows_ReturnBoundIdsWithoutAnIdColumn(count As Integer)
        RunSta(Sub()
                   Using grid = CreateGrid()
                       For i = 0 To count - 1
                           grid.Rows(i).Cells(MainForm.SelectionColumnName).Value = True
                       Next
                       Assert.Equal(New Long() {91, 37}.Take(count), MainForm.GetSelectedMeasurementIds(grid))
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub Detail_ReturnsBoundIdForEachRowWithoutAnIdColumn()
        RunSta(Sub()
                   Using grid = CreateGrid()
                       Assert.Equal(91L, MainForm.GetDetailMeasurementId(grid, New DataGridViewCellEventArgs(1, 0)).Value)
                       Assert.Equal(37L, MainForm.GetDetailMeasurementId(grid, New DataGridViewCellEventArgs(1, 1)).Value)
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub Detail_IgnoresHeadersCheckboxAndNewRow()
        RunSta(Sub()
                   Using grid = CreateGrid()
                       Assert.Null(MainForm.GetDetailMeasurementId(grid, New DataGridViewCellEventArgs(1, -1)))
                       Assert.Null(MainForm.GetDetailMeasurementId(grid, New DataGridViewCellEventArgs(-1, 0)))
                       Assert.Null(MainForm.GetDetailMeasurementId(grid, New DataGridViewCellEventArgs(0, 0)))
                       grid.AllowUserToAddRows = True
                       Assert.True(grid.Rows(grid.NewRowIndex).IsNewRow)
                       Assert.Null(MainForm.GetDetailMeasurementId(grid, New DataGridViewCellEventArgs(1, grid.NewRowIndex)))
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub Selection_IgnoresNullValuesAndRejectsInvalidSelection()
        RunSta(Sub()
                   Using grid = CreateGrid()
                       grid.Rows(0).Cells(0).Value = Nothing
                       grid.Rows(1).Cells(0).Value = DBNull.Value
                       Assert.Empty(MainForm.GetSelectedMeasurementIds(grid))
                       grid.Rows(0).Cells(0).Value = "invalid"
                       Assert.Throws(Of InvalidOperationException)(Sub() MainForm.GetSelectedMeasurementIds(grid))
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub InvalidBoundId_RejectsEntireSelectionAndDetail()
        RunSta(Sub()
                   Using grid = CreateGrid()
                       DirectCast(grid.Rows(1).DataBoundItem, MeasurementData).Id = 0
                       grid.Rows(0).Cells(0).Value = True
                       grid.Rows(1).Cells(0).Value = True
                       Assert.Throws(Of InvalidOperationException)(Sub() MainForm.GetSelectedMeasurementIds(grid))
                       Assert.Throws(Of InvalidOperationException)(Sub() MainForm.GetDetailMeasurementId(grid, New DataGridViewCellEventArgs(1, 1)))
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub UnboundRow_IsRejectedWithoutCellIdConversion()
        RunSta(Sub()
                   Using grid = CreateGrid()
                       grid.DataSource = Nothing
                       grid.Rows.Add(True, "orphan")
                       Assert.Throws(Of InvalidOperationException)(Sub() MainForm.GetSelectedMeasurementIds(grid))
                       Assert.Throws(Of InvalidOperationException)(Sub() MainForm.GetDetailMeasurementId(grid, New DataGridViewCellEventArgs(1, 0)))
                   End Using
               End Sub)
    End Sub

    Private Shared Function CreateGrid() As DataGridView
        Dim grid As New DataGridView With {.AutoGenerateColumns = False, .AllowUserToAddRows = False, .BindingContext = New BindingContext()}
        grid.Columns.Add(New DataGridViewCheckBoxColumn With {.Name = MainForm.SelectionColumnName})
        grid.Columns.Add(New DataGridViewTextBoxColumn With {.DataPropertyName = NameOf(MeasurementData.EquipmentId)})
        grid.DataSource = New BindingList(Of MeasurementData)(New List(Of MeasurementData) From {
            New MeasurementData With {.Id = 91, .EquipmentId = "EQ002"},
            New MeasurementData With {.Id = 37, .EquipmentId = "EQ001"}})
        Return grid
    End Function

    Private Shared Sub RunSta(action As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     Try
                                         action()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        thread.Join()
        If failure IsNot Nothing Then ExceptionDispatchInfo.Capture(failure).Throw()
    End Sub
End Class
