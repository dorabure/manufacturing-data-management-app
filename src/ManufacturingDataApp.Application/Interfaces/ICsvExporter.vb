Imports ManufacturingDataApp.Domain.Entities

Namespace Interfaces
    Public Interface ICsvExporter
        Sub Write(filePath As String, headers As IReadOnlyList(Of String), rows As IEnumerable(Of IReadOnlyList(Of String)), config As CsvImportConfig)
    End Interface
End Namespace
