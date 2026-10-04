Imports System.Diagnostics
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Domain.Exceptions

Namespace Services
    Public Class MasterDataService
        Private ReadOnly _equipmentRepository As IEquipmentRepository
        Private ReadOnly _itemRepository As IMeasurementItemRepository
        Private ReadOnly _logWriter As IImportLogWriter
        Private ReadOnly _measurementDataRepository As IMeasurementDataRepository

        Public Sub New(equipmentRepository As IEquipmentRepository, itemRepository As IMeasurementItemRepository, logWriter As IImportLogWriter, measurementDataRepository As IMeasurementDataRepository)
            _equipmentRepository = equipmentRepository
            _itemRepository = itemRepository
            _logWriter = logWriter
            _measurementDataRepository = measurementDataRepository
        End Sub

        Public Function GetEquipment() As IReadOnlyList(Of EquipmentMaster)
            Return _equipmentRepository.GetAll()
        End Function
        Public Function GetMeasurementItems() As IReadOnlyList(Of MeasurementItemMaster)
            Return _itemRepository.GetAll()
        End Function

        Public Sub SaveEquipment(entity As EquipmentMaster, isNew As Boolean)
            If String.IsNullOrWhiteSpace(entity.EquipmentId) Then Throw New ArgumentException("設備IDを入力してください。")
            If String.IsNullOrWhiteSpace(entity.EquipmentName) Then Throw New ArgumentException("設備名を入力してください。")
            Dim existing = _equipmentRepository.FindById(entity.EquipmentId.Trim())
            If isNew AndAlso existing IsNot Nothing Then Throw New ArgumentException("同じ設備IDが既に存在します。")
            If Not isNew AndAlso existing Is Nothing Then Throw New InvalidOperationException("更新対象の設備が見つかりません。")
            entity.EquipmentId = entity.EquipmentId.Trim()
            entity.EquipmentName = entity.EquipmentName.Trim()
            entity.FactoryName = NormalizeOptional(entity.FactoryName)
            entity.LineName = NormalizeOptional(entity.LineName)
            If isNew Then
                entity.CreatedAt = DateTime.UtcNow
                ExecuteWithLog("設備マスタ登録", Sub() _equipmentRepository.Add(entity))
            Else
                ExecuteWithLog("設備マスタ更新", Sub() _equipmentRepository.Update(entity))
            End If
        End Sub

        Public Sub DeleteEquipment(equipmentId As String)
            If _equipmentRepository.FindById(equipmentId) Is Nothing Then Throw New InvalidOperationException("削除対象の設備が見つかりません。")
            ExecuteWithLog("設備マスタ削除", Sub() _equipmentRepository.Delete(equipmentId))
        End Sub

        Public Sub SaveMeasurementItem(entity As MeasurementItemMaster, isNew As Boolean)
            If String.IsNullOrWhiteSpace(entity.ItemName) Then Throw New ArgumentException("項目名を入力してください。")
            If String.IsNullOrWhiteSpace(entity.Unit) Then Throw New ArgumentException("単位を入力してください。")
            If entity.MinValue.HasValue AndAlso entity.MaxValue.HasValue AndAlso entity.MinValue.Value > entity.MaxValue.Value Then Throw New ArgumentException("最小値は最大値以下で入力してください。")
            Dim sameName = _itemRepository.GetAll().FirstOrDefault(Function(item) item.ItemName = entity.ItemName.Trim() AndAlso item.ItemId <> entity.ItemId)
            If sameName IsNot Nothing Then Throw New ArgumentException("同じ項目名が既に存在します。")
            If Not isNew AndAlso _itemRepository.FindById(entity.ItemId) Is Nothing Then Throw New InvalidOperationException("更新対象の測定項目が見つかりません。")
            If Not isNew Then entity.ItemName = _itemRepository.FindById(entity.ItemId).ItemName
            entity.ItemName = entity.ItemName.Trim()
            entity.Unit = entity.Unit.Trim()
            If isNew Then
                ExecuteWithLog("測定項目マスタ登録", Sub() _itemRepository.Add(entity))
            Else
                ExecuteWithLog("測定項目マスタ更新", Sub() _itemRepository.Update(entity))
            End If
        End Sub

        Public Sub DeleteMeasurementItem(itemId As Integer)
            Dim item = _itemRepository.FindById(itemId)
            If item Is Nothing Then Throw New InvalidOperationException("削除対象の測定項目が見つかりません。")
            If _measurementDataRepository IsNot Nothing AndAlso _measurementDataRepository.IsItemNameInUse(item.ItemName) Then Throw New MeasurementItemInUseException()
            ExecuteWithLog("測定項目マスタ削除", Sub() _itemRepository.Delete(itemId))
        End Sub

        Private Sub ExecuteWithLog(operationType As String, action As Action)
            Dim timer = Stopwatch.StartNew()
            action()
            timer.Stop()
            If _logWriter Is Nothing Then Return
            Try
                _logWriter.WriteOperation(New OperationLog With {.ExecutedAt = DateTime.UtcNow, .OperationType = operationType, .ElapsedMs = timer.ElapsedMilliseconds, .TotalCount = 1, .SuccessCount = 1, .FailureCount = 0})
            Catch
                ' 永続化済みのマスタ操作をログ失敗として取り消さない。
            End Try
        End Sub

        Private Shared Function NormalizeOptional(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return Nothing
            Return value.Trim()
        End Function
    End Class
End Namespace
