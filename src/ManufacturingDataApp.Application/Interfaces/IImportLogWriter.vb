Imports ManufacturingDataApp.Domain.Entities

Namespace Interfaces
    Public Interface IImportLogWriter
        Sub WriteOperation(log As OperationLog)
        Sub WriteErrors(errors As IEnumerable(Of ErrorLog))
    End Interface
End Namespace
