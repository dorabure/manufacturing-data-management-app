Imports System.IO
Imports CsvHelper
Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Csv
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Logging
Imports ManufacturingDataApp.Infrastructure.Repositories

Namespace PeriodicImport
    Public Class PeriodicCsvImportExecutor
        Implements IPeriodicCsvImportExecutor
        Private ReadOnly _service As CsvImportService
        Private ReadOnly _logger As IImportLogWriter
        Private ReadOnly _time As TimeProvider
        Public ReadOnly Property DatabaseIdentity As String Implements IPeriodicCsvImportExecutor.DatabaseIdentity

        Public Sub New(factory As DatabaseConnectionFactory, Optional adapter As ICsvFileAdapter = Nothing,
                       Optional data As IMeasurementDataRepository = Nothing, Optional equipment As IEquipmentRepository = Nothing,
                       Optional logger As IImportLogWriter = Nothing, Optional time As TimeProvider = Nothing)
            ArgumentNullException.ThrowIfNull(factory)
            DatabaseIdentity = factory.DatabasePath
            _time = If(time, TimeProvider.System)
            _logger = New PeriodicImportLogWriter(If(logger, New DbLogWriter(factory)))
            _service = New CsvImportService(New ReadBoundaryAdapter(If(adapter, New CsvHelperAdapter())),
                New ValidationService(), New MeasurementItemRepository(factory),
                If(data, New MeasurementDataRepository(factory)), _logger, If(equipment, New EquipmentRepository(factory)))
        End Sub

        Public Function Execute(request As CsvImportRequestDto) As PeriodicFileResultDto Implements IPeriodicCsvImportExecutor.Execute
            Try
                Dim result = _service.ImportAndSaveAtomic(request)
                Return New PeriodicFileResultDto With {
                    .Outcome = If(result.ValidationResult.Errors.Count = 0, PeriodicFileOutcome.Succeeded, PeriodicFileOutcome.Rejected),
                    .DbOutcomeKnown = True, .TotalCount = result.TotalCount, .RegisteredCount = result.SuccessCount,
                    .UnregisteredCount = result.FailureCount, .ValidationErrorRowCount = result.ValidationErrorRowCount,
                    .HeldValidRowCount = result.HeldValidRowCount, .LogRecorded = result.LogRecorded}
            Catch ex As BeforeDbReadException
                Return Failure(request, PeriodicFileOutcome.ReadFailed, "[CSVRead] CSV読込に失敗しました。")
            Catch ex As SqliteException
                Return Failure(request, PeriodicFileOutcome.DatabaseFailed, "[DB] DB結果を確定できません。")
            Catch ex As Exception
                Return Failure(request, PeriodicFileOutcome.Unknown, "[RecoveryRequired] 取込結果を確定できません。")
            End Try
        End Function

        Private Function Failure(request As CsvImportRequestDto, outcome As PeriodicFileOutcome, safeMessage As String) As PeriodicFileResultDto
            Dim result As New PeriodicFileResultDto With {.Outcome = outcome, .DbOutcomeKnown = outcome = PeriodicFileOutcome.ReadFailed}
            Try
                _logger.WriteErrors({New ErrorLog With {.OccurredAt = _time.GetUtcNow().UtcDateTime,
                    .ConfigId = If(request.Config.ConfigId > 0, CType(request.Config.ConfigId, Integer?), Nothing),
                    .FileName = Path.GetFileName(request.FilePath), .ErrorType = ErrorTypes.System, .ErrorMessage = safeMessage}})
                _logger.WriteOperation(New OperationLog With {.ExecutedAt = _time.GetUtcNow().UtcDateTime,
                    .OperationType = If(result.DbOutcomeKnown, "周期CSV取込失敗", "周期CSV取込結果不明")})
                result.LogRecorded = True
            Catch
                result.LogRecorded = False
            End Try
            Return result
        End Function

        Private Class BeforeDbReadException
            Inherits Exception
            Public Sub New(inner As Exception)
                MyBase.New("CSV read failed before repositories.", inner)
            End Sub
        End Class

        Private Class ReadBoundaryAdapter
            Implements ICsvFileAdapter
            Private ReadOnly _inner As ICsvFileAdapter
            Public Sub New(inner As ICsvFileAdapter)
                _inner = inner
            End Sub
            Public Function Read(path As String, encoding As String, delimiter As String, header As Boolean) As IReadOnlyList(Of IReadOnlyDictionary(Of String, String)) Implements ICsvFileAdapter.Read
                Try
                    Return _inner.Read(path, encoding, delimiter, header)
                Catch ex As Exception When TypeOf ex Is CsvHelperException OrElse TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                    Throw New BeforeDbReadException(ex)
                End Try
            End Function
            Public Sub Write(path As String, rows As IEnumerable(Of IReadOnlyDictionary(Of String, String)), encoding As String, delimiter As String, header As Boolean) Implements ICsvFileAdapter.Write
                Throw New NotSupportedException()
            End Sub
        End Class
    End Class
End Namespace

