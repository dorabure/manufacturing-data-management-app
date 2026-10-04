Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Namespace Services
    Public Class LogViewService
        Private ReadOnly _errors As IErrorLogRepository
        Private ReadOnly _operations As IOperationLogRepository
        Public Sub New(errors As IErrorLogRepository, operations As IOperationLogRepository)
            _errors = errors : _operations = operations
        End Sub
        Public Function SearchErrors(fromDate As DateTime?, toDate As DateTime?, fileName As String, errorType As String) As IReadOnlyList(Of ErrorLog)
            Return _errors.Search(fromDate, toDate, fileName, errorType)
        End Function
        Public Function SearchOperations(fromDate As DateTime?, toDate As DateTime?, operationType As String) As IReadOnlyList(Of OperationLog)
            Return _operations.Search(fromDate, toDate, operationType)
        End Function
    End Class
End Namespace
