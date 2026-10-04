Imports ManufacturingDataApp.Domain.Entities
Namespace Interfaces
    Public Interface IErrorLogRepository
        Function Search(fromDate As DateTime?, toDate As DateTime?, fileName As String, errorType As String) As IReadOnlyList(Of ErrorLog)
    End Interface
End Namespace
