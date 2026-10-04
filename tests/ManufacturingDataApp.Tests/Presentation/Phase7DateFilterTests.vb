Imports System.Windows.Forms
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Forms
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase7DateFilterTests
    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub UncheckedLogDatesDoNotBecomeMinValue(operation As Boolean)
        RunSta(Sub()
                   Using db As New Phase2Database(True)
                       db.Initialize()
                       Dim service As New LogViewService(New ErrorLogRepository(db.Factory), New OperationLogRepository(db.Factory))
                       Using form As Form = If(operation, CType(New OperationLogForm(service), Form), New ErrorLogForm(service))
                           Dim handle = form.Handle
                           Dim grid = Field(Of DataGridView)(form, "_grid")
                           grid.CreateControl()
                           Field(Of DateTimePicker)(form, "_from").Checked = False
                           Field(Of DateTimePicker)(form, "_to").Checked = False
                           Invoke(form, "Search")
                           Assert.Equal(1, grid.Rows.Count)
                       End Using
                   End Using
               End Sub)
    End Sub
End Class
