Imports ManufacturingDataApp.Application.DTOs

Namespace Interfaces
    Public Interface IPeriodicReadLease
        Inherits IDisposable
        ReadOnly Property Fingerprint As FileFingerprintDto
        ReadOnly Property FullPath As String
    End Interface

    Public Interface IPeriodicImportFileStore
        Function NormalizeRoot(root As String) As String
        Sub Prepare(root As String)
        Function AcquireOwner(root As String) As IDisposable
        Function Enumerate(root As String) As IReadOnlyList(Of PeriodicOrderEntryDto)
        ' Nothing means sharing/lock violation ONLY.
        Function TryRead(root As String, relativePath As String, Optional directOnly As Boolean = True) As IPeriodicReadLease
        Function Exists(root As String, relativePath As String) As Boolean
        Function Destination(root As String, originalName As String, succeeded As Boolean, candidate As Integer, utc As DateTimeOffset) As String
        ' False means destination-name collision ONLY. Other errors propagate.
        Function Move(root As String, source As String, destination As String) As Boolean
    End Interface

    Public Interface IImportProcessingJournal
        Function Load(root As String) As ImportJournalStateDto
        Sub SaveOrder(root As String, order As PeriodicImportOrderDto)
        Sub SaveAttempt(root As String, attempt As ImportProcessingRecordDto)
        Sub SaveBinding(root As String, binding As CorrectionBindingDto)
        Sub Prune(root As String, state As ImportJournalStateDto, now As DateTimeOffset)
    End Interface

    Public Interface IPeriodicCsvImportExecutor
        ReadOnly Property DatabaseIdentity As String
        Function Execute(request As CsvImportRequestDto) As PeriodicFileResultDto
    End Interface
End Namespace

