Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Tests.TestSupport
Imports System.IO
Imports Xunit

Public Class MonitoringConfigPersistenceTests
    <Fact>
    Public Sub T43_T60_SettingsSurviveReopen_AreIndependent_AndSingleFileKeepsValues()
        Using db As New Phase2Database()
            db.Initialize()
            Dim repo As New CsvImportConfigRepository(db.Factory)
            Dim configB As New CsvImportConfig With {.ConfigName = "PLC_B"}
            repo.Save(configB, Array.Empty(Of CsvColumnMapping)())
            Dim service As New CsvConfigService(repo)
            service.SaveMonitoringSettings(1, ImportMode.PeriodicFolder, "  C:\InputA  ", 60)
            service.SaveMonitoringSettings(configB.ConfigId, ImportMode.SingleFile, "C:\PLC_B", 300)
            Dim reopened As New CsvConfigService(New CsvImportConfigRepository(db.Factory))
            Dim a = reopened.GetById(1)
            Assert.Equal(ImportMode.PeriodicFolder, a.ImportMode)
            Assert.Equal("C:\InputA", a.WatchFolderPath)
            Assert.Equal(60, a.WatchIntervalSeconds)
            reopened.SaveMonitoringSettings(1, ImportMode.SingleFile, a.WatchFolderPath, a.WatchIntervalSeconds)
            Dim b = reopened.GetById(configB.ConfigId)
            Assert.Equal("C:\PLC_B", b.WatchFolderPath)
            Assert.Equal(300, b.WatchIntervalSeconds)
            Assert.Equal(ImportMode.SingleFile, b.ImportMode)
            Assert.Equal("C:\InputA", reopened.GetById(1).WatchFolderPath)
            Assert.Equal(60, reopened.GetById(1).WatchIntervalSeconds)
            Assert.Equal("SingleFile", CStr(db.Scalar("SELECT ImportMode FROM CsvImportConfig WHERE ConfigId=1;")))
            Assert.Equal(2, reopened.GetAll().Count)
        End Using
    End Sub

    <Fact>
    Public Sub T58_FormatSaveUsingFreshEntity_PreservesMonitoring_AndMonitoringPreservesMappingIds()
        Using db As New Phase2Database()
            db.Initialize()
            Dim repo As New CsvImportConfigRepository(db.Factory)
            Dim service As New CsvConfigService(repo)
            service.SaveMonitoringSettings(1, ImportMode.PeriodicFolder, "C:\Input", 120)
            Dim fresh As New CsvImportConfig With {.ConfigId = 1, .ConfigName = "Renamed", .Encoding = "Shift_JIS", .Delimiter = ";", .HasHeader = False}
            Dim mappings = repo.GetMappings(1).ToList()
            For Each mapping In mappings
                mapping.CsvColumnIndex += 1
            Next
            service.Save(fresh, mappings)
            Dim actual = repo.FindByName("Renamed")
            Assert.Equal(ImportMode.PeriodicFolder, actual.ImportMode)
            Assert.Equal("C:\Input", actual.WatchFolderPath)
            Assert.Equal(120, actual.WatchIntervalSeconds)
            Assert.Equal("Shift_JIS", actual.Encoding)
            Assert.Equal(";", actual.Delimiter)
            Assert.False(actual.HasHeader)
            Assert.Equal(2, repo.GetMappings(1).Min(Function(m) m.CsvColumnIndex))
            Dim before = db.Snapshot()
            service.SaveMonitoringSettings(1, ImportMode.SingleFile, actual.WatchFolderPath, 300)
            Assert.Equal(before, db.Snapshot())
            Assert.Equal(300, repo.FindById(1).WatchIntervalSeconds)
        End Using
    End Sub

    <Fact>
    Public Sub T58_NewConfig_UsesDatabaseDefaults_NotEntityMonitoringValues()
        Using db As New Phase2Database()
            db.Initialize()
            Dim config As New CsvImportConfig With {.ConfigName = "New", .ImportMode = ImportMode.PeriodicFolder, .WatchFolderPath = "C:\ignored", .WatchIntervalSeconds = 10}
            Dim repo As New CsvImportConfigRepository(db.Factory)
            repo.Save(config, Array.Empty(Of CsvColumnMapping)())
            Dim actual = repo.FindById(config.ConfigId)
            Assert.Equal(ImportMode.SingleFile, actual.ImportMode)
            Assert.Null(actual.WatchFolderPath)
            Assert.Equal(60, actual.WatchIntervalSeconds)
        End Using
    End Sub

    <Theory>
    <InlineData(0, 0, 60)>
    <InlineData(1, 99, 60)>
    <InlineData(1, 0, 0)>
    <InlineData(1, 0, -1)>
    <InlineData(1, 0, 604801)>
    Public Sub T59_InvalidApplicationAndRepositoryValues_AreRejected(id As Integer, mode As Integer, seconds As Integer)
        Using db As New Phase2Database()
            db.Initialize()
            Dim repo As New CsvImportConfigRepository(db.Factory)
            Dim service As New CsvConfigService(repo)
            Assert.Throws(Of ArgumentOutOfRangeException)(Sub() service.SaveMonitoringSettings(id, CType(mode, ImportMode), Nothing, seconds))
            Assert.Throws(Of ArgumentOutOfRangeException)(Sub() repo.SaveMonitoringSettings(id, CType(mode, ImportMode), Nothing, seconds))
            Assert.Equal(60, repo.FindById(1).WatchIntervalSeconds)
        End Using
    End Sub

    <Theory>
    <InlineData("WatchIntervalSeconds=NULL")>
    <InlineData("WatchIntervalSeconds=0")>
    <InlineData("WatchIntervalSeconds=604801")>
    <InlineData("WatchIntervalSeconds=1.5")>
    <InlineData("WatchIntervalSeconds='text'")>
    <InlineData("ImportMode='Unknown'")>
    <InlineData("ImportMode=NULL")>
    Public Sub T59_DatabaseConstraints_RejectInvalidStorage(setClause As String)
        For Each legacy In {False, True}
            Using db As New Phase2Database(legacy)
                db.Initialize()
                Assert.Throws(Of SqliteException)(Sub() db.Execute("UPDATE CsvImportConfig SET " & setClause & " WHERE ConfigId=1;"))
            End Using
        Next
    End Sub

    <Theory>
    <InlineData("ImportMode='Unknown'")>
    <InlineData("WatchIntervalSeconds=0")>
    <InlineData("WatchIntervalSeconds=604801")>
    <InlineData("WatchIntervalSeconds=1.5")>
    <InlineData("WatchIntervalSeconds='abc'")>
    <InlineData("WatchFolderPath=x'0102'")>
    Public Sub T59_CorruptStoredValues_AreNotSilentlyCoerced(setClause As String)
        Using db As New Phase2Database()
            db.Initialize()
            db.Execute("PRAGMA ignore_check_constraints=ON; UPDATE CsvImportConfig SET " & setClause & " WHERE ConfigId=1;")
            Dim repo As New CsvImportConfigRepository(db.Factory)
            Assert.Throws(Of InvalidDataException)(Sub() repo.FindById(1))
            Assert.Throws(Of InvalidDataException)(Sub() repo.FindByName("SCADA_A"))
            Assert.Throws(Of InvalidDataException)(Sub() repo.GetAll())
        End Using
    End Sub

    <Theory>
    <InlineData(1)>
    <InlineData(604800)>
    Public Sub T59_BoundariesAndUnsetFolder_AreAllowedWithoutFilesystemAccess(seconds As Integer)
        Using db As New Phase2Database()
            db.Initialize()
            Dim service As New CsvConfigService(New CsvImportConfigRepository(db.Factory))
            service.SaveMonitoringSettings(1, ImportMode.PeriodicFolder, "   ", seconds)
            Assert.Null(service.GetById(1).WatchFolderPath)
            Assert.Equal(seconds, service.GetById(1).WatchIntervalSeconds)
            service.SaveMonitoringSettings(1, ImportMode.PeriodicFolder, "  nonexistent\folder  ", seconds)
            Assert.Equal(Path.GetFullPath("nonexistent\folder"), service.GetById(1).WatchFolderPath)
        End Using
    End Sub

    <Fact>
    Public Sub SaveMonitoringSettings_MissingConfig_ReportsExplicitFailure()
        Using db As New Phase2Database()
            db.Initialize()
            Dim service As New CsvConfigService(New CsvImportConfigRepository(db.Factory))
            Assert.Throws(Of InvalidOperationException)(Sub() service.SaveMonitoringSettings(999, ImportMode.SingleFile, Nothing, 60))
        End Using
    End Sub
End Class
