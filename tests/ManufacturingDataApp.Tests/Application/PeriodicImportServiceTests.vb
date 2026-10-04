Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class PeriodicImportServiceTests
    Private Shared Function Signal() As TaskCompletionSource(Of Boolean)
        Return New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
    End Function

    Private Shared Function Options(Optional seconds As Integer = 10) As PeriodicImportOptionsDto
        Return New PeriodicImportOptionsDto With {.WatchIntervalSeconds = seconds}
    End Function

    <Fact>
    Public Async Function T01_StartAsync_ImmediatelyExecutesOnce_AndRejectsDuplicateStart() As Task
        Dim clock As New ManualTimeProvider()
        Dim fake As New PeriodicImportCycleFake()
        Dim service As New PeriodicImportService(fake, clock)
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
        Assert.Equal(0, fake.Count)
        Await service.StartAsync(Options(60))
        Await service.CurrentCycleCompletion
        Assert.Equal(1, fake.Count)
        Assert.Equal(PeriodicImportState.Running, service.Status.State)
        Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() service.StartAsync(Options()))
        Assert.Equal(1, fake.Count)
        Await service.StopAsync()
        Assert.Equal(0, clock.ActiveTimerCount)
    End Function

    <Theory>
    <InlineData(1)>
    <InlineData(604800)>
    Public Async Function T02_IntervalBoundary_OnlyRunsWhenDue(seconds As Integer) As Task
        Dim clock As New ManualTimeProvider()
        Dim fake As New PeriodicImportCycleFake()
        Dim service As New PeriodicImportService(fake, clock)
        Await service.StartAsync(Options(seconds))
        Await service.CurrentCycleCompletion
        clock.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromTicks(1))
        Assert.Equal(1, fake.Count)
        clock.Advance(TimeSpan.FromTicks(1))
        Await clock.WaitForTimerAsync(2)
        Await service.CurrentCycleCompletion
        Assert.Equal(2, fake.Count)
        Await service.StopAsync()
    End Function

    <Theory>
    <InlineData(0)>
    <InlineData(-1)>
    <InlineData(604801)>
    Public Async Function T03_InvalidInterval_RejectsBeforePreparation(seconds As Integer) As Task
        Dim fake As New PeriodicImportCycleFake With {
            .Preparation = Function(token) ThrowPreparation()}
        Dim service As New PeriodicImportService(fake, New ManualTimeProvider())
        Await Assert.ThrowsAsync(Of ArgumentOutOfRangeException)(Function() service.StartAsync(Options(seconds)))
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
        Assert.Null(service.Status.LastError)
        Assert.Equal(0, fake.Count)
    End Function

    Private Shared Function ThrowPreparation() As Task
        Throw New InvalidOperationException("Preparation must not be called")
    End Function

    <Fact>
    Public Async Function T04_ActiveCycle_SkipsTen_CompletesAtFifteen_RunsAtTwenty() As Task
        Dim clock As New ManualTimeProvider()
        Dim release = Signal()
        Dim fake As New PeriodicImportCycleFake With {.Execution = Function() release.Task}
        Dim service As New PeriodicImportService(fake, clock)
        Dim notices As New List(Of PeriodicImportStatusDto)()
        AddHandler service.CycleSkipped, Sub(status) notices.Add(status)
        Await service.StartAsync(Options())
        Await fake.WaitForCountAsync(1)
        clock.Advance(TimeSpan.FromSeconds(10))
        Await clock.WaitForTimerAsync(2)
        Assert.Equal(1, fake.Count)
        Assert.Equal(1L, service.Status.SkippedCycles)
        Assert.Equal(1L, Assert.Single(notices).SkippedCycles)
        Assert.Equal("Running", service.Status.Message)
        clock.Advance(TimeSpan.FromSeconds(5))
        release.SetResult(True)
        Await service.CurrentCycleCompletion
        Assert.False(service.Status.IsProcessing)
        Assert.Equal(1, fake.Count)
        clock.Advance(TimeSpan.FromSeconds(5))
        Await clock.WaitForTimerAsync(3)
        Await service.CurrentCycleCompletion
        Assert.Equal(2, fake.Count)
        Assert.Equal(1, fake.MaximumConcurrency)
        Await service.StopAsync()
    End Function

    <Fact>
    Public Async Function T05_DelayedScheduler_CoalescesMissedTicks_AndIgnoresWallClock() As Task
        Dim clock As New ManualTimeProvider()
        Dim fake As New PeriodicImportCycleFake()
        Dim service As New PeriodicImportService(fake, clock)
        Await service.StartAsync(Options())
        Await service.CurrentCycleCompletion
        clock.SetUtcNow(DateTimeOffset.UnixEpoch.AddYears(50))
        Assert.Equal(1, fake.Count)
        clock.Advance(TimeSpan.FromSeconds(35))
        Await clock.WaitForTimerAsync(2)
        Await service.CurrentCycleCompletion
        Assert.Equal(2, fake.Count)
        clock.SetUtcNow(DateTimeOffset.UnixEpoch.AddYears(-50))
        clock.Advance(TimeSpan.FromSeconds(4))
        Assert.Equal(2, fake.Count)
        clock.Advance(TimeSpan.FromSeconds(1))
        Await clock.WaitForTimerAsync(3)
        Await service.CurrentCycleCompletion
        Assert.Equal(3, fake.Count)
        Await service.StopAsync()
    End Function

    <Fact>
    Public Async Function T06_NoWork_ManyPeriods_KeepOneTimerAndNoQueuedWork() As Task
        Dim clock As New ManualTimeProvider()
        Dim fake As New PeriodicImportCycleFake()
        Dim service As New PeriodicImportService(fake, clock)
        Await service.StartAsync(Options(1))
        Await service.CurrentCycleCompletion
        For tick = 1 To 1000
            clock.Advance(TimeSpan.FromSeconds(1))
            Await clock.WaitForTimerAsync(tick + 1)
            Await service.CurrentCycleCompletion
            Assert.Equal(tick + 1, fake.Count)
            Assert.Equal(1, clock.ActiveTimerCount)
        Next
        Assert.Equal(1, fake.MaximumConcurrency)
        Assert.Null(service.Status.LastError)
        Assert.Equal(0L, service.Status.SkippedCycles)
        Await service.StopAsync()
        Assert.True(service.Completion.IsCompletedSuccessfully)
        Assert.Equal(0, clock.ActiveTimerCount)
    End Function

    <Fact>
    Public Async Function T20_StopWinsBeforeTick_NoNewCycleStarts() As Task
        Dim clock As New ManualTimeProvider()
        Dim hold = Signal()
        Dim fake As New PeriodicImportCycleFake With {.Execution = Function() hold.Task}
        Dim service As New PeriodicImportService(fake, clock)
        Await service.StartAsync(Options())
        Await fake.WaitForCountAsync(1)
        Dim stopTask = service.StopAsync()
        Assert.Equal(PeriodicImportState.Stopping, service.Status.State)
        clock.Advance(TimeSpan.FromSeconds(10))
        Assert.Equal(1, fake.Count)
        Assert.False(stopTask.IsCompleted)
        hold.SetResult(True)
        Await stopTask
        Assert.Equal(1, fake.Count)
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
    End Function

    <Fact>
    Public Async Function T20_TickWinsBeforeStop_StopWaitsForAcceptedCycle() As Task
        Dim clock As New ManualTimeProvider()
        Dim hold = Signal()
        Dim fake As New PeriodicImportCycleFake()
        Dim service As New PeriodicImportService(fake, clock)
        Await service.StartAsync(Options())
        Await service.CurrentCycleCompletion
        fake.Execution = Function() hold.Task
        clock.Advance(TimeSpan.FromSeconds(10))
        Await clock.WaitForTimerAsync(2)
        Await fake.WaitForCountAsync(2)
        Dim stopTask = service.StopAsync()
        Assert.False(stopTask.IsCompleted)
        Assert.True(service.Status.IsProcessing)
        hold.SetResult(True)
        Await stopTask
        Assert.Equal(2, fake.Count)
        Assert.Equal(1, fake.MaximumConcurrency)
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
        Assert.True(service.CurrentCycleCompletion.IsCompletedSuccessfully)
    End Function

    <Fact>
    Public Async Function StopAsync_WhenStopped_IsSafeAndDoesNotStartWork() As Task
        Dim fake As New PeriodicImportCycleFake()
        Dim service As New PeriodicImportService(fake)
        Await service.StopAsync()
        Await service.StopAsync()
        Assert.Equal(0, fake.Count)
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
    End Function

    <Fact>
    Public Async Function StopAsync_WhileWaiting_CancelsDelay_AndCanRestart() As Task
        Dim clock As New ManualTimeProvider()
        Dim fake As New PeriodicImportCycleFake()
        Dim service As New PeriodicImportService(fake, clock)
        Await service.StartAsync(Options())
        Await service.CurrentCycleCompletion
        Await service.StopAsync()
        clock.Advance(TimeSpan.FromDays(7))
        Assert.Equal(1, fake.Count)
        Assert.Equal(0, clock.ActiveTimerCount)
        Await service.StartAsync(Options())
        Await service.CurrentCycleCompletion
        Assert.Equal(2, fake.Count)
        Await service.StopAsync()
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
    End Function

    <Fact>
    Public Async Function StopAsync_CalledTwice_ReturnsSameCompletion_RejectsStartWhileStopping() As Task
        Dim hold = Signal()
        Dim fake As New PeriodicImportCycleFake With {.Execution = Function() hold.Task}
        Dim service As New PeriodicImportService(fake, New ManualTimeProvider())
        Await service.StartAsync(Options())
        Await fake.WaitForCountAsync(1)
        Dim first = service.StopAsync()
        Dim second = service.StopAsync()
        Assert.Same(first, second)
        Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() service.StartAsync(Options()))
        Assert.False(first.IsCompleted)
        hold.SetResult(True)
        Await Task.WhenAll(first, second)
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
    End Function

    <Fact>
    Public Async Function StopAsync_DuringStarting_WaitsForPreparation_WithoutStartingCycle() As Task
        Dim entered = Signal()
        Dim release = Signal()
        Dim cancellationSeen = Signal()
        Dim fake As New PeriodicImportCycleFake With {
            .Preparation = Async Function(token)
                               Using registration = token.Register(Sub() cancellationSeen.TrySetResult(True))
                                   entered.SetResult(True)
                                   Await release.Task
                               End Using
                           End Function}
        Dim clock As New ManualTimeProvider()
        Dim service As New PeriodicImportService(fake, clock)
        Dim starting = service.StartAsync(Options())
        Await entered.Task
        Assert.Equal(PeriodicImportState.Starting, service.Status.State)
        Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() service.StartAsync(Options()))
        Dim stopping = service.StopAsync()
        Await cancellationSeen.Task
        Assert.False(stopping.IsCompleted)
        release.SetResult(True)
        Await stopping
        Await Assert.ThrowsAnyAsync(Of OperationCanceledException)(Function() starting)
        Assert.Equal(0, fake.Count)
        Assert.Equal(0, clock.ActiveTimerCount)
        Assert.Null(service.Status.LastError)
    End Function

    <Fact>
    Public Async Function StartAsync_PreparationTakesTime_UsesCompletionAsOrigin_AndCopiesOptions() As Task
        Dim entered = Signal()
        Dim release = Signal()
        Dim fake As New PeriodicImportCycleFake With {
            .Preparation = Async Function(token)
                               entered.SetResult(True)
                               Await release.Task
                           End Function}
        Dim clock As New ManualTimeProvider()
        Dim service As New PeriodicImportService(fake, clock)
        Dim settings = Options()
        Dim starting = service.StartAsync(settings)
        Await entered.Task
        settings.WatchIntervalSeconds = 1
        clock.Advance(TimeSpan.FromSeconds(35))
        Assert.Equal(0, fake.Count)
        release.SetResult(True)
        Await starting
        Await service.CurrentCycleCompletion
        clock.Advance(TimeSpan.FromSeconds(9))
        Assert.Equal(1, fake.Count)
        clock.Advance(TimeSpan.FromSeconds(1))
        Await clock.WaitForTimerAsync(2)
        Await service.CurrentCycleCompletion
        Assert.Equal(2, fake.Count)
        Await service.StopAsync()
    End Function

    <Fact>
    Public Async Function PreparationFailure_IsObserved_StopsAndAllowsRestart() As Task
        Dim fake As New PeriodicImportCycleFake With {.Preparation = Function(token) ThrowPreparation()}
        Dim service As New PeriodicImportService(fake, New ManualTimeProvider())
        Await Assert.ThrowsAsync(Of InvalidOperationException)(Function() service.StartAsync(Options()))
        Await service.Completion
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
        Assert.IsType(Of InvalidOperationException)(service.Status.LastError)
        fake.Preparation = Function(token) Task.CompletedTask
        Await service.StartAsync(Options())
        Await service.StopAsync()
        Assert.Null(service.Status.LastError)
    End Function

    <Fact>
    Public Async Function CycleFailure_IsObserved_AutomaticallyStopsWithoutSelfAwait() As Task
        Dim release = Signal()
        Dim fake As New PeriodicImportCycleFake With {
            .Execution = Async Function()
                             Await release.Task
                             Throw New InvalidOperationException("cycle failure")
                         End Function}
        Dim clock As New ManualTimeProvider()
        Dim service As New PeriodicImportService(fake, clock)
        Await service.StartAsync(Options())
        release.SetResult(True)
        Await service.Completion
        Assert.Equal("cycle failure", service.Status.LastError.Message)
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
        Assert.False(service.Status.IsProcessing)
        Assert.Equal(0, clock.ActiveTimerCount)
    End Function

    <Fact>
    Public Async Function StopAsync_FromConcurrentCallers_JoinsOneShutdown() As Task
        Dim release = Signal()
        Dim beginStop = Signal()
        Dim firstRequested = Signal()
        Dim secondRequested = Signal()
        Dim fake As New PeriodicImportCycleFake With {.Execution = Function() release.Task}
        Dim service As New PeriodicImportService(fake, New ManualTimeProvider())
        Await service.StartAsync(Options())
        Await fake.WaitForCountAsync(1)
        Dim first = Task.Run(Async Function()
                                 Await beginStop.Task
                                 Dim pending = service.StopAsync()
                                 firstRequested.SetResult(True)
                                 Await pending
                             End Function)
        Dim second = Task.Run(Async Function()
                                  Await beginStop.Task
                                  Dim pending = service.StopAsync()
                                  secondRequested.SetResult(True)
                                  Await pending
                              End Function)
        beginStop.SetResult(True)
        Await Task.WhenAll(firstRequested.Task, secondRequested.Task)
        Assert.False(first.IsCompleted)
        Assert.False(second.IsCompleted)
        release.SetResult(True)
        Await Task.WhenAll(first, second)
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
        Assert.Null(service.Status.LastError)
    End Function

    <Fact>
    Public Async Function StopAsync_CancelsCooperativePreparation_WithoutLeavingTasks() As Task
        Dim entered = Signal()
        Dim neverComplete = Signal()
        Dim fake As New PeriodicImportCycleFake With {
            .Preparation = Function(token)
                               entered.SetResult(True)
                               Return neverComplete.Task.WaitAsync(token)
                           End Function}
        Dim service As New PeriodicImportService(fake, New ManualTimeProvider())
        Dim starting = service.StartAsync(Options())
        Await entered.Task
        Await service.StopAsync()
        Await Assert.ThrowsAnyAsync(Of OperationCanceledException)(Function() starting)
        Assert.True(service.Completion.IsCompletedSuccessfully)
        Assert.Equal(0, fake.Count)
        Assert.Null(service.Status.LastError)
    End Function

    <Fact>
    Public Async Function SchedulerFailure_IsObserved_AndWaitsForActiveCycle() As Task
        Dim release = Signal()
        Dim fake As New PeriodicImportCycleFake With {.Execution = Function() release.Task}
        Dim clock As New ManualTimeProvider()
        Dim service As New PeriodicImportService(fake, clock)
        Await service.StartAsync(Options())
        Await fake.WaitForCountAsync(1)
        clock.FailNextTimer = True
        clock.Advance(TimeSpan.FromSeconds(10))
        Await clock.TimerFailureObserved.Task
        Assert.False(service.Completion.IsCompleted)
        ' Completion is the tracked supervisor: it must await the accepted cycle.
        release.SetResult(True)
        Await service.Completion
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
        Assert.IsType(Of InvalidOperationException)(service.Status.LastError)
        Assert.Equal(0, clock.ActiveTimerCount)
        Assert.Equal(1, fake.Count)
    End Function
End Class
