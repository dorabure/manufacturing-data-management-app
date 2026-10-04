Imports System.Threading
Imports System.Threading.Tasks

Namespace TestSupport
    ' Advance deliberately coalesces overdue callbacks; no real-time sleeps.
    Public Class ManualTimeProvider
        Inherits TimeProvider

        Private ReadOnly _sync As New Object()
        Private ReadOnly _timers As New List(Of ManualTimer)()
        Private ReadOnly _waiters As New List(Of KeyValuePair(Of Integer, TaskCompletionSource(Of Boolean)))()
        Private _timestamp As Long
        Private _utc As DateTimeOffset = DateTimeOffset.UnixEpoch
        Private _created As Integer
        Public Property FailNextTimer As Boolean
        Public ReadOnly TimerFailureObserved As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)

        Public Overrides ReadOnly Property TimestampFrequency As Long
            Get
                Return TimeSpan.TicksPerSecond
            End Get
        End Property

        Public Overrides Function GetTimestamp() As Long
            SyncLock _sync
                Return _timestamp
            End SyncLock
        End Function

        Public Overrides Function GetUtcNow() As DateTimeOffset
            SyncLock _sync
                Return _utc
            End SyncLock
        End Function

        Public Sub SetUtcNow(value As DateTimeOffset)
            SyncLock _sync
                _utc = value
            End SyncLock
        End Sub

        Public ReadOnly Property ActiveTimerCount As Integer
            Get
                SyncLock _sync
                    Return _timers.Count
                End SyncLock
            End Get
        End Property

        Public Function WaitForTimerAsync(number As Integer) As Task
            SyncLock _sync
                If _created >= number Then Return Task.CompletedTask
                Dim completion As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                _waiters.Add(New KeyValuePair(Of Integer, TaskCompletionSource(Of Boolean))(number, completion))
                Return completion.Task
            End SyncLock
        End Function

        Public Overrides Function CreateTimer(callback As TimerCallback, state As Object, dueTime As TimeSpan, period As TimeSpan) As ITimer
            SyncLock _sync
                If FailNextTimer Then
                    FailNextTimer = False
                    TimerFailureObserved.TrySetResult(True)
                    Throw New InvalidOperationException("Injected scheduler timer failure")
                End If
                Dim timer As New ManualTimer(Me, callback, state)
                timer.Change(dueTime, period)
                _timers.Add(timer)
                _created += 1
                For Each waiter In _waiters.Where(Function(w) w.Key <= _created).ToArray()
                    waiter.Value.TrySetResult(True)
                    _waiters.Remove(waiter)
                Next
                Return timer
            End SyncLock
        End Function

        Public Sub Advance(value As TimeSpan)
            If value < TimeSpan.Zero Then Throw New ArgumentOutOfRangeException(NameOf(value))
            Dim due As New List(Of ManualTimer)()
            SyncLock _sync
                _timestamp += value.Ticks
                _utc += value
                For Each timer In _timers
                    If timer.Due <= _timestamp Then
                        timer.Due = If(timer.PeriodTicks > 0, _timestamp + timer.PeriodTicks, Long.MaxValue)
                        due.Add(timer)
                    End If
                Next
            End SyncLock
            For Each timer In due
                timer.Callback(timer.CallbackState)
            Next
        End Sub

        Private NotInheritable Class ManualTimer
            Implements ITimer

            Private ReadOnly _owner As ManualTimeProvider
            Private _disposed As Boolean
            Public ReadOnly Callback As TimerCallback
            Public ReadOnly CallbackState As Object
            Public Due As Long = Long.MaxValue
            Public PeriodTicks As Long

            Public Sub New(owner As ManualTimeProvider, callback As TimerCallback, state As Object)
                _owner = owner
                Me.Callback = callback
                CallbackState = state
            End Sub

            Public Function Change(dueTime As TimeSpan, period As TimeSpan) As Boolean Implements ITimer.Change
                SyncLock _owner._sync
                    If _disposed Then Return False
                    Due = If(dueTime = Timeout.InfiniteTimeSpan, Long.MaxValue, _owner._timestamp + dueTime.Ticks)
                    PeriodTicks = period.Ticks
                    Return True
                End SyncLock
            End Function

            Public Sub Dispose() Implements IDisposable.Dispose
                SyncLock _owner._sync
                    _disposed = True
                    _owner._timers.Remove(Me)
                End SyncLock
            End Sub

            Public Function DisposeAsync() As ValueTask Implements IAsyncDisposable.DisposeAsync
                Dispose()
                Return ValueTask.CompletedTask
            End Function
        End Class
    End Class
End Namespace
