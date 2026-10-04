Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports System.IO
Imports System.Diagnostics

Namespace Services
    Public Class CsvImportService
        Private ReadOnly _csvAdapter As ICsvFileAdapter
        Private ReadOnly _validator As ValidationService
        Private ReadOnly _measurementItemRepository As IMeasurementItemRepository
        Private ReadOnly _measurementDataRepository As IMeasurementDataRepository
        Private ReadOnly _logWriter As IImportLogWriter
        Private ReadOnly _equipmentRepository As IEquipmentRepository

        Private Enum SavePolicy
            AllowPartial
            AllOrNothing
        End Enum

        Public Sub New(csvAdapter As ICsvFileAdapter, validator As ValidationService, measurementItemRepository As IMeasurementItemRepository)
            _csvAdapter = csvAdapter
            _validator = validator
            _measurementItemRepository = measurementItemRepository
        End Sub

        Public Sub New(csvAdapter As ICsvFileAdapter, validator As ValidationService, measurementItemRepository As IMeasurementItemRepository, measurementDataRepository As IMeasurementDataRepository, logWriter As IImportLogWriter)
            Me.New(csvAdapter, validator, measurementItemRepository)
            _measurementDataRepository = measurementDataRepository
            _logWriter = logWriter
        End Sub

        Public Sub New(csvAdapter As ICsvFileAdapter, validator As ValidationService, measurementItemRepository As IMeasurementItemRepository, measurementDataRepository As IMeasurementDataRepository, logWriter As IImportLogWriter, equipmentRepository As IEquipmentRepository)
            Me.New(csvAdapter, validator, measurementItemRepository, measurementDataRepository, logWriter)
            _equipmentRepository = equipmentRepository
        End Sub

        Public Function ReadRaw(filePath As String, encodingName As String, delimiter As String, hasHeader As Boolean) As IReadOnlyList(Of IReadOnlyDictionary(Of String, String))
            Return _csvAdapter.Read(filePath, encodingName, delimiter, hasHeader)
        End Function

        Public Function Import(request As CsvImportRequestDto) As ValidationResultDto
            Dim result As New ValidationResultDto()
            Dim rows = _csvAdapter.Read(request.FilePath, request.Config.Encoding, request.Config.Delimiter, request.Config.HasHeader)
            result.SourceRowCount = rows.Count
            Dim mappings = request.ColumnMappings.ToDictionary(Function(mapping) mapping.FieldName, Function(mapping) mapping.CsvColumnIndex)
            Dim knownItems = _measurementItemRepository.GetAll().ToDictionary(Function(item) item.ItemName, StringComparer.Ordinal)
            Dim seenKeys As New HashSet(Of String)(StringComparer.Ordinal)

            For index = 0 To rows.Count - 1
                Dim rowNumber = index + If(request.Config.HasHeader, 2, 1)
                Dim values = MapRow(rows(index), mappings)
                Dim errors = Validate(values, rowNumber, Path.GetFileName(request.FilePath), knownItems, seenKeys)
                result.Errors.AddRange(errors)
                If errors.Count = 0 Then
                    Dim measurement = New MeasurementData With {
                        .MeasuredAt = DateTime.Parse(values(StandardFields.AcquiredAt), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind),
                        .EquipmentId = values(StandardFields.EquipmentId), .EquipmentName = values(StandardFields.EquipmentName),
                        .ItemName = values(StandardFields.ItemName), .Value = Double.Parse(values(StandardFields.Value), Globalization.CultureInfo.InvariantCulture), .Unit = values(StandardFields.Unit)}
                    result.ValidMeasurements.Add(measurement)
                    result.ValidCandidates.Add(New CsvImportCandidateDto With {.RowNumber = rowNumber, .Measurement = measurement})
                End If
            Next
            Return result
        End Function

        Public Function ImportAndSave(request As CsvImportRequestDto) As CsvImportExecutionResultDto
            Return ImportAndSaveCore(request, SavePolicy.AllowPartial)
        End Function

        Public Function ImportAndSaveAtomic(request As CsvImportRequestDto) As CsvImportExecutionResultDto
            Return ImportAndSaveCore(request, SavePolicy.AllOrNothing)
        End Function

        Private Function ImportAndSaveCore(request As CsvImportRequestDto, policy As SavePolicy) As CsvImportExecutionResultDto
            If _measurementDataRepository Is Nothing OrElse _logWriter Is Nothing Then Throw New InvalidOperationException("CSV取込のDB登録にはRepositoryとログライターが必要です。")
            If policy = SavePolicy.AllOrNothing AndAlso (_equipmentRepository Is Nothing OrElse _csvAdapter Is Nothing OrElse _validator Is Nothing OrElse _measurementItemRepository Is Nothing) Then
                Throw New InvalidOperationException("Atomic取込にはCSV・検証・測定項目・設備Repositoryの構成が必要です。")
            End If

            Dim timer = Stopwatch.StartNew()
            Dim validationResult = Import(request)
            Dim candidates = ExcludeDatabaseDuplicates(validationResult, Path.GetFileName(request.FilePath))
            If policy = SavePolicy.AllOrNothing Then
                candidates = ExcludeUnknownEquipment(candidates, validationResult, Path.GetFileName(request.FilePath))
                Dim errorRows = validationResult.Errors.Where(Function(e) e.RowNumber.HasValue).Select(Function(e) e.RowNumber.Value).Distinct().Count()
                If candidates.Count + errorRows <> validationResult.SourceRowCount OrElse validationResult.Errors.Any(Function(e) Not e.RowNumber.HasValue) Then
                    Throw New InvalidOperationException("Atomic取込の行数に内部不整合があります。登録を中止します。")
                End If
            End If

            Dim rejected = policy = SavePolicy.AllOrNothing AndAlso validationResult.Errors.Count > 0
            Dim registeredCount = If(rejected, 0, candidates.Count)
            ' Keep the manual empty-list call unchanged. Atomic rejection/empty CSV never calls AddRange.
            If policy = SavePolicy.AllowPartial OrElse (Not rejected AndAlso candidates.Count > 0) Then
                _measurementDataRepository.AddRange(candidates.Select(Function(c) c.Measurement).ToList())
            End If
            timer.Stop()
            Dim executionResult = New CsvImportExecutionResultDto With {
                .TotalCount = validationResult.SourceRowCount, .SuccessCount = registeredCount,
                .FailureCount = validationResult.SourceRowCount - registeredCount,
                .ElapsedMs = timer.ElapsedMilliseconds, .ValidationResult = validationResult,
                .HeldValidRowCount = If(rejected, candidates.Count, 0)}
            RecordLogs(request, executionResult)
            Return executionResult
        End Function

        Private Function ExcludeDatabaseDuplicates(validationResult As ValidationResultDto, fileName As String) As List(Of CsvImportCandidateDto)
            Dim candidates As New List(Of CsvImportCandidateDto)()
            For Each candidate In validationResult.ValidCandidates
                Dim measurement = candidate.Measurement
                Dim existing = _measurementDataRepository.Search(measurement.EquipmentId, measurement.ItemName, measurement.MeasuredAt, measurement.MeasuredAt.AddTicks(1))
                If existing.Count = 0 Then
                    candidates.Add(candidate)
                Else
                    validationResult.Errors.Add(New ValidationErrorDto With {.RowNumber = candidate.RowNumber, .FieldName = StandardFields.AcquiredAt, .ErrorType = ErrorTypes.Duplicate, .Message = "DBに既に同一の測定データが存在します。", .FileName = fileName})
                End If
            Next
            Return candidates
        End Function

        Private Function ExcludeUnknownEquipment(candidates As IEnumerable(Of CsvImportCandidateDto), validationResult As ValidationResultDto, fileName As String) As List(Of CsvImportCandidateDto)
            Dim accepted As New List(Of CsvImportCandidateDto)()
            For Each candidate In candidates
                If _equipmentRepository.FindById(candidate.Measurement.EquipmentId) Is Nothing Then
                    validationResult.Errors.Add(New ValidationErrorDto With {.RowNumber = candidate.RowNumber, .FieldName = StandardFields.EquipmentId, .ErrorType = ErrorTypes.System, .Message = "設備マスタに存在しない設備IDです。", .FileName = fileName})
                Else
                    accepted.Add(candidate)
                End If
            Next
            Return accepted
        End Function

        Private Sub RecordLogs(request As CsvImportRequestDto, executionResult As CsvImportExecutionResultDto)
            Dim validationResult = executionResult.ValidationResult
            Dim configId As Integer? = Nothing
            If request.Config.ConfigId <> 0 Then configId = request.Config.ConfigId
            Try
                _logWriter.WriteErrors(validationResult.Errors.Select(Function(errorItem) New ErrorLog With {.OccurredAt = DateTime.UtcNow, .ConfigId = configId, .FileName = errorItem.FileName, .RowNumber = errorItem.RowNumber, .FieldName = errorItem.FieldName, .ErrorType = errorItem.ErrorType, .ErrorMessage = errorItem.Message}))
                _logWriter.WriteOperation(New OperationLog With {.ExecutedAt = DateTime.UtcNow, .OperationType = "CSV取込", .ElapsedMs = executionResult.ElapsedMs, .TotalCount = executionResult.TotalCount, .SuccessCount = executionResult.SuccessCount, .FailureCount = executionResult.FailureCount})
                executionResult.LogRecorded = True
            Catch
                executionResult.LogRecorded = False
                executionResult.LogErrorMessage = "ログの記録に失敗しました。"
            End Try
        End Sub

        Private Function Validate(values As Dictionary(Of String, String), rowNumber As Integer, fileName As String, knownItems As Dictionary(Of String, MeasurementItemMaster), seenKeys As HashSet(Of String)) As List(Of ValidationErrorDto)
            Dim errors As New List(Of ValidationErrorDto)()
            Dim requiredFields = {StandardFields.AcquiredAt, StandardFields.EquipmentId, StandardFields.EquipmentName, StandardFields.ItemName, StandardFields.Value, StandardFields.Unit}
            For Each fieldName In requiredFields
                AddError(errors, _validator.ValidateRequired(values(fieldName), fieldName, rowNumber), fileName)
            Next
            If errors.Count > 0 Then Return errors

            AddError(errors, _validator.ValidateDateTime(values(StandardFields.AcquiredAt), StandardFields.AcquiredAt, rowNumber), fileName)
            AddError(errors, _validator.ValidateDouble(values(StandardFields.Value), StandardFields.Value, rowNumber), fileName)
            If errors.Count > 0 Then Return errors

            Dim measuredAt = DateTime.Parse(values(StandardFields.AcquiredAt), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind)
            Dim key = $"{values(StandardFields.EquipmentId)}|{values(StandardFields.ItemName)}|{measuredAt:O}"
            If Not seenKeys.Add(key) Then errors.Add(New ValidationErrorDto With {.RowNumber = rowNumber, .FieldName = StandardFields.AcquiredAt, .ErrorType = ErrorTypes.Duplicate, .Message = "CSV内で重複する測定データです。", .FileName = fileName})

            Dim item As MeasurementItemMaster = Nothing
            If knownItems.TryGetValue(values(StandardFields.ItemName), item) Then
                AddError(errors, _validator.ValidateRange(Double.Parse(values(StandardFields.Value), Globalization.CultureInfo.InvariantCulture), item.MinValue, item.MaxValue, StandardFields.Value, rowNumber), fileName)
            End If
            Return errors
        End Function

        Private Shared Function MapRow(row As IReadOnlyDictionary(Of String, String), mappings As Dictionary(Of String, Integer)) As Dictionary(Of String, String)
            Dim values As New Dictionary(Of String, String)(StringComparer.Ordinal)
            For Each fieldName In {StandardFields.AcquiredAt, StandardFields.EquipmentId, StandardFields.EquipmentName, StandardFields.ItemName, StandardFields.Value, StandardFields.Unit}
                Dim columnIndex As Integer
                Dim columnValue As String = Nothing
                If mappings.TryGetValue(fieldName, columnIndex) AndAlso columnIndex > 0 Then
                    row.TryGetValue($"Column{columnIndex}", columnValue)
                End If
                values(fieldName) = columnValue
            Next
            Return values
        End Function

        Private Shared Sub AddError(errors As List(Of ValidationErrorDto), validationError As ValidationErrorDto, fileName As String)
            If validationError Is Nothing Then Return
            validationError.FileName = fileName
            errors.Add(validationError)
        End Sub
    End Class
End Namespace
