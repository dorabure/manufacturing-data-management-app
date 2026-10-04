Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities

Namespace Logging
    Public NotInheritable Class PeriodicImportLogWriter
        Implements IImportLogWriter

        Private ReadOnly _inner As IImportLogWriter

        Public Sub New(inner As IImportLogWriter)
            ArgumentNullException.ThrowIfNull(inner)
            _inner = inner
        End Sub

        Public Sub WriteErrors(errors As IEnumerable(Of ErrorLog)) Implements IImportLogWriter.WriteErrors
            _inner.WriteErrors(errors)
        End Sub

        Public Sub WriteOperation(log As OperationLog) Implements IImportLogWriter.WriteOperation
            ArgumentNullException.ThrowIfNull(log)
            ' Do not mutate an object owned by the caller.
            _inner.WriteOperation(New OperationLog With {
                .ExecutedAt = log.ExecutedAt, .OperationType = If(log.OperationType = "CSV取込", "周期CSV取込", log.OperationType),
                .ElapsedMs = log.ElapsedMs, .TotalCount = log.TotalCount,
                .SuccessCount = log.SuccessCount, .FailureCount = log.FailureCount})
        End Sub
    End Class
End Namespace
