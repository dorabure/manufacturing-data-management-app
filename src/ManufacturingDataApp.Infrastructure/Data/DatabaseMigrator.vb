Imports Microsoft.Data.Sqlite
Imports System.IO

Namespace Data
    Friend NotInheritable Class DatabaseMigrator
        ' Test-only per-instance fault boundary; production callers cannot bypass validation.
        Private ReadOnly _checkpoint As Action(Of String, SqliteConnection, SqliteTransaction)
        Public Sub New(Optional checkpoint As Action(Of String, SqliteConnection, SqliteTransaction) = Nothing)
            _checkpoint = checkpoint
        End Sub

        Public Sub Initialize(connection As SqliteConnection, databasePath As String)
            Dim version = CLng(DatabaseSchemaValidator.Scalar(connection, "PRAGMA user_version;"))
            If version <> 0 AndAlso version <> DatabaseMigrations.CurrentVersion Then Throw New InvalidDataException("未対応のDB user_version: " & version.ToString())
            Dim objects = CLng(DatabaseSchemaValidator.Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';"))
            If version = 0 AndAlso objects = 0 Then
                CreateNew(connection)
            ElseIf version = DatabaseMigrations.CurrentVersion Then
                DatabaseSchemaValidator.Validate(connection, 110)
                DatabaseSchemaValidator.CheckIntegrity(connection)
            Else
                Migrate(connection, databasePath)
            End If
        End Sub

        Private Sub CreateNew(connection As SqliteConnection)
            Using transaction = connection.BeginTransaction(deferred:=False)
                If CLng(DatabaseSchemaValidator.Scalar(connection, "PRAGMA user_version;", transaction)) <> 0 OrElse
                    CLng(DatabaseSchemaValidator.Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';", transaction)) <> 0 Then
                    Throw New InvalidDataException("初期化前にDBが変更されました。")
                End If
                For Each statement In DatabaseSchema.CreateStatements
                    DatabaseSchemaValidator.Execute(connection, statement, transaction)
                Next
                Checkpoint("NewSchemaCreated", connection, transaction)
                DatabaseSchemaValidator.Execute(connection, DatabaseSchema.SeedDataStatement, transaction)
                DatabaseSchemaValidator.Execute(connection, "PRAGMA user_version=110;", transaction)
                DatabaseSchemaValidator.Validate(connection, 110, transaction)
                DatabaseSchemaValidator.CheckIntegrity(connection, transaction)
                Checkpoint("BeforeNewCommit", connection, transaction)
                transaction.Commit()
            End Using
        End Sub

        Private Sub Migrate(connection As SqliteConnection, databasePath As String)
            Dim beforeVersion = CLng(DatabaseSchemaValidator.Scalar(connection, "PRAGMA data_version;"))
            DatabaseSchemaValidator.Validate(connection, 0)
            DatabaseSchemaValidator.CheckIntegrity(connection)
            Checkpoint("BeforeBackup", connection, Nothing)
            Dim backupPath = databasePath & ".pre-v110." & DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", Globalization.CultureInfo.InvariantCulture) & "." & Guid.NewGuid().ToString("N") & ".db.bak"
            ' Reserve the destination without overwriting an existing file.
            Using reservation As New FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)
            End Using
            Dim backupString As New SqliteConnectionStringBuilder With {.DataSource = backupPath, .Mode = SqliteOpenMode.ReadWrite, .Pooling = False}
            Using destination As New SqliteConnection(backupString.ToString())
                destination.Open()
                connection.BackupDatabase(destination)
                Checkpoint("BackupCreated", destination, Nothing)
            End Using
            ' Reopen without ReadOnly: the installed SQLite runtime's integrity_check
            ' can omit CHECK violations on a read-only connection (covered by tests).
            ' Validation issues only PRAGMA/SELECT; it does not change backup data.
            backupString.Mode = SqliteOpenMode.ReadWrite
            Using backup As New SqliteConnection(backupString.ToString())
                backup.Open()
                DatabaseSchemaValidator.CheckIntegrity(backup)
                DatabaseSchemaValidator.Validate(backup, 0)
                If CLng(DatabaseSchemaValidator.Scalar(backup, "PRAGMA user_version;")) <> 0 Then Throw New InvalidDataException("バックアップ版が不一致です。")
            End Using
            Checkpoint("BackupVerified", connection, Nothing)
            Using transaction = connection.BeginTransaction(deferred:=False)
                If CLng(DatabaseSchemaValidator.Scalar(connection, "PRAGMA data_version;", transaction)) <> beforeVersion OrElse
                    CLng(DatabaseSchemaValidator.Scalar(connection, "PRAGMA user_version;", transaction)) <> 0 Then
                    Throw New InvalidDataException("バックアップ中に原DBが変更されました。移行を中止します。")
                End If
                DatabaseSchemaValidator.Validate(connection, 0, transaction)
                Dim stepNumber = 0
                For Each statement In DatabaseMigrations.Statements
                    DatabaseSchemaValidator.Execute(connection, statement, transaction)
                    stepNumber += 1
                    Checkpoint("MigrationStep" & stepNumber.ToString(), connection, transaction)
                Next
                DatabaseSchemaValidator.Validate(connection, 110, transaction)
                DatabaseSchemaValidator.CheckIntegrity(connection, transaction)
                If CLng(DatabaseSchemaValidator.Scalar(connection, "PRAGMA user_version;", transaction)) <> 110 Then Throw New InvalidDataException("移行後の版不一致")
                Checkpoint("BeforeMigrationCommit", connection, transaction)
                transaction.Commit()
            End Using
        End Sub

        Private Sub Checkpoint(name As String, connection As SqliteConnection, transaction As SqliteTransaction)
            If _checkpoint IsNot Nothing Then _checkpoint(name, connection, transaction)
        End Sub
    End Class
End Namespace
