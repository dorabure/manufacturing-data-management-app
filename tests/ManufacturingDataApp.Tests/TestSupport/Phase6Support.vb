Imports System.Diagnostics
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Presentation

Namespace TestSupport
    Friend Module Phase6Support
        Public Function Signal6() As TaskCompletionSource(Of Boolean)
            Return New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
        End Function
        Public Function ExitFor(service As PeriodicImportService, Optional stopAction As Func(Of Task) = Nothing) As PeriodicImportExitController
            Return New PeriodicImportExitController(Function() service.Status, If(stopAction, New Func(Of Task)(AddressOf service.StopAsync)), Function() service.Completion)
        End Function
        Public Sub RunUiAsync(action As Func(Of Task))
            RunSta(Sub()
                       Dim previous = SynchronizationContext.Current
                       Using dispatcher As New Control()
                           Dim handle = dispatcher.Handle
                           SynchronizationContext.SetSynchronizationContext(New WindowsFormsSynchronizationContext())
                           Try
                               Dim pending = action()
                               Dim deadline = Stopwatch.StartNew()
                               While Not pending.IsCompleted
                                   If deadline.Elapsed > TimeSpan.FromSeconds(25) Then Throw New TimeoutException("UI test timed out")
                                   System.Windows.Forms.Application.DoEvents()
                                   Thread.Yield()
                               End While
                               pending.GetAwaiter().GetResult()
                           Finally
                               SynchronizationContext.SetSynchronizationContext(previous)
                           End Try
                       End Using
                   End Sub)
        End Sub
    End Module

    Friend Class Phase6Cycle
        Implements IPeriodicImportCycle
        Public Preparation As Func(Of CancellationToken, Task) = Function(token) Task.CompletedTask
        Public Execution As Func(Of CancellationToken, Task) = Function(token) Task.CompletedTask
        Public Cleanup As Func(Of Task) = Function() Task.CompletedTask
        Public CleanupCount As Integer
        Public ExecuteCount As Integer
        Public ReadOnly Entered As TaskCompletionSource(Of Boolean) = Signal6()
        Public Function PrepareAsync(options As PeriodicImportOptionsDto, token As CancellationToken) As Task Implements IPeriodicImportCycle.PrepareAsync
            Return Preparation(token)
        End Function
        Public Function ExecuteAsync(token As CancellationToken) As Task Implements IPeriodicImportCycle.ExecuteAsync
            Interlocked.Increment(ExecuteCount)
            Entered.TrySetResult(True)
            Return Execution(token)
        End Function
        Public Function CleanupAsync() As Task Implements IPeriodicImportCycle.CleanupAsync
            Interlocked.Increment(CleanupCount)
            Return Cleanup()
        End Function
    End Class

    ' Gates real Atomic validation or an actual AddRange transaction without production hooks.
    Friend Class Phase6DataRepository
        Implements IMeasurementDataRepository
        Private ReadOnly _inner As MeasurementDataRepository
        Public ValidationGate As Action
        Public TransactionGate As Action
        Public Sub New(inner As MeasurementDataRepository)
            _inner = inner
        End Sub
        Public Function Search(id As String, item As String, fromDate As DateTime?, toDate As DateTime?) As IReadOnlyList(Of MeasurementData) Implements IMeasurementDataRepository.Search
            ValidationGate?.Invoke()
            Return _inner.Search(id, item, fromDate, toDate)
        End Function
        Public Sub AddRange(rows As IEnumerable(Of MeasurementData)) Implements IMeasurementDataRepository.AddRange
            _inner.AddRange(GatedRows(rows))
        End Sub
        Private Iterator Function GatedRows(rows As IEnumerable(Of MeasurementData)) As IEnumerable(Of MeasurementData)
            For Each row In rows
                Yield row
                ' The real repository has inserted this row, but has not committed yet.
                TransactionGate?.Invoke()
            Next
        End Function
        Public Function FindById(id As Long) As MeasurementData Implements IMeasurementDataRepository.FindById
            Return _inner.FindById(id)
        End Function
        Public Sub Add(row As MeasurementData) Implements IMeasurementDataRepository.Add
            _inner.Add(row)
        End Sub
        Public Sub Update(row As MeasurementData) Implements IMeasurementDataRepository.Update
            _inner.Update(row)
        End Sub
        Public Sub Delete(ids As IEnumerable(Of Long)) Implements IMeasurementDataRepository.Delete
            _inner.Delete(ids)
        End Sub
        Public Function IsItemNameInUse(item As String) As Boolean Implements IMeasurementDataRepository.IsItemNameInUse
            Return _inner.IsItemNameInUse(item)
        End Function
    End Class
End Namespace
