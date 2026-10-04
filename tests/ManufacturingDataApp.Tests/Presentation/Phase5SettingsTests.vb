Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Presentation
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase5SettingsTests
    <Theory>
    <InlineData(DirtySettingsChoice.Save)>
    <InlineData(DirtySettingsChoice.Discard)>
    <InlineData(DirtySettingsChoice.Cancel)>
    Public Sub DirtySwitchPreservesOriginalTarget(choice As DirtySettingsChoice)
        Using db As New Phase2Database()
            db.Initialize()
            db.Execute("INSERT INTO CsvImportConfig(ConfigName,Encoding,Delimiter,HasHeader) VALUES('Second','UTF-8',',',1)")
            Dim repo As New Phase5ConfigRepository(New CsvImportConfigRepository(db.Factory))
            Dim model As New MonitoringSettingsController(New CsvConfigService(repo))
            Dim ids = repo.GetAll().Select(Function(c) c.ConfigId).ToArray()
            model.Load(ids(0))
            model.Mode = ImportMode.PeriodicFolder
            model.Folder = db.DirectoryPath
            model.IntervalSeconds = 123
            Assert.True(model.IsDirty)
            Dim switched = model.SwitchConfig(ids(1), choice)
            Assert.Equal(choice <> DirtySettingsChoice.Cancel, switched)
            Assert.Equal(If(switched, ids(1), ids(0)), model.ConfigId)
            Assert.Equal(If(choice = DirtySettingsChoice.Save, 123, 60), repo.FindById(ids(0)).WatchIntervalSeconds)
            Assert.Equal(60, repo.FindById(ids(1)).WatchIntervalSeconds)
            If choice = DirtySettingsChoice.Cancel Then
                Assert.Equal(123, model.IntervalSeconds)
                Assert.Equal(db.DirectoryPath, model.Folder)
                Assert.True(model.IsDirty)
            End If
        End Using
    End Sub

    <Fact>
    Public Sub SaveFailurePreventsSwitchAndPreservesInput()
        Using db As New Phase2Database()
            db.Initialize()
            Dim repo As New Phase5ConfigRepository(New CsvImportConfigRepository(db.Factory)) With {.FailSave = True}
            Dim model As New MonitoringSettingsController(New CsvConfigService(repo))
            Dim id = repo.GetAll()(0).ConfigId
            model.Load(id)
            model.IntervalSeconds = 321
            Assert.Throws(Of InvalidOperationException)(Function() model.SwitchConfig(999, DirtySettingsChoice.Save))
            Assert.Equal(id, model.ConfigId)
            Assert.Equal(321, model.IntervalSeconds)
            Assert.DoesNotContain("Load:999", repo.Calls)
            Assert.Equal(60, repo.FindById(id).WatchIntervalSeconds)
        End Using
    End Sub

    <Fact>
    Public Sub SaveReloadMappingsOrderAndFormatIndependence()
        Using db As New Phase2Database()
            db.Initialize()
            Dim snapshot = db.Snapshot()
            Dim repo As New Phase5ConfigRepository(New CsvImportConfigRepository(db.Factory))
            Dim model As New MonitoringSettingsController(New CsvConfigService(repo))
            Dim id = repo.GetAll()(0).ConfigId
            model.Load(id)
            Assert.Equal("", model.Folder)
            model.Mode = ImportMode.PeriodicFolder
            model.Folder = db.DirectoryPath
            model.IntervalSeconds = 604800
            repo.Calls.Clear()
            Dim options = model.SaveAndCreateStartOptions(db.Factory.DatabasePath)
            Assert.Equal(New String() {$"Save:{id}", $"Load:{id}", $"Load:{id}", $"Mappings:{id}"}, repo.Calls)
            Assert.Equal(604800, options.WatchIntervalSeconds)
            Assert.Equal(db.Factory.DatabasePath, options.DatabaseIdentity)
            Assert.Equal(6, options.ColumnMappings.Count)
            Assert.False(model.IsDirty)
            Assert.Equal(snapshot, db.Snapshot())
            Dim recreated As New MonitoringSettingsController(New CsvConfigService(repo))
            recreated.Load(id)
            Assert.Equal(ImportMode.PeriodicFolder, recreated.Mode)
            Assert.Equal(db.DirectoryPath, recreated.Folder)
            Assert.Equal(604800, recreated.IntervalSeconds)
            model.Mode = ImportMode.SingleFile
            model.Save()
            Assert.Equal(db.DirectoryPath, model.Folder)
            Assert.Equal(604800, model.IntervalSeconds)
        End Using
    End Sub

    <Theory>
    <InlineData(0)>
    <InlineData(604801)>
    Public Sub InvalidIntervalRejectedBeforeSave(value As Integer)
        Using db As New Phase2Database()
            db.Initialize()
            Dim repo As New Phase5ConfigRepository(New CsvImportConfigRepository(db.Factory))
            Dim model As New MonitoringSettingsController(New CsvConfigService(repo))
            model.Load(repo.GetAll()(0).ConfigId)
            model.Mode = ImportMode.PeriodicFolder
            model.Folder = db.DirectoryPath
            model.IntervalSeconds = value
            repo.Calls.Clear()
            Assert.Throws(Of ArgumentException)(Function() model.SaveAndCreateStartOptions(db.Factory.DatabasePath))
            Assert.Empty(repo.Calls)
        End Using
    End Sub

    <Fact>
    Public Sub SaveFailurePreventsOptionsAndSchedulerStart()
        Using db As New Phase2Database()
            db.Initialize()
            Dim repo As New Phase5ConfigRepository(New CsvImportConfigRepository(db.Factory)) With {.FailSave = True}
            Dim model As New MonitoringSettingsController(New CsvConfigService(repo))
            model.Load(repo.GetAll()(0).ConfigId)
            model.Mode = ImportMode.PeriodicFolder
            model.Folder = db.DirectoryPath
            repo.Calls.Clear()
            Assert.Throws(Of InvalidOperationException)(Function() model.SaveAndCreateStartOptions(db.Factory.DatabasePath))
            Assert.StartsWith("Save:", Assert.Single(repo.Calls))
            Assert.True(model.IsDirty)
        End Using
    End Sub

    <Fact>
    Public Sub MissingConfigOrSingleModeCannotStart()
        Using db As New Phase2Database()
            db.Initialize()
            Dim model As New MonitoringSettingsController(New CsvConfigService(New CsvImportConfigRepository(db.Factory)))
            model.Mode = ImportMode.PeriodicFolder
            Assert.Throws(Of InvalidOperationException)(Function() model.SaveAndCreateStartOptions(db.Factory.DatabasePath))
            model.Load(1)
            Assert.Throws(Of InvalidOperationException)(Function() model.SaveAndCreateStartOptions(db.Factory.DatabasePath))
        End Using
    End Sub
End Class
