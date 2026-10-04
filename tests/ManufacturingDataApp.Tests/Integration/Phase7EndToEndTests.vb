Imports System.IO
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Infrastructure.Logging
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Presentation
Imports ManufacturingDataApp.Tests.TestSupport
Imports Microsoft.Data.Sqlite
Imports Xunit

Public Class Phase7EndToEndTests
    <Fact>
    Public Sub MainFormSettingsStartThreeFilesLogsSearchThenClose()
        RunUiAsync(Async Function()
                       Using f As New Phase4Fixture()
                           For i = 0 To 2
                               Write7(f.Root, ChrW(65 + i) & ".csv", i)
                           Next
                           Dim entered = Signal6()
                           Dim probe As New Phase7Probe(f.Database) With {.Before = Sub() entered.TrySetResult(True)}
                           Dim presenter As New PeriodicImportStatusPresenter()
                           Dim scheduler As New PeriodicImportService(New PeriodicImportCycle(f.Files, f.Journal, probe, sink:=presenter), sink:=presenter, log:=New DbLogWriter(f.Database.Factory))
                           Using form = CreateMain(f.Database, Phase6MainFormTests.Compose(f.Database, scheduler, presenter))
                               Dim handle = form.Handle
                               Field(Of DataGridView)(form, "_grid").CreateControl()
                               Field(Of RadioButton)(form, "_periodicMode").Checked = True
                               Field(Of TextBox)(form, "_watchFolder").Text = f.Root
                               Field(Of NumericUpDown)(form, "_watchInterval").Value = 60
                               Dim failure As Exception = Nothing
                               Try
                               Invoke(form, "StartStopMonitoring", Nothing, EventArgs.Empty)
                               Await entered.Task
                               Await scheduler.CurrentCycleCompletion
                               Assert.Equal(PeriodicImportState.Running, scheduler.Status.State)
                               Assert.Equal(New String() {"A.csv", "B.csv", "C.csv"}, probe.Calls)
                               Assert.Equal(3L, f.DataCount())
                               Assert.Equal(3, f.State().Attempts.Count)
                               Assert.All(f.State().Attempts, Sub(a) Assert.Equal(ProcessingStage.Completed, a.Stage))
                               Assert.Equal(3, Assert.Single(f.State().Orders).Cursor)
                               Assert.Equal(3, Directory.GetFiles(Path.Combine(f.Root, "success")).Length)
                               Assert.Equal(3L, CLng(f.Database.Scalar("SELECT SUM(SuccessCount) FROM OperationLog WHERE OperationType='周期CSV取込'")))
                               Assert.Equal(1L, CLng(f.Database.Scalar("SELECT COUNT(*) FROM OperationLog WHERE OperationType='周期監視開始'")))
                               Field(Of DateTimePicker)(form, "_fromPicker").Checked = False
                               Field(Of DateTimePicker)(form, "_toPicker").Checked = False
                               Invoke(form, "Search")
                               Assert.Equal(3, Field(Of IReadOnlyList(Of ManufacturingDataApp.Domain.Entities.MeasurementData))(form, "_currentResults").Count)
                               Assert.Equal(3, Field(Of DataGridView)(form, "_grid").Rows.Count)
                               Field(Of TextBox)(form, "_equipmentIdTextBox").Text = "missing"
                               Invoke(form, "Search")
                               Assert.Empty(Field(Of IReadOnlyList(Of ManufacturingDataApp.Domain.Entities.MeasurementData))(form, "_currentResults"))
                               Invoke(form, "ClearConditions")
                               Assert.Equal(3, Field(Of DataGridView)(form, "_grid").Rows.Count)
                               Dim closed = Signal6()
                               AddHandler form.FormClosed, Sub() closed.TrySetResult(True)
                               form.Close()
                               Await closed.Task
                               Assert.Equal(1L, CLng(f.Database.Scalar("SELECT COUNT(*) FROM OperationLog WHERE OperationType='周期監視停止'")))
                               Catch ex As Exception
                                   failure = ex
                               End Try
                               Await scheduler.StopAsync()
                               If failure IsNot Nothing Then System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw()
                           End Using
                       End Using
                   End Function)
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Async Function SchedulerCorrectionBindingPreservesSnapshotFingerprintAndChain(rejectAgain As Boolean) As Task
        Using f As New Phase4Fixture()
            Write7(f.Root, "A.csv", 0)
            Write7(f.Root, "B.csv", 1, value:="bad")
            Write7(f.Root, "C.csv", 2)
            Dim service As New PeriodicImportService(f.Cycle, f.Clock)
            Await service.StartAsync(f.Options)
            Await service.Completion
            Assert.Equal(HeadDisposition.CorrectionPending, Assert.Single(f.State().Orders).HeadDisposition)
            Dim failed = f.State().Attempts.Single(Function(a) a.OriginalFileName = "B.csv")
            Dim candidate = Write7(f.Root, "B2.csv", 1, value:=If(rejectAgain, "bad", "20"))
            File.SetLastWriteTimeUtc(candidate, DateTime.UtcNow.AddYears(1))
            Dim recovery As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() service.Status.State = PeriodicImportState.Stopped)
            Dim preview = recovery.PreviewCandidate(f.Options, "B2.csv")
            Dim binding = recovery.ConfirmCorrection(f.Options, failed.AttemptId, "B2.csv", preview)
            Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
            Assert.Equal(2, f.Executor.Calls.Count)
            Assert.True(preview.Matches(binding.Candidate))
            Assert.Equal(failed.ConfigHash, binding.ConfigHash)
            Await service.StartAsync(f.Options)
            Await service.CurrentCycleCompletion
            Await service.StopAsync()
            Dim corrected = f.State().Attempts.Single(Function(a) a.OriginalFileName = "B2.csv")
            Assert.Equal(failed.OrderId, corrected.OrderId)
            Assert.Equal(failed.Position, corrected.Position)
            Assert.Equal(failed.ConfigHash, corrected.ConfigHash)
            Assert.Equal(failed.ConfigHash, PeriodicSnapshot.Hash(corrected.Snapshot))
            Assert.Equal(failed.AttemptId, corrected.RootFailedAttemptId)
            Assert.Equal(failed.AttemptId, corrected.LatestFailedAttemptId)
            Assert.Equal(binding.CorrectionBindingId, corrected.CorrectionBindingId)
            Assert.True(preview.Matches(corrected.Fingerprint))
            Assert.Equal(corrected.AttemptId, Assert.Single(f.State().Bindings).UsedAttemptId)
            Assert.Equal(ProcessingStage.Completed, corrected.Stage)
            If rejectAgain Then
                Assert.Equal(New String() {"A.csv", "B.csv", "B2.csv"}, f.Executor.Calls)
                Assert.Equal(1L, f.DataCount())
                Assert.Equal(HeadDisposition.CorrectionPending, Assert.Single(f.State().Orders).HeadDisposition)
                Assert.Equal(corrected.AttemptId, Assert.Single(f.State().Orders).LatestFailedAttemptId)
                Assert.True(File.Exists(Path.Combine(f.Root, "error/B2.csv")))
                Assert.True(File.Exists(Path.Combine(f.Root, "C.csv")))
            Else
                Assert.Equal(New String() {"A.csv", "B.csv", "B2.csv", "C.csv"}, f.Executor.Calls)
                Assert.Equal(3L, f.DataCount())
                Assert.True(File.Exists(Path.Combine(f.Root, "success/B2.csv")))
                Assert.True(File.Exists(Path.Combine(f.Root, "success/C.csv")))
            End If
        End Using
    End Function

    <Fact>
    Public Async Function TwoSchedulersRespectActualOwnerLease() As Task
        Using f As New Phase4Fixture()
            Dim a As New PeriodicImportService(f.Cycle)
            Dim bProbe As New Phase7Probe(f.Database)
            Dim b As New PeriodicImportService(New PeriodicImportCycle(f.Files, f.Journal, bProbe))
            Await a.StartAsync(f.Options)
            Await a.CurrentCycleCompletion
            Await Assert.ThrowsAsync(Of PeriodicImportStopRequiredException)(Function() b.StartAsync(f.Options))
            Await b.Completion
            Assert.Empty(bProbe.Calls)
            Assert.Equal(PeriodicImportState.Running, a.Status.State)
            Await a.StopAsync()
            Write7(f.Root, "A.csv", 0)
            Await b.StartAsync(f.Options)
            Await b.CurrentCycleCompletion
            Await b.StopAsync()
            Assert.Single(bProbe.Calls)
            Assert.Equal(1L, f.DataCount())
        End Using
    End Function

    <Theory>
    <InlineData("connection")>
    <InlineData("locked")>
    <InlineData("search")>
    <InlineData("equipment")>
    <InlineData("add")>
    <InlineData("commit-unknown")>
    <InlineData("unique")>
    <InlineData("fk")>
    Public Async Function FatalRepositoryBoundaryNeverMovesToErrorOrRetries(kind As String) As Task
        Using f As New Phase4Fixture()
            Write7(f.Root, "A.csv", 0)
            Write7(f.Root, "B.csv", 1)
            Dim probe As New Phase7Probe(f.Database)
            Select Case kind
                Case "connection" : probe.SearchFailure = New SqliteException("injected cannot open", 14)
                Case "locked" : probe.BeforeDb = Sub() Throw New SqliteException("injected database locked", 5)
                Case "search" : probe.SearchFailure = New IOException("search")
                Case "equipment" : probe.EquipmentFailure = New IOException("equipment")
                Case "add" : probe.BeforeDb = Sub() Throw New IOException("before AddRange")
                Case "commit-unknown" : probe.AfterDb = Sub() Throw New IOException("after actual commit, before outcome")
                Case "unique" : probe.BeforeDb = Sub() f.Database.Execute("INSERT INTO MeasurementData(MeasuredAt,EquipmentId,EquipmentName,ItemName,Value,Unit) VALUES('2026-01-01T00:00:00.0000000Z','EQ001','競合','温度',10,'℃')")
                Case "fk" : probe.BeforeDb = Sub() f.Database.Execute("DELETE FROM EquipmentMaster WHERE EquipmentId='EQ001'")
            End Select
            Dim service As New PeriodicImportService(New PeriodicImportCycle(f.Files, f.Journal, probe))
            Await service.StartAsync(f.Options)
            Await service.Completion
            Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
            Assert.Equal(HeadDisposition.RecoveryRequired, Assert.Single(f.State().Orders).HeadDisposition)
            Assert.Equal(ProcessingStage.RecoveryRequired, Assert.Single(f.State().Attempts).Stage)
            Assert.True(File.Exists(Path.Combine(f.Root, "A.csv")))
            Assert.True(File.Exists(Path.Combine(f.Root, "B.csv")))
            Assert.Empty(Directory.GetFiles(Path.Combine(f.Root, "error")))
            Assert.Equal(If(kind = "commit-unknown" OrElse kind = "unique", 1L, 0L), f.DataCount())
            Dim restartedProbe As New Phase7Probe(f.Database)
            Dim restarted As New PeriodicImportService(New PeriodicImportCycle(f.Files, f.Journal, restartedProbe))
            Await Assert.ThrowsAsync(Of PeriodicImportStopRequiredException)(Function() restarted.StartAsync(f.Options))
            Await restarted.Completion
            Assert.Empty(restartedProbe.Calls)
            Assert.Single(probe.Calls)
        End Using
    End Function

    <Fact>
    Public Sub LegacyMigrationThroughMainFormStartPreservesOriginalData()
        RunUiAsync(Async Function()
                       Using db As New Phase2Database(True)
                           Dim before = db.Snapshot()
                           db.Initialize()
                           Assert.Equal(before, db.Snapshot())
                           Assert.Single(db.Backups())
                           db.Initialize()
                           Assert.Equal(before, db.Snapshot())
                           Dim root = Path.Combine(db.DirectoryPath, "watch")
                           Directory.CreateDirectory(root)
                           File.WriteAllText(Path.Combine(root, "new.csv"), Row7(10).Replace(",", ";"))
                           Dim entered = Signal6()
                           Dim probe As New Phase7Probe(db) With {.Before = Sub() entered.TrySetResult(True)}
                           Dim presenter As New PeriodicImportStatusPresenter()
                           Dim service As New PeriodicImportService(New PeriodicImportCycle(New PeriodicImportFileStore(), New JsonImportProcessingJournal(), probe))
                           Using form = CreateMain(db, Phase6MainFormTests.Compose(db, service, presenter))
                               Dim handle = form.Handle
                               Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
                               Field(Of RadioButton)(form, "_periodicMode").Checked = True
                               Field(Of TextBox)(form, "_watchFolder").Text = root
                               Invoke(form, "StartStopMonitoring", Nothing, EventArgs.Empty)
                               Await entered.Task
                               Await service.CurrentCycleCompletion
                               Assert.Null(service.Status.LastError)
                               Assert.Single(probe.Calls)
                               Assert.Equal(2L, CLng(db.Scalar("SELECT COUNT(*) FROM MeasurementData")))
                               Assert.Equal("保持設備", CStr(db.Scalar("SELECT EquipmentName FROM MeasurementData WHERE Id=71")))
                               Assert.Equal("保持エラー", CStr(db.Scalar("SELECT ErrorMessage FROM ErrorLog WHERE ErrorId=91")))
                               Assert.Equal("旧ログ", CStr(db.Scalar("SELECT OperationType FROM OperationLog WHERE LogId=81")))
                               Assert.Equal(0L, CLng(db.Scalar("SELECT COUNT(*) FROM EquipmentMaster WHERE EquipmentId='EQ003'")))
                               Dim saved = New CsvImportConfigRepository(db.Factory).FindById(1)
                               Assert.Equal(ImportMode.PeriodicFolder, saved.ImportMode)
                               Assert.Equal(";", saved.Delimiter)
                               Assert.False(saved.HasHeader)
                               Dim closed = Signal6()
                               AddHandler form.FormClosed, Sub() closed.TrySetResult(True)
                               form.Close()
                               Await closed.Task
                           End Using
                       End Using
                   End Function)
    End Sub
End Class
