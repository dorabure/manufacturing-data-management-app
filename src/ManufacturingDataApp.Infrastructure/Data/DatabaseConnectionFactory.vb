Imports Microsoft.Data.Sqlite
Imports System.IO

Namespace Data
    Public Class DatabaseConnectionFactory
        Private ReadOnly _connectionString As String

        Public Sub New()
            Me.New(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ManufacturingDataApp", "ManufacturingDataApp.db"))
        End Sub

        Public Sub New(databasePath As String, Optional pooling As Boolean = True)
            If String.IsNullOrWhiteSpace(databasePath) Then
                Throw New ArgumentException("データベースファイルのパスを指定してください。", NameOf(databasePath))
            End If

            Me.DatabasePath = Path.GetFullPath(databasePath)
            _connectionString = New SqliteConnectionStringBuilder With {
                .DataSource = DatabasePath,
                .Pooling = pooling
            }.ToString()
        End Sub

        Public ReadOnly Property DatabasePath As String
        Public Function CreateConnection() As SqliteConnection
            Dim connection = New SqliteConnection(_connectionString)
            Try
                connection.Open()
                Using command = connection.CreateCommand()
                    command.CommandText = "PRAGMA foreign_keys = ON;"
                    command.ExecuteNonQuery()
                End Using
                Return connection
            Catch
                connection.Dispose()
                Throw
            End Try
        End Function
    End Class
End Namespace
