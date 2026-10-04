Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Presentation
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase6MainFormTests
    Friend Shared Function Compose(db As Phase2Database, service As PeriodicImportService, presenter As PeriodicImportStatusPresenter) As PeriodicImportComposition
        Return New PeriodicImportComposition(db.Factory.DatabasePath, presenter, service,
            New PeriodicImportRecoveryService(New PeriodicImportFileStore(), New JsonImportProcessingJournal(), Function() service.Status.State = PeriodicImportState.Stopped))
    End Function

    <Fact>
    Public Sub StoppedActualClosePassesOnceWithoutCleanup()
        RunUiAsync(Async Function()
                       Using db As New Phase2Database()
                           db.Initialize()
                           Dim cycle As New Phase6Cycle()
                           Dim service As New PeriodicImportService(cycle)
                           Using form = CreateMain(db, Compose(db, service, New PeriodicImportStatusPresenter()))
                               Dim handle = form.Handle
                               Dim events = 0
                               AddHandler form.FormClosing, Sub(sender, e)
                                                                events += 1
                                                                Assert.False(e.Cancel)
                                                            End Sub
                               form.Close()
                               Assert.True(form.IsDisposed)
                               Assert.Equal(1, events)
                               Assert.Equal(0, cycle.CleanupCount)
                               Assert.Equal(0, cycle.ExecuteCount)
                           End Using
                       End Using
                       Await Task.CompletedTask
                   End Function)
    End Sub

    <Theory>
    <InlineData(PeriodicImportState.Starting)>
    <InlineData(PeriodicImportState.Running)>
    <InlineData(PeriodicImportState.Stopping)>
    Public Sub ActualFormClosingCancelsThenPostsExactlyOneUiClose(state As PeriodicImportState)
        RunUiAsync(Async Function()
                       Using db As New Phase2Database()
                           db.Initialize()
                           Dim release = Signal6()
                           Dim cleanupRelease = Signal6()
                           Dim cleanupEntered = Signal6()
                           Dim cycle As New Phase6Cycle With {
                               .Cleanup = Async Function()
                                              cleanupEntered.TrySetResult(True)
                                              Await cleanupRelease.Task
                                          End Function}
                           If state = PeriodicImportState.Starting Then
                               cycle.Preparation = Function(token) release.Task
                           Else
                               cycle.Execution = Function(token) release.Task
                           End If
                           Dim presenter As New PeriodicImportStatusPresenter()
                           Dim service As New PeriodicImportService(cycle, sink:=presenter)
                           Using form = CreateMain(db, Compose(db, service, presenter))
                               Dim handle = form.Handle
                               Dim threadId = Environment.CurrentManagedThreadId
                               Dim events As New List(Of Boolean)()
                               Dim closed = Signal6()
                               AddHandler form.FormClosing, Sub(sender, e)
                                                                Assert.Equal(threadId, Environment.CurrentManagedThreadId)
                                                                events.Add(e.Cancel)
                                                            End Sub
                               AddHandler form.FormClosed, Sub() closed.TrySetResult(True)
                               Dim starting = service.StartAsync(New PeriodicImportOptionsDto())
                               Try
                                   If state <> PeriodicImportState.Starting Then
                                       Await starting
                                       Await cycle.Entered.Task
                                   End If
                                   If state = PeriodicImportState.Stopping Then Invoke(form, "StartStopMonitoring", Nothing, EventArgs.Empty)
                                   Assert.Equal(state, service.Status.State)
                                   Dim requests = If(state = PeriodicImportState.Running, 10, 1)
                                   For i = 1 To requests
                                       form.Close()
                                   Next
                                   Assert.Equal(requests, events.Count)
                                   Assert.All(events, Sub(cancelled) Assert.True(cancelled))
                                   Assert.False(form.IsDisposed)
                                   Assert.False(Field(Of Button)(form, "configButton").Enabled)
                                   Assert.False(Field(Of Button)(form, "_startStop").Enabled)
                                   Invoke(form, "StartStopMonitoring", Nothing, EventArgs.Empty)
                                   release.TrySetResult(True)
                                   Await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10))
                                   Assert.False(closed.Task.IsCompleted)
                                   Assert.False(form.IsDisposed)
                                   Assert.Equal(1, cycle.CleanupCount)
                                   cleanupRelease.SetResult(True)
                                   Await closed.Task.WaitAsync(TimeSpan.FromSeconds(10))
                                   If state = PeriodicImportState.Starting Then Await Assert.ThrowsAnyAsync(Of OperationCanceledException)(Function() starting)
                                   Assert.True(form.IsDisposed)
                                   Assert.Equal(requests + 1, events.Count)
                                   Assert.False(events.Last())
                                   Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
                                   Assert.True(service.Status.CleanupCompleted)
                                   Assert.Equal(1, cycle.CleanupCount)
                               Finally
                                   release.TrySetResult(True)
                                   cleanupRelease.TrySetResult(True)
                               End Try
                           End Using
                       End Using
                   End Function)
    End Sub

    <Fact>
    Public Sub CleanupFailureKeepsFormAndMutationGuardsAfterStopped()
        RunUiAsync(Async Function()
                       Using db As New Phase2Database()
                           db.Initialize()
                           Dim release = Signal6()
                           Dim cycle As New Phase6Cycle With {.Execution = Function(token) release.Task,
                               .Cleanup = Function() Task.FromException(New InvalidOperationException("private cleanup"))}
                           Dim service As New PeriodicImportService(cycle)
                           Using form = CreateMain(db, Compose(db, service, New PeriodicImportStatusPresenter()))
                               Dim handle = form.Handle
                               Await service.StartAsync(New PeriodicImportOptionsDto())
                               Await cycle.Entered.Task
                               form.Close()
                               release.SetResult(True)
                               Dim controller = Field(Of PeriodicImportExitController)(form, "_exitController")
                               Await controller.Completion
                               Invoke(form, "DrainNotifications")
                               form.Close()
                               Assert.False(form.IsDisposed)
                               Assert.True(controller.Failed)
                               Assert.Equal(PeriodicImportState.Stopped, service.Status.State)
                               Assert.Contains("終了を中止", Field(Of Label)(form, "_statusLabel").Text)
                               Assert.DoesNotContain("private", Field(Of Label)(form, "_statusLabel").Text)
                               For Each name In {"configButton", "deleteButton", "masterButton", "importButton", "_startStop"}
                                   Assert.False(Field(Of Button)(form, name).Enabled)
                               Next
                               For Each name In {"OpenCsvImport", "OpenCsvConfig", "OpenMaster", "DeleteSelected", "StartStopMonitoring"}
                                   Invoke(form, name, Nothing, EventArgs.Empty)
                               Next
                               Assert.Equal(1, cycle.CleanupCount)
                           End Using
                       End Using
                   End Function)
    End Sub

    <Fact>
    Public Sub DisposedFormDoesNotReceiveDeferredCloseOrNotification()
        RunUiAsync(Async Function()
                       Using db As New Phase2Database()
                           db.Initialize()
                           Dim release = Signal6()
                           Dim cycle As New Phase6Cycle With {.Execution = Function(token) release.Task}
                           Dim presenter As New PeriodicImportStatusPresenter()
                           Dim service As New PeriodicImportService(cycle, sink:=presenter)
                           Using form = CreateMain(db, Compose(db, service, presenter))
                               Dim handle = form.Handle
                               Await service.StartAsync(New PeriodicImportOptionsDto())
                               Await cycle.Entered.Task
                               form.Close()
                               Dim controller = Field(Of PeriodicImportExitController)(form, "_exitController")
                               ' External disposal race, not an application shutdown path.
                               form.Dispose()
                               release.SetResult(True)
                               Await controller.Completion
                               presenter.Publish(New PeriodicImportNotificationDto With {.IsCritical = True})
                               System.Windows.Forms.Application.DoEvents()
                               Assert.True(form.IsDisposed)
                               Assert.True(service.Status.CleanupCompleted)
                               Assert.Equal(0, presenter.PendingCount)
                           End Using
                       End Using
                   End Function)
    End Sub
End Class
