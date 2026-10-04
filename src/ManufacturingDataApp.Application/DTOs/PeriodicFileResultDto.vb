Namespace DTOs
    Public Enum PeriodicFileOutcome
        Unknown
        Succeeded
        Rejected
        ReadFailed
        DatabaseFailed
    End Enum

    ' The future Infrastructure executor classifies exceptions. Zero counts alone
    ' never mean a known DB outcome; callers must inspect DbOutcomeKnown.
    Public Class PeriodicFileResultDto
        Public Property Outcome As PeriodicFileOutcome = PeriodicFileOutcome.Unknown
        Public Property TotalCount As Integer
        Public Property RegisteredCount As Integer
        Public Property UnregisteredCount As Integer
        Public Property ValidationErrorRowCount As Integer
        Public Property HeldValidRowCount As Integer
        Public Property LogRecorded As Boolean
        Public Property DbOutcomeKnown As Boolean
    End Class
End Namespace
