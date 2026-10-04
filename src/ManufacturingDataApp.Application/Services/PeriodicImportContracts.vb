Imports System.Security.Cryptography
Imports System.Text.Json
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Domain

Namespace Services
    Public Enum PeriodicStopReason
        RecoveryRequired
        CorrectionPending
        MovePending
        LogFailure
        RecoveryCompleted
        ReconfirmationRequired
    End Enum

    Public Class PeriodicImportStopRequiredException
        Inherits Exception
        Public ReadOnly Property Reason As PeriodicStopReason
        Public Sub New(reason As PeriodicStopReason, Optional inner As Exception = Nothing)
            MyBase.New(reason.ToString(), inner)
            Me.Reason = reason
        End Sub
    End Class

    Public NotInheritable Class PeriodicSnapshot
        Public Shared Function Hash(options As PeriodicImportOptionsDto) As String
            Dim copy = options.DeepCopy()
            copy.ColumnMappings = copy.ColumnMappings.OrderBy(Function(m) m.FieldName, StringComparer.Ordinal).ToList()
            Return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(copy)))
        End Function

        Public Shared Sub Validate(options As PeriodicImportOptionsDto)
            If options Is Nothing OrElse options.Config Is Nothing OrElse options.ColumnMappings Is Nothing Then Throw New ArgumentException("CSV設定が必要です。")
            Dim c = options.Config
            Dim fields = {StandardFields.AcquiredAt, StandardFields.EquipmentId, StandardFields.EquipmentName, StandardFields.ItemName, StandardFields.Value, StandardFields.Unit}
            If c.ConfigId <= 0 OrElse String.IsNullOrWhiteSpace(c.ConfigName) OrElse String.IsNullOrWhiteSpace(c.Encoding) OrElse String.IsNullOrWhiteSpace(c.Delimiter) OrElse
                String.IsNullOrWhiteSpace(c.WatchFolderPath) OrElse String.IsNullOrWhiteSpace(options.DatabaseIdentity) OrElse
                options.WatchIntervalSeconds < 1 OrElse options.WatchIntervalSeconds > 604800 OrElse c.WatchIntervalSeconds <> options.WatchIntervalSeconds OrElse
                Not [Enum].IsDefined(c.ImportMode) OrElse options.ColumnMappings.Count <> 6 OrElse
                fields.Any(Function(f) options.ColumnMappings.Where(Function(m) m.FieldName = f).Count() <> 1) OrElse
                options.ColumnMappings.Any(Function(m) m.CsvColumnIndex < 1 OrElse m.ConfigId <> c.ConfigId) OrElse
                options.ColumnMappings.Select(Function(m) m.CsvColumnIndex).Distinct().Count() <> 6 Then Throw New ArgumentException("周期取込設定が不正です。")
        End Sub
    End Class
End Namespace
