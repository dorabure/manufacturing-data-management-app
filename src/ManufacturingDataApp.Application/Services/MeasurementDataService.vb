Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities

Namespace Services
    Public Class MeasurementDataService
        Private ReadOnly _repository As IMeasurementDataRepository
        Private ReadOnly _itemRepository As IMeasurementItemRepository
        Private ReadOnly _validator As ValidationService
        Private ReadOnly _logWriter As IImportLogWriter

        Public Sub New(repository As IMeasurementDataRepository)
            _repository = repository
            _validator = New ValidationService()
        End Sub

        Public Sub New(repository As IMeasurementDataRepository, itemRepository As IMeasurementItemRepository, validator As ValidationService, logWriter As IImportLogWriter)
            _repository = repository
            _itemRepository = itemRepository
            _validator = validator
            _logWriter = logWriter
        End Sub
        Public Function Search(equipmentId As String, itemName As String, fromDate As DateTime?, toDate As DateTime?) As IReadOnlyList(Of MeasurementData)
            Return _repository.Search(equipmentId, itemName, fromDate, toDate)
        End Function
        Public Sub Delete(ids As IEnumerable(Of Long))
            Dim selectedIds = ids.Distinct().ToList()
            If selectedIds.Count = 0 Then Return
            _repository.Delete(selectedIds)
            WriteOperation("削除", selectedIds.Count)
        End Sub

        Public Function FindById(id As Long) As MeasurementData
            Return _repository.FindById(id)
        End Function

        Public Function ValidateForUpdate(entity As MeasurementData) As IReadOnlyList(Of ValidationErrorDto)
            Dim errors As New List(Of ValidationErrorDto)()
            AddError(errors, _validator.ValidateRequired(entity.EquipmentId, "設備ID", 0))
            AddError(errors, _validator.ValidateRequired(entity.EquipmentName, "設備名", 0))
            AddError(errors, _validator.ValidateRequired(entity.ItemName, "項目名", 0))
            AddError(errors, _validator.ValidateRequired(entity.Unit, "単位", 0))
            If entity.MeasuredAt = DateTime.MinValue Then
                errors.Add(New ValidationErrorDto With {.FieldName = "取得日時", .ErrorType = ErrorTypes.Type, .Message = "取得日時は日時である必要があります。"})
            End If
            If Double.IsNaN(entity.Value) OrElse Double.IsInfinity(entity.Value) Then
                errors.Add(New ValidationErrorDto With {.FieldName = "測定値", .ErrorType = ErrorTypes.Type, .Message = "測定値は数値である必要があります。"})
            End If
            If errors.Count > 0 Then Return errors

            If _itemRepository IsNot Nothing Then
                Dim item = _itemRepository.GetAll().FirstOrDefault(Function(candidate) candidate.ItemName = entity.ItemName)
                If item IsNot Nothing Then AddError(errors, _validator.ValidateRange(entity.Value, item.MinValue, item.MaxValue, "測定値", 0))
            End If

            Dim sameKeyRecords = _repository.Search(entity.EquipmentId, entity.ItemName, entity.MeasuredAt, entity.MeasuredAt.AddTicks(1))
            If sameKeyRecords.Any(Function(record) record.Id <> entity.Id) Then
                errors.Add(New ValidationErrorDto With {.FieldName = "取得日時", .ErrorType = ErrorTypes.Duplicate, .Message = "同一の設備ID、項目名、取得日時のデータが既に存在します。"})
            End If
            Return errors
        End Function

        Public Sub Update(entity As MeasurementData)
            Dim errors = ValidateForUpdate(entity)
            If errors.Count > 0 Then Throw New InvalidOperationException("入力内容に問題があります。")
            _repository.Update(entity)
            WriteOperation("編集", 1)
        End Sub

        Private Shared Sub AddError(errors As List(Of ValidationErrorDto), validationError As ValidationErrorDto)
            If validationError IsNot Nothing Then errors.Add(validationError)
        End Sub

        Private Sub WriteOperation(operationType As String, successCount As Integer)
            If _logWriter Is Nothing Then Return
            _logWriter.WriteOperation(New OperationLog With {.ExecutedAt = DateTime.UtcNow, .OperationType = operationType, .TotalCount = successCount, .SuccessCount = successCount, .FailureCount = 0, .ElapsedMs = 0})
        End Sub
    End Class
End Namespace
