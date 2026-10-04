Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Presentation
Imports Xunit

Public Class Phase5PresenterTests
    <Theory>
    <InlineData(PeriodicImportState.Stopped, True)>
    <InlineData(PeriodicImportState.Starting, False)>
    <InlineData(PeriodicImportState.Running, False)>
    <InlineData(PeriodicImportState.Stopping, False)>
    Public Sub T21_T22_Policy(state As PeriodicImportState, canMutate As Boolean)
        For Each mode In {ImportMode.SingleFile, ImportMode.PeriodicFolder}
            Dim policy As New PeriodicUiPolicy(state, mode, True)
            Assert.Equal(canMutate, policy.CanMutate)
            Assert.Equal(mode = ImportMode.PeriodicFolder, policy.ShowPeriodicPanel)
            Assert.Equal(canMutate AndAlso mode = ImportMode.SingleFile, policy.CanManualImport)
            Assert.Equal(canMutate AndAlso mode = ImportMode.PeriodicFolder, policy.CanStart)
            Assert.Equal(state = PeriodicImportState.Running, policy.CanStop)
        Next
        Assert.False(New PeriodicUiPolicy(state, ImportMode.PeriodicFolder, False).CanStart)
    End Sub

    <Fact>
    Public Sub T23_BoundedQueueHistoryAndCriticalRetention()
        Using presenter As New PeriodicImportStatusPresenter()
            Dim important As New PeriodicImportNotificationDto With {.Kind = PeriodicImportNotificationKind.RecoveryRequired, .IsCritical = True, .Message = "復旧確認"}
            presenter.Publish(important)
            For i = 1 To 1000
                presenter.Publish(New PeriodicImportNotificationDto With {.Kind = PeriodicImportNotificationKind.CsvDetected, .Message = i.ToString()})
                Assert.InRange(presenter.PendingCount, 0, 100)
            Next
            Dim history = presenter.Drain()
            Assert.Equal(100, history.Count)
            Assert.Contains(history, Function(n) n.EventId = important.EventId)
            Assert.Equal("1000", history(0).Message)
            presenter.SelectedEventId = important.EventId
            For i = 1 To 110
                presenter.Publish(New PeriodicImportNotificationDto With {.Message = "new"})
                Assert.InRange(presenter.Drain().Count, 1, 100)
            Next
            Assert.Equal(important.EventId, presenter.SelectedEventId.Value)
            Assert.Equal("new", presenter.CurrentMessage)
        End Using
    End Sub

    <Fact>
    Public Sub T23_SkipAggregationAcrossDrainsDoesNotReplaceCurrentState()
        Using presenter As New PeriodicImportStatusPresenter()
            presenter.Publish(New PeriodicImportNotificationDto With {.Kind = PeriodicImportNotificationKind.CorrectionPending, .IsCritical = True, .Message = "important"})
            For i = 1 To 300
                presenter.Publish(New PeriodicImportNotificationDto With {.Kind = PeriodicImportNotificationKind.CycleSkipped, .Message = "skip"})
                If i Mod 10 = 0 Then presenter.Drain()
            Next
            Dim history = presenter.Drain()
            Assert.Equal(2, history.Count)
            Assert.Equal(300L, history(0).Repetitions)
            Assert.Equal("important", presenter.CurrentMessage)
        End Using
    End Sub

    <Fact>
    Public Sub T23_ControlCharactersLengthAndSelection()
        Using presenter As New PeriodicImportStatusPresenter()
            Dim first As New PeriodicImportNotificationDto With {.Message = "old"}
            presenter.Publish(first)
            presenter.Drain()
            presenter.SelectedEventId = first.EventId
            presenter.Publish(New PeriodicImportNotificationDto With {.Message = vbCrLf & vbTab & ChrW(0) & New String("x"c, 1000), .FileName = New String("f"c, 1000)})
            Dim history = presenter.Drain()
            Assert.Equal(first.EventId, presenter.SelectedEventId.Value)
            Assert.InRange(PeriodicImportStatusPresenter.Display(history(0)).Length, 1, 512)
            Assert.DoesNotContain(presenter.CurrentMessage, Function(c) Char.IsControl(c))
            Assert.NotEqual("old", presenter.CurrentMessage)
            presenter.SelectedEventId = Nothing
            Assert.Null(presenter.SelectedEventId)
        End Using
    End Sub

    <Fact>
    Public Async Function CriticalPostsAreCoalescedAndDisposeIsSafe() As Task
        Dim presenter As New PeriodicImportStatusPresenter()
        Dim posts As Integer
        AddHandler presenter.CriticalAvailable, Sub() Threading.Interlocked.Increment(posts)
        Await Task.WhenAll(Enumerable.Range(1, 500).Select(Function(i) Task.Run(Sub() presenter.Publish(New PeriodicImportNotificationDto With {.IsCritical = True}))))
        Assert.Equal(1, posts)
        Assert.Equal(100, presenter.PendingCount)
        Assert.Equal(100, presenter.Drain().Count)
        presenter.Publish(New PeriodicImportNotificationDto With {.IsCritical = True})
        Assert.Equal(2, posts)
        presenter.Dispose()
        presenter.Publish(New PeriodicImportNotificationDto With {.IsCritical = True})
        Assert.Equal(0, presenter.PendingCount)
        Assert.Empty(presenter.Drain())
    End Function
End Class
