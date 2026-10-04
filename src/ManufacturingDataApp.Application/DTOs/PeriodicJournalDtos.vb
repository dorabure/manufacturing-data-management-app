Namespace DTOs
    Public Enum ProcessingStage
        ImportStarted
        ResultKnown
        MovePending
        Completed
        RecoveryRequired
    End Enum

    Public Enum HeadDisposition
        Ready
        WaitingForReadable
        CorrectionPending
        BoundCorrection
        RecoveryRequired
        Resolved
    End Enum

    Public Class FileFingerprintDto
        Public Property RelativePath As String
        Public Property Length As Long
        Public Property LastWriteTimeUtc As DateTime
        Public Property Sha256 As String
        Public Property FileId As String

        Public Function Matches(other As FileFingerprintDto, Optional comparePath As Boolean = True) As Boolean
            Return other IsNot Nothing AndAlso
                (Not comparePath OrElse String.Equals(RelativePath, other.RelativePath, StringComparison.OrdinalIgnoreCase)) AndAlso
                Length = other.Length AndAlso LastWriteTimeUtc = other.LastWriteTimeUtc AndAlso
                String.Equals(Sha256, other.Sha256, StringComparison.Ordinal) AndAlso
                (FileId Is Nothing OrElse String.Equals(FileId, other.FileId, StringComparison.Ordinal))
        End Function
    End Class

    Public Class PeriodicOrderEntryDto
        Public Property Position As Integer
        Public Property RelativePath As String
        Public Property OriginalFileName As String
        Public Property LastWriteTimeUtc As DateTime
        Public Property ObservedLength As Long
        Public Property FileId As String
        Public Property AttemptId As String
    End Class

    Public Class ImportProcessingRecordDto
        Public Property Version As Integer = 1
        Public Property AttemptId As String = Guid.NewGuid().ToString("N")
        Public Property OrderId As String
        Public Property Position As Integer
        Public Property RootFailedAttemptId As String
        Public Property LatestFailedAttemptId As String
        Public Property CorrectionBindingId As String
        Public Property Root As String
        Public Property DatabaseIdentity As String
        Public Property OriginalFileName As String
        Public Property Fingerprint As FileFingerprintDto
        Public Property Snapshot As PeriodicImportOptionsDto
        Public Property ConfigHash As String
        Public Property StartedAt As DateTimeOffset
        Public Property Stage As ProcessingStage
        Public Property Result As New PeriodicFileResultDto()
        Public Property Destination As String
        Public Property CompletedAt As DateTimeOffset?
        Public Property SafeReason As String
    End Class

    Public Class PeriodicImportOrderDto
        Public Property Version As Integer = 1
        Public Property Generation As Long
        Public Property OrderId As String = Guid.NewGuid().ToString("N")
        Public Property Root As String
        Public Property DatabaseIdentity As String
        Public Property Snapshot As PeriodicImportOptionsDto
        Public Property ConfigHash As String
        Public Property Entries As New List(Of PeriodicOrderEntryDto)()
        Public Property Cursor As Integer
        Public Property HeadDisposition As HeadDisposition
        Public Property RootFailedAttemptId As String
        Public Property LatestFailedAttemptId As String
        Public Property CorrectionBindingId As String
        Public Property ResolvedAt As DateTimeOffset?
    End Class

    Public Class CorrectionBindingDto
        Public Property Version As Integer = 1
        Public Property CorrectionBindingId As String = Guid.NewGuid().ToString("N")
        Public Property OrderId As String
        Public Property Position As Integer
        Public Property FailedAttemptId As String
        Public Property Candidate As FileFingerprintDto
        Public Property ConfigHash As String
        Public Property ConfirmedAt As DateTimeOffset
        Public Property Confirmed As Boolean
        Public Property Invalidated As Boolean
        Public Property UsedAttemptId As String
    End Class

    Public Class ImportJournalStateDto
        Public Property Orders As New List(Of PeriodicImportOrderDto)()
        Public Property Attempts As New List(Of ImportProcessingRecordDto)()
        Public Property Bindings As New List(Of CorrectionBindingDto)()
    End Class
End Namespace

