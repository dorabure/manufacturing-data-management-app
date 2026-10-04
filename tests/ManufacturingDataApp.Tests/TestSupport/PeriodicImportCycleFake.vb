Imports System.Threading
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.DTOs

Namespace TestSupport
    Public Class PeriodicImportCycleFake
        Implements IPeriodicImportCycle

        Public Property Preparation As Func(Of CancellationToken, Task) = Function(token) Task.CompletedTask
        Public Property Execution As Func(Of Task) = Function() Task.CompletedTask
        Private ReadOnly _sync As New Object()
        Private _count As Integer
        Private _active As Integer
        Private _maximum As Integer
        Private ReadOnly _waiters As New List(Of KeyValuePair(Of Integer, TaskCompletionSource(Of Boolean)))()

        Public ReadOnly Property Count As Integer
            Get
                SyncLock _sync
                    Return _count
                End SyncLock
            End Get
        End Property

        Public ReadOnly Property MaximumConcurrency As Integer
            Get
                SyncLock _sync
                    Return _maximum
                End SyncLock
            End Get
        End Property

        Public Function WaitForCountAsync(count As Integer) As Task
            SyncLock _sync
                If _count >= count Then Return Task.CompletedTask
                Dim signal As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                _waiters.Add(New KeyValuePair(Of Integer, TaskCompletionSource(Of Boolean))(count, signal))
                Return signal.Task
            End SyncLock
        End Function

        Public Function PrepareAsync(options As PeriodicImportOptionsDto, token As CancellationToken) As Task Implements IPeriodicImportCycle.PrepareAsync
            Return Preparation(token)
        End Function

        Public Async Function ExecuteAsync(stopBoundaryToken As CancellationToken) As Task Implements IPeriodicImportCycle.ExecuteAsync
            SyncLock _sync
                _count += 1
                _active += 1
                _maximum = Math.Max(_maximum, _active)
                For Each waiter In _waiters.Where(Function(w) w.Key <= _count).ToArray()
                    waiter.Value.TrySetResult(True)
                    _waiters.Remove(waiter)
                Next
            End SyncLock
            Try
                Await Execution.Invoke().ConfigureAwait(False)
            Finally
                SyncLock _sync
                    _active -= 1
                End SyncLock
            End Try
        End Function

        Public Function CleanupAsync() As Task Implements IPeriodicImportCycle.CleanupAsync
            Return Task.CompletedTask
        End Function
    End Class
End Namespace
