Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Infrastructure.Repositories

Namespace TestSupport
    Friend Class Phase4Fixture
        Implements IDisposable
        Public ReadOnly Database As New Phase2Database()
        Public ReadOnly Root As String
        Public ReadOnly Files As New Phase4FileStore()
        Public ReadOnly Journal As New Phase4Journal()
        Public ReadOnly Clock As New ManualTimeProvider()
        Public ReadOnly Executor As Phase4Executor
        Public ReadOnly Options As PeriodicImportOptionsDto
        Public Cycle As PeriodicImportCycle

        Public Sub New()
            Database.Initialize()
            Root = Path.Combine(Database.DirectoryPath, "watch")
            Directory.CreateDirectory(Root)
            Clock.SetUtcNow(New DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero))
            Dim repository As New CsvImportConfigRepository(Database.Factory)
            Dim config = repository.GetAll()(0)
            config.WatchFolderPath = Root
            config.WatchIntervalSeconds = 1
            config.Encoding = "UTF-8"
            Options = New PeriodicImportOptionsDto With {.Config = config, .ColumnMappings = repository.GetMappings(config.ConfigId).ToList(),
                .DatabaseIdentity = Database.Factory.DatabasePath, .WatchIntervalSeconds = 1}
            Executor = New Phase4Executor(New PeriodicCsvImportExecutor(Database.Factory))
            Recreate()
        End Sub
        Public Sub Recreate()
            If Cycle IsNot Nothing Then Cycle.CleanupAsync().GetAwaiter().GetResult()
            Cycle = New PeriodicImportCycle(Files, Journal, Executor, Clock)
        End Sub
        Public Function Write(name As String, Optional index As Integer = 0, Optional value As String = "10") As String
            Dim file = Path.Combine(Root, name)
            FileSystemWrite(file, "日時,設備ID,設備名,項目,値,単位" & vbCrLf & $"2026-09-21T09:{index:00}:00.0000000Z,EQ001,設備,温度,{value},℃")
            IO.File.SetLastWriteTimeUtc(file, New DateTime(2026, 1, 1, 0, index, 0, DateTimeKind.Utc))
            Return file
        End Function
        Private Shared Sub FileSystemWrite(pathValue As String, text As String)
            IO.File.WriteAllText(pathValue, text)
        End Sub
        Public Sub Prepare()
            Cycle.PrepareAsync(Options, CancellationToken.None).GetAwaiter().GetResult()
        End Sub
        Public Sub Run()
            Cycle.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult()
        End Sub
        Public Function State() As ImportJournalStateDto
            Return Journal.Load(Root)
        End Function
        Public Function DataCount() As Long
            Return CLng(Database.Scalar("SELECT COUNT(*) FROM MeasurementData;"))
        End Function
        Public Function Bind(name As String) As CorrectionBindingDto
            Dim state = Me.State()
            Dim order = state.Orders.Single(Function(o) o.HeadDisposition <> HeadDisposition.Resolved)
            Cycle.CleanupAsync().GetAwaiter().GetResult()
            Return New PeriodicImportRecoveryService(Files, Journal, Function() True, Clock).ConfirmCorrection(Options, order.LatestFailedAttemptId, name)
        End Function
        Public Sub Dispose() Implements IDisposable.Dispose
            Cycle.CleanupAsync().GetAwaiter().GetResult()
            Database.Dispose()
        End Sub
    End Class

    Friend Class Phase4Executor
        Implements IPeriodicCsvImportExecutor
        Private ReadOnly _inner As IPeriodicCsvImportExecutor
        Public ReadOnly Calls As New List(Of String)()
        Public Before As Action(Of CsvImportRequestDto)
        Public OverrideResult As Func(Of CsvImportRequestDto, PeriodicFileResultDto)
        Public After As Action(Of PeriodicFileResultDto)
        Public Sub New(inner As IPeriodicCsvImportExecutor)
            _inner = inner
        End Sub
        Public ReadOnly Property DatabaseIdentity As String Implements IPeriodicCsvImportExecutor.DatabaseIdentity
            Get
                Return _inner.DatabaseIdentity
            End Get
        End Property
        Public Function Execute(request As CsvImportRequestDto) As PeriodicFileResultDto Implements IPeriodicCsvImportExecutor.Execute
            Calls.Add(Path.GetFileName(request.FilePath))
            Before?.Invoke(request)
            Dim result = If(OverrideResult Is Nothing, _inner.Execute(request), OverrideResult(request))
            After?.Invoke(result)
            Return result
        End Function
    End Class

    Friend Class Phase4FileStore
        Implements IPeriodicImportFileStore
        Public ReadOnly Inner As New PeriodicImportFileStore()
        Public BeforeMove As Action(Of String, String, String)
        Public BeforePrepare As Action
        Public AfterOwner As Action
        Public MoveCalls As Integer
        Public Function NormalizeRoot(root As String) As String Implements IPeriodicImportFileStore.NormalizeRoot
            Return Inner.NormalizeRoot(root)
        End Function
        Public Sub Prepare(root As String) Implements IPeriodicImportFileStore.Prepare
            BeforePrepare?.Invoke()
            Inner.Prepare(root)
        End Sub
        Public Function AcquireOwner(root As String) As IDisposable Implements IPeriodicImportFileStore.AcquireOwner
            Dim owner = Inner.AcquireOwner(root)
            Try
                AfterOwner?.Invoke()
                Return owner
            Catch
                owner.Dispose()
                Throw
            End Try
        End Function
        Public Function Enumerate(root As String) As IReadOnlyList(Of PeriodicOrderEntryDto) Implements IPeriodicImportFileStore.Enumerate
            Return Inner.Enumerate(root)
        End Function
        Public Function TryRead(root As String, relativePath As String, Optional directOnly As Boolean = True) As IPeriodicReadLease Implements IPeriodicImportFileStore.TryRead
            Return Inner.TryRead(root, relativePath, directOnly)
        End Function
        Public Function Exists(root As String, relativePath As String) As Boolean Implements IPeriodicImportFileStore.Exists
            Return Inner.Exists(root, relativePath)
        End Function
        Public Function Destination(root As String, originalName As String, succeeded As Boolean, candidate As Integer, utc As DateTimeOffset) As String Implements IPeriodicImportFileStore.Destination
            Return Inner.Destination(root, originalName, succeeded, candidate, utc)
        End Function
        Public Function Move(root As String, source As String, destination As String) As Boolean Implements IPeriodicImportFileStore.Move
            MoveCalls += 1
            BeforeMove?.Invoke(root, source, destination)
            Return Inner.Move(root, source, destination)
        End Function
    End Class

    Friend Class Phase4Journal
        Implements IImportProcessingJournal
        Public ReadOnly Inner As New JsonImportProcessingJournal()
        Public BeforeAttempt As Action(Of ImportProcessingRecordDto)
        Public AfterAttempt As Action(Of ImportProcessingRecordDto)
        Public BeforeOrder As Action(Of PeriodicImportOrderDto)
        Public AfterOrder As Action(Of PeriodicImportOrderDto)
        Public ReadOnly Stages As New List(Of ProcessingStage)()
        Public Function Load(root As String) As ImportJournalStateDto Implements IImportProcessingJournal.Load
            Return Inner.Load(root)
        End Function
        Public Sub SaveOrder(root As String, order As PeriodicImportOrderDto) Implements IImportProcessingJournal.SaveOrder
            BeforeOrder?.Invoke(order)
            Inner.SaveOrder(root, order)
            AfterOrder?.Invoke(order)
        End Sub
        Public Sub SaveAttempt(root As String, attempt As ImportProcessingRecordDto) Implements IImportProcessingJournal.SaveAttempt
            BeforeAttempt?.Invoke(attempt)
            Inner.SaveAttempt(root, attempt)
            Stages.Add(attempt.Stage)
            AfterAttempt?.Invoke(attempt)
        End Sub
        Public Sub SaveBinding(root As String, binding As CorrectionBindingDto) Implements IImportProcessingJournal.SaveBinding
            Inner.SaveBinding(root, binding)
        End Sub
        Public Sub Prune(root As String, state As ImportJournalStateDto, now As DateTimeOffset) Implements IImportProcessingJournal.Prune
            Inner.Prune(root, state, now)
        End Sub
    End Class
End Namespace
