Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Tests.TestSupport
Imports System.IO
Imports Xunit

Public Class DatabaseMigrationTests
    <Fact>
    Public Sub T44_LegacyMigration_PreservesAllSevenTablesAndIds_AndValidBackup()
        Using db As New Phase2Database(legacy:=True)
            Dim before = db.Snapshot()
            Assert.Equal(5L, CLng(db.Scalar("SELECT count(*) FROM pragma_table_info('CsvImportConfig');")))
            db.Initialize()
            Assert.Equal(before, db.Snapshot())
            Assert.Equal(110L, CLng(db.Scalar("PRAGMA user_version;")))
            Dim config = New CsvImportConfigRepository(db.Factory).FindById(1)
            Assert.Equal(ImportMode.SingleFile, config.ImportMode)
            Assert.Null(config.WatchFolderPath)
            Assert.Equal(60, config.WatchIntervalSeconds)
            Assert.Equal(0L, CLng(db.Scalar("SELECT count(*) FROM EquipmentMaster WHERE EquipmentId='EQ003';")))
            Dim backup = Assert.Single(db.Backups())
            Using connection As New SqliteConnection(New SqliteConnectionStringBuilder With {.DataSource = backup, .Mode = SqliteOpenMode.ReadOnly, .Pooling = False}.ToString())
                connection.Open()
                Assert.Equal(0L, CLng(DatabaseSchemaValidator.Scalar(connection, "PRAGMA user_version;")))
                DatabaseSchemaValidator.CheckIntegrity(connection)
                DatabaseSchemaValidator.Validate(connection, 0)
                Assert.Equal("保持エラー", CStr(DatabaseSchemaValidator.Scalar(connection, "SELECT ErrorMessage FROM ErrorLog WHERE ErrorId=91;")))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub T55_InitializeTwice_DoesNotRepeatMigrationBackupOrSeed()
        Using db As New Phase2Database(True)
            db.Initialize()
            db.Execute("DELETE FROM CsvColumnMapping; DELETE FROM ErrorLog; DELETE FROM CsvImportConfig; DELETE FROM MeasurementItemMaster WHERE ItemId=3;")
            Dim before = db.Snapshot()
            Dim schema = CStr(db.Scalar("SELECT group_concat(sql) FROM sqlite_master;"))
            db.Initialize()
            Assert.Equal(before, db.Snapshot())
            Assert.Equal(schema, CStr(db.Scalar("SELECT group_concat(sql) FROM sqlite_master;")))
            Assert.Single(db.Backups())
            Assert.Equal(110L, CLng(db.Scalar("PRAGMA user_version;")))
        End Using
    End Sub

    <Theory>
    <InlineData("MigrationStep1")>
    <InlineData("MigrationStep2")>
    <InlineData("MigrationStep4")>
    <InlineData("BeforeMigrationCommit")>
    Public Sub T55_FailureDuringMigration_RollsBackColumnsVersionAndPreservesData(stage As String)
        Using db As New Phase2Database(True)
            Dim before = db.Snapshot()
            Assert.Throws(Of IOException)(
                Sub() db.Initialize(Sub(point, connection, transaction)
                                        If point = stage Then Throw New IOException("injected")
                                    End Sub))
            Assert.Equal(before, db.Snapshot())
            Assert.Equal(0L, CLng(db.Scalar("PRAGMA user_version;")))
            Assert.Equal(5L, CLng(db.Scalar("SELECT count(*) FROM pragma_table_info('CsvImportConfig');")))
            Assert.Single(db.Backups())
        End Using
    End Sub

    <Theory>
    <InlineData("BeforeBackup")>
    <InlineData("Integrity")>
    <InlineData("ForeignKey")>
    Public Sub T56_BackupFailure_DoesNotAlterSource(kind As String)
        Using db As New Phase2Database(True)
            Dim before = db.Snapshot()
            Dim trace As New List(Of String)()
            Dim failure = Record.Exception(
                Sub() db.Initialize(Sub(stage, connection, transaction)
                                        trace.Add(stage)
                                        If kind = "BeforeBackup" AndAlso stage = "BeforeBackup" Then Throw New IOException("backup denied")
                                        If stage = "BackupCreated" AndAlso kind = "Integrity" Then
                                            DatabaseSchemaValidator.Execute(connection, "PRAGMA ignore_check_constraints=ON; UPDATE MeasurementItemMaster SET MinValue=999,MaxValue=-1; PRAGMA ignore_check_constraints=OFF;")
                                            Assert.Equal(999.0, CDbl(DatabaseSchemaValidator.Scalar(connection, "SELECT MinValue FROM MeasurementItemMaster LIMIT 1;")))
                                            Assert.NotEqual("ok", CStr(DatabaseSchemaValidator.Scalar(connection, "PRAGMA integrity_check;")))
                                            trace.Add(CStr(DatabaseSchemaValidator.Scalar(connection, "PRAGMA integrity_check;")))
                                        End If
                                        If stage = "BackupCreated" AndAlso kind = "ForeignKey" Then DatabaseSchemaValidator.Execute(connection, "PRAGMA foreign_keys=OFF; DELETE FROM EquipmentMaster WHERE EquipmentId='EQ001';")
                                    End Sub))
            Assert.True(failure IsNot Nothing, String.Join(" -> ", trace))
            If kind = "BeforeBackup" Then
                Assert.IsType(Of IOException)(failure)
            Else
                Assert.IsType(Of InvalidDataException)(failure)
            End If
            Assert.Equal(before, db.Snapshot())
            Assert.Equal(0L, CLng(db.Scalar("PRAGMA user_version;")))
            Assert.Equal(5L, CLng(db.Scalar("SELECT count(*) FROM pragma_table_info('CsvImportConfig');")))
        End Using
    End Sub

    <Theory>
    <InlineData("PRAGMA user_version=9;")>
    <InlineData("PRAGMA user_version=111;")>
    <InlineData("PRAGMA user_version=110;")>
    <InlineData("ALTER TABLE CsvImportConfig ADD COLUMN ImportMode TEXT;")>
    <InlineData("DROP INDEX IX_OperationLog_ExecutedAt;")>
    <InlineData("CREATE TABLE Alien(Id INTEGER);")>
    <InlineData("ALTER TABLE ErrorLog ADD COLUMN Extra TEXT;")>
    <InlineData("CREATE TRIGGER Unexpected AFTER INSERT ON OperationLog BEGIN DELETE FROM ErrorLog; END;")>
    Public Sub T56_UnknownOrPartialSchema_RejectsWithoutRepair(sql As String)
        Using db As New Phase2Database(True)
            db.Execute(sql)
            Dim before = db.Snapshot()
            Dim schema = CStr(db.Scalar("SELECT group_concat(sql) FROM sqlite_master;"))
            Dim version = db.Scalar("PRAGMA user_version;")
            Assert.Throws(Of InvalidDataException)(Sub() db.Initialize())
            Assert.Equal(before, db.Snapshot())
            Assert.Equal(schema, CStr(db.Scalar("SELECT group_concat(sql) FROM sqlite_master;")))
            Assert.Equal(version, db.Scalar("PRAGMA user_version;"))
            Assert.Empty(db.Backups())
        End Using
    End Sub

    <Fact>
    Public Sub T56_AllNewColumnsWithVersionZero_RefusesToGuessMigrationState()
        Using db As New Phase2Database(True)
            For Each sql In DatabaseMigrations.Statements.Take(3)
                db.Execute(sql)
            Next
            Assert.Throws(Of InvalidDataException)(Sub() db.Initialize())
            Assert.Equal(0L, CLng(db.Scalar("PRAGMA user_version;")))
            Assert.Empty(db.Backups())
        End Using
    End Sub

    <Fact>
    Public Sub T56_CorruptFile_IsNotRecreated()
        Using db As New Phase2Database()
            Dim bytes = System.Text.Encoding.UTF8.GetBytes("not a SQLite database")
            File.WriteAllBytes(db.Factory.DatabasePath, bytes)
            Assert.Throws(Of SqliteException)(Sub() db.Initialize())
            SqliteConnection.ClearAllPools()
            Assert.True(bytes.SequenceEqual(File.ReadAllBytes(db.Factory.DatabasePath)))
            Assert.Empty(db.Backups())
        End Using
    End Sub

    <Fact>
    Public Sub T56_ConcurrentUpdateAfterBackup_AbortsMigration()
        Using db As New Phase2Database(True)
            Assert.Throws(Of InvalidDataException)(
                Sub() db.Initialize(Sub(stage, connection, transaction)
                                        If stage = "BackupVerified" Then db.Execute("UPDATE EquipmentMaster SET EquipmentName='external' WHERE EquipmentId='EQ001';")
                                    End Sub))
            Assert.Equal(0L, CLng(db.Scalar("PRAGMA user_version;")))
            Assert.Equal(5L, CLng(db.Scalar("SELECT count(*) FROM pragma_table_info('CsvImportConfig');")))
            Assert.Equal("external", CStr(db.Scalar("SELECT EquipmentName FROM EquipmentMaster WHERE EquipmentId='EQ001';")))
        End Using
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub T57_NewOrEmptyDatabase_SeedsOnlyOnce(emptyFile As Boolean)
        Using db As New Phase2Database()
            If emptyFile Then
                Using connection = db.Factory.CreateConnection()
                End Using
            End If
            db.Initialize()
            Assert.Equal(110L, CLng(db.Scalar("PRAGMA user_version;")))
            Assert.Equal(7L, CLng(db.Scalar("SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';")))
            Assert.Equal(4L, CLng(db.Scalar("SELECT count(*) FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';")))
            Dim config = New CsvImportConfigRepository(db.Factory).FindByName("SCADA_A")
            Assert.Equal(ImportMode.SingleFile, config.ImportMode)
            Assert.Null(config.WatchFolderPath)
            Assert.Equal(60, config.WatchIntervalSeconds)
            db.Execute("DELETE FROM EquipmentMaster WHERE EquipmentId='EQ003'; DELETE FROM CsvColumnMapping; DELETE FROM CsvImportConfig;")
            Dim before = db.Snapshot()
            db.Initialize()
            Assert.Equal(before, db.Snapshot())
            Assert.Empty(db.Backups())
        End Using
    End Sub

    <Theory>
    <InlineData("NewSchemaCreated")>
    <InlineData("BeforeNewCommit")>
    Public Sub T57_NewInitializationFailure_RollsBackTablesSeedAndVersion(stage As String)
        Using db As New Phase2Database()
            Assert.Throws(Of IOException)(Sub() db.Initialize(Sub(point, connection, transaction)
                                                                 If point = stage Then Throw New IOException("injected")
                                                             End Sub))
            Assert.Equal(0L, CLng(db.Scalar("PRAGMA user_version;")))
            Assert.Equal(0L, CLng(db.Scalar("SELECT count(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';")))
            db.Initialize()
            Assert.Equal(110L, CLng(db.Scalar("PRAGMA user_version;")))
        End Using
    End Sub

    <Fact>
    Public Sub LegacySchema_WhitespaceChanges_AreAccepted()
        Using db As New Phase2Database()
            Using connection = db.Factory.CreateConnection()
                For Each sql In LegacyDatabaseSchema.CreateStatements
                    DatabaseSchemaValidator.Execute(connection, sql.Replace(", ", "," & vbCrLf).Replace(" (", "  ("))
                Next
            End Using
            db.Initialize()
            Assert.Equal(110L, CLng(db.Scalar("PRAGMA user_version;")))
        End Using
    End Sub

    <Theory>
    <InlineData("UNIQUE")>
    <InlineData("FK")>
    <InlineData("CHECK")>
    <InlineData("PK")>
    <InlineData("DEFAULT")>
    <InlineData("CONFLICT")>
    Public Sub T56_ModifiedLegacyConstraints_AreRejected(kind As String)
        Using db As New Phase2Database()
            Using connection = db.Factory.CreateConnection()
                For Each original In LegacyDatabaseSchema.CreateStatements
                    Dim sql = original
                    Select Case kind
                        Case "UNIQUE"
                            sql = sql.Replace("ConfigName TEXT NOT NULL UNIQUE", "ConfigName TEXT NOT NULL")
                        Case "FK"
                            sql = sql.Replace(" ON DELETE CASCADE", " ON DELETE SET NULL")
                        Case "CHECK"
                            sql = sql.Replace("CHECK (HasHeader IN (0, 1))", "CHECK (HasHeader IN (0, 1, 2))")
                        Case "PK"
                            sql = sql.Replace("EquipmentId TEXT NOT NULL PRIMARY KEY", "EquipmentId TEXT NOT NULL")
                        Case "DEFAULT"
                            sql = sql.Replace("Encoding TEXT NOT NULL DEFAULT 'UTF-8'", "Encoding TEXT NOT NULL DEFAULT 'ASCII'")
                        Case "CONFLICT"
                            sql = sql.Replace("ConfigName TEXT NOT NULL UNIQUE", "ConfigName TEXT NOT NULL UNIQUE ON CONFLICT REPLACE")
                    End Select
                    DatabaseSchemaValidator.Execute(connection, sql)
                Next
            End Using
            Assert.Throws(Of InvalidDataException)(Sub() db.Initialize())
            Assert.Equal(0L, CLng(db.Scalar("PRAGMA user_version;")))
            Assert.Empty(db.Backups())
        End Using
    End Sub

    <Fact>
    Public Sub LegacyWalDatabase_BackupIncludesCommittedWalRows()
        Using db As New Phase2Database(True)
            Using keepOpen = db.Factory.CreateConnection()
                DatabaseSchemaValidator.Execute(keepOpen, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO OperationLog(OperationType) VALUES('WAL retained');")
                db.Initialize()
                Dim backupPath = Assert.Single(db.Backups())
                Using backup As New SqliteConnection($"Data Source={backupPath};Pooling=False")
                    backup.Open()
                    Assert.Equal(1L, CLng(DatabaseSchemaValidator.Scalar(backup, "SELECT count(*) FROM OperationLog WHERE OperationType='WAL retained';")))
                End Using
            End Using
        End Using
    End Sub
End Class
