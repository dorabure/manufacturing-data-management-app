Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Presentation
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase6ExitControllerTests
    <Fact>
    Public Sub StoppedDoesNotCallStop()
        Dim stops = 0
        Dim service As New PeriodicImportService(New Phase6Cycle())
        Dim controller = ExitFor(service, Function()
                                              stops += 1
                                              Return service.StopAsync()
                                          End Function)
        Assert.True(controller.RequestClose())
        Assert.False(controller.Requested)
        Assert.Equal(0, stops)
    End Sub

    <Theory>
    <InlineData(PeriodicImportState.Starting)>
    <InlineData(PeriodicImportState.Running)>
    <InlineData(PeriodicImportState.Stopping)>
    Public Async Function TenCloseRequestsJoinOneStop(state As PeriodicImportState) As Task
        Dim release = Signal6()
        Dim cycle As New Phase6Cycle()
        If state = PeriodicImportState.Starting Then
            cycle.Preparation = Function(token) release.Task
        Else
            cycle.Execution = Function(token) release.Task
        End If
        Dim service As New PeriodicImportService(cycle)
        Dim starting = service.StartAsync(New PeriodicImportOptionsDto())
        If state <> PeriodicImportState.Starting Then
            Await starting
            Await cycle.Entered.Task
        End If
        Dim stops = 0
        Dim requestStop As Func(Of Task) = Function()
                                              stops += 1
                                              Return service.StopAsync()
                                          End Function
        Dim buttonStop = If(state = PeriodicImportState.Stopping, requestStop(), Task.CompletedTask)
        Dim controller = ExitFor(service, requestStop)
        For i = 1 To 10
            Assert.False(controller.RequestClose())
        Next
        Assert.Equal(1, stops)
        Assert.False(controller.Completion.IsCompleted)
        Assert.False(service.Status.CleanupCompleted)
        release.SetResult(True)
        Await controller.Completion.WaitAsync(TimeSpan.FromSeconds(10))
        Await buttonStop
        If state = PeriodicImportState.Starting Then Await Assert.ThrowsAnyAsync(Of OperationCanceledException)(Function() starting)
        Assert.True(controller.AllowFinalClose)
        Assert.True(controller.RequestClose())
        Assert.Equal(1, cycle.CleanupCount)
        Assert.Equal(If(state = PeriodicImportState.Starting, 0, 1), cycle.ExecuteCount)
        Assert.True(service.Status.CleanupCompleted)
        Assert.Equal(1, stops)
    End Function

    <Fact>
    Public Async Function ThirtySecondsOnlyChangesExplanationNeverTimesOut() As Task
        Dim clock As New ManualTimeProvider()
        Dim release = Signal6()
        Dim cycle As New Phase6Cycle With {.Execution = Function(token) release.Task}
        Dim service As New PeriodicImportService(cycle, clock)
        Await service.StartAsync(New PeriodicImportOptionsDto())
        Await cycle.Entered.Task
        Dim controller As New PeriodicImportExitController(Function() service.Status, AddressOf service.StopAsync, Function() service.Completion, clock)
        Assert.False(controller.RequestClose())
        clock.Advance(TimeSpan.FromSeconds(29))
        Assert.DoesNotContain("30秒", controller.Message)
        clock.Advance(TimeSpan.FromSeconds(1))
        Assert.Contains("30秒", controller.Message)
        clock.Advance(TimeSpan.FromDays(1))
        Assert.False(controller.Completion.IsCompleted)
        Assert.False(controller.AllowFinalClose)
        release.SetResult(True)
        Await controller.Completion
        Assert.True(controller.AllowFinalClose)
    End Function

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Async Function StopExceptionOrFaultNeverPermitsClose(synchronous As Boolean) As Task
        Dim stops = 0
        Dim controller As New PeriodicImportExitController(
            Function() New PeriodicImportStatusDto(PeriodicImportState.Running, False, 0, Nothing, False),
            Function()
                stops += 1
                If synchronous Then Throw New InvalidOperationException("private failure")
                Return Task.FromException(New InvalidOperationException("private failure"))
            End Function, Function() Task.CompletedTask)
        Assert.False(controller.RequestClose())
        Await controller.Completion
        For i = 1 To 10
            Assert.False(controller.RequestClose())
        Next
        Assert.Equal(1, stops)
        Assert.True(controller.Failed)
        Assert.False(controller.AllowFinalClose)
        Assert.DoesNotContain("private", controller.Message)
    End Function

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Async Function CleanupFailureIsVisibleEvenIfFirstErrorWasDifferent(firstFailure As Boolean) As Task
        Dim release = Signal6()
        Dim cycle As New Phase6Cycle With {
            .Execution = Async Function(token)
                             Await release.Task
                             If firstFailure Then Throw New InvalidOperationException("first")
                         End Function,
            .Cleanup = Function() Task.FromException(New InvalidOperationException("cleanup"))}
        Dim service As New PeriodicImportService(cycle)
        Await service.StartAsync(New PeriodicImportOptionsDto())
        Await cycle.Entered.Task
        Dim controller = ExitFor(service)
        Assert.False(controller.RequestClose())
        release.SetResult(True)
        Await controller.Completion
        Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
        Assert.Equal(If(firstFailure, "first", "cleanup"), service.Status.LastError.Message)
        Assert.False(service.Status.CleanupCompleted)
        Assert.False(controller.AllowFinalClose)
        Assert.True(controller.Failed)
        Assert.False(ExitFor(service).RequestClose())
        Assert.Equal(1, cycle.CleanupCount)
    End Function

    <Fact>
    Public Async Function StoppedPublicationStillWaitsForLifetimeCompletion() As Task
        Dim release = Signal6()
        Dim stops = 0
        Dim controller As New PeriodicImportExitController(
            Function() New PeriodicImportStatusDto(PeriodicImportState.Stopped, False, 0, Nothing),
            Function()
                stops += 1
                Return Task.CompletedTask
            End Function, Function() release.Task)
        Assert.False(controller.RequestClose())
        Assert.False(controller.AllowFinalClose)
        release.SetResult(True)
        Await controller.Completion
        Assert.True(controller.AllowFinalClose)
        Assert.Equal(0, stops)
    End Function
End Class
