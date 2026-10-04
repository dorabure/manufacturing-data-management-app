Namespace Interfaces
    Public Interface ICsvFileAdapter
        Function Read(filePath As String, encodingName As String, delimiter As String, hasHeader As Boolean) As IReadOnlyList(Of IReadOnlyDictionary(Of String, String))
        Sub Write(filePath As String, rows As IEnumerable(Of IReadOnlyDictionary(Of String, String)), encodingName As String, delimiter As String, includeHeader As Boolean)
    End Interface
End Namespace
