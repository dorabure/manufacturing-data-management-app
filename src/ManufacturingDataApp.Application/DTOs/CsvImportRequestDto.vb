Imports ManufacturingDataApp.Domain.Entities

Namespace DTOs
    Public Class CsvImportRequestDto
        Public Property FilePath As String = String.Empty
        Public Property Config As New CsvImportConfig()
        Public ReadOnly Property ColumnMappings As New List(Of CsvColumnMapping)()
    End Class
End Namespace
