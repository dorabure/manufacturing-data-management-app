Imports System.IO
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Presentation
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase6ClosingIntegrationTests
    <Theory>
    <InlineData("Atomic")>
    <InlineData("Validation")>
    <InlineData("Transaction")>
    <InlineData("Journal")>
    <InlineData("Move")>
    <InlineData("Cleanup")>
    Public Sub CloseDuringRealPipelineCompletesAOnlyReleasesOwnerAndCanRestart(stage As String)
        RunUiAsync(Async Function()
                       Using f As New Phase4Fixture()
                           f.Write("A.csv")
                           f.Write("B.csv", 1)
                           Dim entered = Signal6()
                           Dim release = Signal6()
                           Dim gate As Action = Sub()
                                                    entered.TrySetResult(True)
                                                    release.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult()
                                                End Sub
                           Dim data As New Phase6DataRepository(New MeasurementDataRepository(f.Database.Factory))
                           Dim executor As New Phase4Executor(New PeriodicCsvImportExecutor(f.Database.Factory, data:=data))
                           Dim service As PeriodicImportService = Nothing
                           Select Case stage
                               Case "Atomic"
                                   executor.Before = Sub(request) gate()
                               Case "Validation"
                                   data.ValidationGate = gate
                               Case "Transaction"
                                   data.TransactionGate = gate
                               Case "Journal"
                                   f.Journal.BeforeAttempt = Sub(attempt)
                                                                 If attempt.Stage = ProcessingStage.ResultKnown Then gate()
                                                             End Sub
                               Case "Move"
                                   f.Files.BeforeMove = Sub(root, source, destination) gate()
                               Case "Cleanup"
                                   executor.After = Sub(result)
                                                        Dim stopping = service.StopAsync()
                                                    End Sub
                           End Select
                           f.Cycle = New PeriodicImportCycle(f.Files, f.Journal, executor, f.Clock)
                           Dim wrapper As New Phase6Cycle With {
                               .Preparation = Function(token) f.Cycle.PrepareAsync(f.Options, token),
                               .Execution = Function(token) f.Cycle.ExecuteAsync(token),
                               .Cleanup = Async Function()
                                              If stage = "Cleanup" Then
                                                  entered.TrySetResult(True)
                                                  Await release.Task
                                              End If
                                              Await f.Cycle.CleanupAsync()
                                          End Function}
                           Dim presenter As New PeriodicImportStatusPresenter()
                           service = New PeriodicImportService(wrapper, f.Clock, presenter)
                           Using form = CreateMain(f.Database, Phase6MainFormTests.Compose(f.Database, service, presenter))
                               Dim handle = form.Handle
                               Dim closed = Signal6()
                               Dim cancellations As New List(Of Boolean)()
                               AddHandler form.FormClosing, Sub(sender, e) cancellations.Add(e.Cancel)
                               AddHandler form.FormClosed, Sub() closed.TrySetResult(True)
                               Await service.StartAsync(f.Options)
                               Try
                                   Await entered.Task.WaitAsync(TimeSpan.FromSeconds(10))
                                   Assert.Throws(Of IOException)(Function() f.Files.AcquireOwner(f.Root))
                                   form.Close()
                                   Assert.True(Assert.Single(cancellations))
                                   Assert.False(form.IsDisposed)
                                   Assert.False(closed.Task.IsCompleted)
                                   Assert.Equal(PeriodicImportState.Stopping, service.Status.State)
                                   If stage = "Transaction" Then Assert.Equal(0, f.DataCount())
                                   Assert.True(File.Exists(Path.Combine(f.Root, "B.csv")))
                                   Assert.Single(executor.Calls)
                                   release.SetResult(True)
                                   Await closed.Task.WaitAsync(TimeSpan.FromSeconds(10))
                                   Assert.Equal(New Boolean() {True, False}, cancellations)
                                   Assert.True(service.Status.CleanupCompleted)
                                   Assert.Equal(1, wrapper.CleanupCount)
                                   Assert.Equal(1, f.DataCount())
                                   Assert.Equal(New String() {"A.csv"}, executor.Calls)
                                   Dim attempt = Assert.Single(f.State().Attempts)
                                   Assert.Equal(ProcessingStage.Completed, attempt.Stage)
                                   Assert.True(File.Exists(Path.Combine(f.Root, attempt.Destination)))
                                   Assert.False(File.Exists(Path.Combine(f.Root, "A.csv")))
                                   Assert.True(File.Exists(Path.Combine(f.Root, "B.csv")))
                                   Assert.Equal(1, Assert.Single(f.State().Orders).Cursor)
                                   Using owner = f.Files.AcquireOwner(f.Root)
                                       Assert.NotNull(owner)
                                   End Using
                               Finally
                                   release.TrySetResult(True)
                               End Try
                           End Using
                           executor.Before = Nothing
                           executor.After = Nothing
                           data.ValidationGate = Nothing
                           data.TransactionGate = Nothing
                           f.Journal.BeforeAttempt = Nothing
                           f.Files.BeforeMove = Nothing
                           ' New session on the exact same root resumes B, never imports A twice.
                           Dim restarted As New PeriodicImportService(New PeriodicImportCycle(f.Files, f.Journal, executor), f.Clock)
                           Await restarted.StartAsync(f.Options)
                           Await restarted.CurrentCycleCompletion
                           Await restarted.StopAsync()
                           Assert.Equal(New String() {"A.csv", "B.csv"}, executor.Calls)
                           Assert.Equal(2, f.DataCount())
                           Assert.True(restarted.Status.CleanupCompleted)
                       End Using
                   End Function)
    End Sub

    <Fact>
    Public Sub WritingHeadClosePreservesBothFilesAndDoesNotImport()
        RunUiAsync(Async Function()
                       Using f As New Phase4Fixture()
                           Dim first = f.Write("A.csv")
                           Dim second = f.Write("B.csv", 1)
                           Using writer As New FileStream(first, FileMode.Open, FileAccess.Write, FileShare.Read)
                               Dim presenter As New PeriodicImportStatusPresenter()
                               Dim service As New PeriodicImportService(f.Cycle, f.Clock, presenter)
                               Using form = CreateMain(f.Database, Phase6MainFormTests.Compose(f.Database, service, presenter))
                                   Dim handle = form.Handle
                                   Dim closed = Signal6()
                                   AddHandler form.FormClosed, Sub() closed.TrySetResult(True)
                                   Await service.StartAsync(f.Options)
                                   Await service.CurrentCycleCompletion
                                   Assert.Equal(HeadDisposition.WaitingForReadable, Assert.Single(f.State().Orders).HeadDisposition)
                                   form.Close()
                                   Await closed.Task.WaitAsync(TimeSpan.FromSeconds(10))
                                   Assert.Empty(f.Executor.Calls)
                                   Assert.Empty(f.State().Attempts)
                                   Assert.Equal(0, f.DataCount())
                                   Assert.True(File.Exists(first))
                                   Assert.True(File.Exists(second))
                                   Assert.Empty(Directory.GetFiles(Path.Combine(f.Root, "error")))
                                   Using owner = f.Files.AcquireOwner(f.Root)
                                       Assert.NotNull(owner)
                                   End Using
                               End Using
                           End Using
                       End Using
                   End Function)
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub PendingStoppedCloseNeverRunsCorrectionOrRecovery(unknown As Boolean)
        RunUiAsync(Async Function()
                       Using f As New Phase4Fixture()
                           f.Write("A.csv", value:="bad")
                           f.Write("B.csv", 1)
                           If unknown Then f.Executor.OverrideResult = Function(request) New PeriodicFileResultDto()
                           Dim presenter As New PeriodicImportStatusPresenter()
                           Dim service As New PeriodicImportService(f.Cycle, f.Clock, presenter)
                           Await service.StartAsync(f.Options)
                           Await service.Completion
                           Dim disposition = If(unknown, HeadDisposition.RecoveryRequired, HeadDisposition.CorrectionPending)
                           Assert.Equal(disposition, Assert.Single(f.State().Orders).HeadDisposition)
                           Using form = CreateMain(f.Database, Phase6MainFormTests.Compose(f.Database, service, presenter))
                               Dim handle = form.Handle
                               Dim before = f.Database.Snapshot()
                               Dim saved = Directory.GetFiles(Path.Combine(f.Root, ".periodic-import"), "*.json", SearchOption.AllDirectories).
                                   ToDictionary(Function(path) path, Function(path) File.ReadAllText(path))
                               Dim closings = 0
                               AddHandler form.FormClosing, Sub(sender, e)
                                                                closings += 1
                                                                Assert.False(e.Cancel)
                                                            End Sub
                               form.Close()
                               Assert.True(form.IsDisposed)
                               Assert.Equal(1, closings)
                               Assert.Equal(before, f.Database.Snapshot())
                               Assert.NotEmpty(saved)
                               For Each pair In saved
                                   Assert.Equal(pair.Value, File.ReadAllText(pair.Key))
                               Next
                               Assert.Single(f.Executor.Calls)
                               Assert.Empty(f.State().Bindings)
                               Assert.Equal(disposition, Assert.Single(f.State().Orders).HeadDisposition)
                           End Using
                       End Using
                   End Function)
    End Sub
End Class
