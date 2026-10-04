Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces

Namespace Presentation
    ' No controls or scheduler mutations. UI drains at 250 ms; critical posts are coalesced.
    Public NotInheritable Class PeriodicImportStatusPresenter
        Implements IPeriodicImportNotificationSink, IDisposable
        Private ReadOnly _sync As New Object()
        Private ReadOnly _pending As New List(Of PeriodicImportNotificationDto)()
        Private ReadOnly _history As New List(Of PeriodicImportNotificationDto)()
        Private _current As PeriodicImportNotificationDto
        Private _disposed As Boolean
        Private _criticalPosted As Boolean
        Private _selected As Guid?
        Public Event CriticalAvailable()

        Public Shared Function SafeText(value As String) As String
            Return New String(If(value, String.Empty).Take(512).Select(Function(c) If(Char.IsControl(c), " "c, c)).ToArray())
        End Function

        Public Sub Publish(notification As PeriodicImportNotificationDto) Implements IPeriodicImportNotificationSink.Publish
            If notification Is Nothing Then Return
            Dim post As Boolean
            SyncLock _sync
                If _disposed Then Return
                Dim item As New PeriodicImportNotificationDto With {.EventId = notification.EventId, .OccurredAt = notification.OccurredAt,
                    .Kind = notification.Kind, .Message = SafeText(notification.Message), .FileName = SafeText(notification.FileName),
                    .TotalCount = notification.TotalCount, .RegisteredCount = notification.RegisteredCount,
                    .UnregisteredCount = notification.UnregisteredCount, .IsCritical = notification.IsCritical}
                If item.Kind <> PeriodicImportNotificationKind.CycleSkipped AndAlso item.Kind <> PeriodicImportNotificationKind.FollowingDeferred Then _current = item
                AppendBounded(_pending, item)
                If item.IsCritical AndAlso Not _criticalPosted Then
                    _criticalPosted = True
                    post = True
                End If
            End SyncLock
            If post Then
                Try
                    RaiseEvent CriticalAvailable()
                Catch
                    ' A closed/disposed UI is not an import failure.
                End Try
            End If
        End Sub

        Private Shared Sub AppendBounded(items As List(Of PeriodicImportNotificationDto), item As PeriodicImportNotificationDto)
            If item.Kind = PeriodicImportNotificationKind.CycleSkipped AndAlso items.Count > 0 AndAlso items(items.Count - 1).Kind = item.Kind Then
                Dim last = items(items.Count - 1)
                last.Repetitions = Math.Min(Long.MaxValue - item.Repetitions, last.Repetitions) + item.Repetitions
                last.OccurredAt = item.OccurredAt
                Return
            End If
            If items.Count >= 100 Then
                Dim index = items.FindIndex(Function(n) Not n.IsCritical)
                items.RemoveAt(If(index < 0, 0, index))
            End If
            items.Add(item)
        End Sub

        Public Function Drain() As IReadOnlyList(Of PeriodicImportNotificationDto)
            SyncLock _sync
                For Each item In _pending
                    AppendBounded(_history, item)
                Next
                _pending.Clear()
                _criticalPosted = False
                If _selected.HasValue AndAlso Not _history.Any(Function(n) n.EventId = _selected.Value) Then _selected = Nothing
                Return _history.AsEnumerable().Reverse().ToArray()
            End SyncLock
        End Function

        Public ReadOnly Property PendingCount As Integer
            Get
                SyncLock _sync
                    Return _pending.Count
                End SyncLock
            End Get
        End Property
        Public ReadOnly Property CurrentMessage As String
            Get
                SyncLock _sync
                    Return If(_current Is Nothing, "停止中です。", SafeText(If(String.IsNullOrEmpty(_current.FileName), "", _current.FileName & "：") & _current.Message))
                End SyncLock
            End Get
        End Property
        Public Property SelectedEventId As Guid?
            Get
                SyncLock _sync
                    Return _selected
                End SyncLock
            End Get
            Set(value As Guid?)
                SyncLock _sync
                    _selected = value
                End SyncLock
            End Set
        End Property
        Public Shared Function Display(item As PeriodicImportNotificationDto) As String
            Return SafeText($"{item.OccurredAt.ToLocalTime():HH:mm:ss} {item.Message} {item.FileName}" & If(item.Repetitions > 1, $" ({item.Repetitions}回)", ""))
        End Function
        Public Sub Dispose() Implements IDisposable.Dispose
            SyncLock _sync
                _disposed = True
                _pending.Clear()
                _history.Clear()
            End SyncLock
        End Sub
    End Class
End Namespace
