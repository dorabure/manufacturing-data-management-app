Namespace Data
    Friend NotInheritable Class CsvImportConfigSql
        Public Const SelectAll As String = "SELECT ConfigId, ConfigName, Encoding, Delimiter, HasHeader, ImportMode, WatchFolderPath, WatchIntervalSeconds, typeof(ImportMode), typeof(WatchFolderPath), typeof(WatchIntervalSeconds) FROM CsvImportConfig ORDER BY ConfigName;"
        Public Const SelectMappings As String = "SELECT MappingId, ConfigId, FieldName, CsvColumnIndex FROM CsvColumnMapping WHERE ConfigId = $configId ORDER BY CsvColumnIndex;"
        Public Const InsertConfig As String = "INSERT INTO CsvImportConfig (ConfigName, Encoding, Delimiter, HasHeader) VALUES ($configName, $encoding, $delimiter, $hasHeader); SELECT last_insert_rowid();"
        Public Const UpdateFormat As String = "UPDATE CsvImportConfig SET ConfigName = $configName, Encoding = $encoding, Delimiter = $delimiter, HasHeader = $hasHeader WHERE ConfigId = $configId;"
        Public Const DeleteMappings As String = "DELETE FROM CsvColumnMapping WHERE ConfigId = $configId;"
        Public Const InsertMapping As String = "INSERT INTO CsvColumnMapping (ConfigId, FieldName, CsvColumnIndex) VALUES ($configId, $fieldName, $columnIndex);"
        Public Const DeleteConfig As String = "DELETE FROM CsvImportConfig WHERE ConfigId = $configId;"
        Public Shared Function SelectWhere(condition As String) As String
            Return $"SELECT ConfigId, ConfigName, Encoding, Delimiter, HasHeader, ImportMode, WatchFolderPath, WatchIntervalSeconds, typeof(ImportMode), typeof(WatchFolderPath), typeof(WatchIntervalSeconds) FROM CsvImportConfig WHERE {condition};"
        End Function
        Public Const UpdateMonitoring As String = "UPDATE CsvImportConfig SET ImportMode=$mode, WatchFolderPath=$folder, WatchIntervalSeconds=$seconds WHERE ConfigId=$id;"
    End Class
End Namespace
