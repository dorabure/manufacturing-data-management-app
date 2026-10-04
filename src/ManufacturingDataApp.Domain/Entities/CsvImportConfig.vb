Namespace Entities
    Public Class CsvImportConfig
        Public Property ConfigId As Integer
        Public Property ConfigName As String = String.Empty
        Public Property Encoding As String = "UTF-8"
        Public Property Delimiter As String = ","
        Public Property HasHeader As Boolean = True
        Public Property ImportMode As Constants.ImportMode = Constants.ImportMode.SingleFile
        Public Property WatchFolderPath As String = Nothing
        Public Property WatchIntervalSeconds As Integer = 60
    End Class
End Namespace
