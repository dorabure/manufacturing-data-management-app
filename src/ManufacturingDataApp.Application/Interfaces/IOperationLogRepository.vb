Imports ManufacturingDataApp.Domain.Entities
Namespace Interfaces
    Public Interface IOperationLogRepository
        Function Search(fromDate As DateTime?, toDate As DateTime?, operationType As String) As IReadOnlyList(Of OperationLog)
    End Interface
End Namespace
