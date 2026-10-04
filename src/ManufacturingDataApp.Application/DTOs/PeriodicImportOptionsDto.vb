Imports ManufacturingDataApp.Domain.Entities

Namespace DTOs
    Public Class PeriodicImportOptionsDto
        Public Property WatchIntervalSeconds As Integer = 60
        Public Property Config As New CsvImportConfig()
        Public Property ColumnMappings As New List(Of CsvColumnMapping)()
        Public Property DatabaseIdentity As String

        Public Function DeepCopy() As PeriodicImportOptionsDto
            Return New PeriodicImportOptionsDto With {
                .WatchIntervalSeconds = WatchIntervalSeconds, .DatabaseIdentity = DatabaseIdentity,
                .Config = If(Config Is Nothing, Nothing, New CsvImportConfig With {
                    .ConfigId = Config.ConfigId, .ConfigName = Config.ConfigName, .Encoding = Config.Encoding,
                    .Delimiter = Config.Delimiter, .HasHeader = Config.HasHeader, .ImportMode = Config.ImportMode,
                    .WatchFolderPath = Config.WatchFolderPath, .WatchIntervalSeconds = Config.WatchIntervalSeconds}),
                .ColumnMappings = If(ColumnMappings Is Nothing, Nothing, ColumnMappings.Select(Function(m) New CsvColumnMapping With {
                    .MappingId = m.MappingId, .ConfigId = m.ConfigId, .FieldName = m.FieldName, .CsvColumnIndex = m.CsvColumnIndex}).ToList())}
        End Function
    End Class
End Namespace
