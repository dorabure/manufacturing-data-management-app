Imports System.Threading
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Presentation
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase5NotificationTests
    <Fact>
    Public Async Function LifecycleLogsOncePerSessionNotPerEmptyCycle() As Task
        Dim clock As New ManualTimeProvider()
        Dim cycle As New PeriodicImportCycleFake()
        Dim log As New Phase5LogWriter()
        Dim presenter As New PeriodicImportStatusPresenter()
        Dim service As New PeriodicImportService(cycle, clock, presenter, log)
        Await service.StartAsync(New PeriodicImportOptionsDto With {.WatchIntervalSeconds = 1})
        Await service.CurrentCycleCompletion
        For i = 1 To 5
            clock.Advance(TimeSpan.FromSeconds(1))
            Await clock.WaitForTimerAsync(i + 1)
            Await service.CurrentCycleCompletion
        Next
        Await service.StopAsync()
        Await service.StopAsync()
        Assert.Equal(New String() {"周期監視開始", "周期監視停止"}, log.Operations.Select(Function(l) l.OperationType))
        Assert.All(log.Operations, Sub(l)
                                      Assert.Equal(0, l.TotalCount)
                                      Assert.Equal(0, l.SuccessCount)
                                      Assert.Equal(0, l.FailureCount)
                                  End Sub)
        Assert.Contains(presenter.Drain(), Function(n) n.Kind = PeriodicImportNotificationKind.MonitoringStopped)
        Await service.StartAsync(New PeriodicImportOptionsDto With {.WatchIntervalSeconds = 1})
        Await service.StopAsync()
        Assert.Equal(4, log.Operations.Count)
    End Function

    <Theory>
    <InlineData(True)>
    <InlineData(False)>
    Public Async Function AbnormalStopIsLoggedOnceAndSafe(preparationFails As Boolean) As Task
        Dim cycle As New PeriodicImportCycleFake()
        If preparationFails Then
            cycle.Preparation = Function(token) Task.FromException(New InvalidOperationException("secret SQL / password"))
        Else
            cycle.Execution = Function() Task.FromException(New InvalidOperationException("secret SQL / password"))
        End If
        Dim log As New Phase5LogWriter()
        Dim presenter As New PeriodicImportStatusPresenter()
        Dim service As New PeriodicImportService(cycle, sink:=presenter, log:=log)
        If preparationFails Then
            Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() service.StartAsync(New PeriodicImportOptionsDto()))
        Else
            Await service.StartAsync(New PeriodicImportOptionsDto())
        End If
        Await service.Completion
        Await service.StopAsync()
        Assert.Equal(1, log.Operations.Where(Function(l) l.OperationType = "周期監視異常停止").Count())
        Assert.Equal(If(preparationFails, 0, 1), log.Operations.Where(Function(l) l.OperationType = "周期監視開始").Count())
        Assert.DoesNotContain("secret", presenter.CurrentMessage)
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
    End Function

    <Fact>
    Public Async Function SkipNotificationAndStatisticDoNotStickInNextNormalCycle() As Task
        Dim clock As New ManualTimeProvider()
        Dim release As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim cycle As New PeriodicImportCycleFake With {.Execution = Function() release.Task}
        Dim log As New Phase5LogWriter()
        Dim presenter As New PeriodicImportStatusPresenter()
        Dim service As New PeriodicImportService(cycle, clock, presenter, log)
        Dim events As Integer
        AddHandler service.CycleSkipped, Sub(status) Interlocked.Increment(events)
        Await service.StartAsync(New PeriodicImportOptionsDto With {.WatchIntervalSeconds = 1})
        Await cycle.WaitForCountAsync(1)
        clock.Advance(TimeSpan.FromSeconds(1))
        Await clock.WaitForTimerAsync(2)
        Assert.Equal(1, events)
        Assert.Equal(1L, service.Status.SkippedCycles)
        release.SetResult(True)
        Await service.CurrentCycleCompletion
        clock.Advance(TimeSpan.FromSeconds(1))
        Await clock.WaitForTimerAsync(3)
        Await service.CurrentCycleCompletion
        Assert.Equal(2, cycle.Count)
        Assert.Equal("Running", service.Status.Message)
        Assert.Equal(1L, service.Status.SkippedCycles)
        Assert.Contains(presenter.Drain(), Function(n) n.Kind = PeriodicImportNotificationKind.CycleSkipped)
        Assert.Single(log.Operations)
        Await service.StopAsync()
    End Function

    <Fact>
    Public Async Function LoggingFailureDoesNotRecursivelyLogOrFailImportState() As Task
        Dim log As New Phase5LogWriter With {.Fail = True}
        Dim presenter As New PeriodicImportStatusPresenter()
        Dim service As New PeriodicImportService(New PeriodicImportCycleFake(), sink:=presenter, log:=log)
        Await service.StartAsync(New PeriodicImportOptionsDto())
        Await service.CurrentCycleCompletion
        Assert.Equal(PeriodicImportState.Running, service.Status.State)
        Await service.StopAsync()
        Assert.Null(service.Status.LastError)
        Assert.Equal(2, presenter.Drain().Where(Function(n) n.Kind = PeriodicImportNotificationKind.LogFailure).Count())
        Assert.Empty(log.Operations)
    End Function

    <Fact>
    Public Sub SinkFailureCannotChangeAtomicResultOrJournal()
        Using f As New Phase4Fixture()
            f.Write("ok.csv")
            f.Cycle = New PeriodicImportCycle(f.Files, f.Journal, f.Executor, sink:=New ThrowingSink())
            f.Prepare()
            f.Run()
            Assert.Equal(1, f.DataCount())
            Assert.Equal(ProcessingStage.Completed, Assert.Single(f.State().Attempts).Stage)
            Assert.Equal(HeadDisposition.Resolved, Assert.Single(f.State().Orders).HeadDisposition)
        End Using
    End Sub

    <Theory>
    <InlineData("10", PeriodicImportNotificationKind.CsvSucceeded)>
    <InlineData("bad", PeriodicImportNotificationKind.CsvRejected)>
    Public Sub ActualCyclePublishesSafeResult(value As String, kind As PeriodicImportNotificationKind)
        Using f As New Phase4Fixture()
            Dim presenter As New PeriodicImportStatusPresenter()
            f.Write("target.csv", value:=value)
            f.Cycle = New PeriodicImportCycle(f.Files, f.Journal, f.Executor, sink:=presenter)
            f.Prepare()
            If kind = PeriodicImportNotificationKind.CsvSucceeded Then
                f.Run()
            Else
                Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            End If
            Dim history = presenter.Drain()
            Assert.Contains(history, Function(n) n.Kind = PeriodicImportNotificationKind.CsvDetected)
            Assert.Contains(history, Function(n) n.Kind = PeriodicImportNotificationKind.CsvImporting)
            Dim result = Assert.Single(history.Where(Function(n) n.Kind = kind))
            Assert.Equal(1, result.TotalCount)
            Assert.Equal(If(value = "10", 1, 0), result.RegisteredCount)
            If value = "10" Then Assert.Contains("一覧は検索で更新", result.Message)
        End Using
    End Sub

    Private Class ThrowingSink
        Implements IPeriodicImportNotificationSink
        Public Sub Publish(notification As PeriodicImportNotificationDto) Implements IPeriodicImportNotificationSink.Publish
            Throw New ObjectDisposedException("window")
        End Sub
    End Class
End Class
