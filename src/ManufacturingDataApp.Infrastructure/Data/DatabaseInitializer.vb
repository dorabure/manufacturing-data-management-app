Imports Microsoft.Data.Sqlite
Imports System.IO

Namespace Data
    Public Class DatabaseInitializer
        Private ReadOnly _factory As DatabaseConnectionFactory
        Private ReadOnly _migrator As DatabaseMigrator

        Public Sub New(factory As DatabaseConnectionFactory)
            Me.New(factory, New DatabaseMigrator())
        End Sub

        Friend Sub New(factory As DatabaseConnectionFactory, migrator As DatabaseMigrator)
            _factory = factory
            _migrator = migrator
        End Sub

        Public Sub Initialize()
            Dim databaseDirectory = Path.GetDirectoryName(_factory.DatabasePath)
            If String.IsNullOrWhiteSpace(databaseDirectory) Then Throw New InvalidOperationException("データベースディレクトリを特定できません。")
            Directory.CreateDirectory(databaseDirectory)
            Using connection = _factory.CreateConnection()
                _migrator.Initialize(connection, _factory.DatabasePath)
            End Using
        End Sub
    End Class
End Namespace
