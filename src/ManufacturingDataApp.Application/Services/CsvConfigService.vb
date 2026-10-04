Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Domain.Constants
Imports System.IO

Namespace Services
    Public Class CsvConfigService
        Private ReadOnly _repository As ICsvImportConfigRepository

        Public Sub New(repository As ICsvImportConfigRepository)
            _repository = repository
        End Sub

        Public Function GetAll() As IReadOnlyList(Of CsvImportConfig)
            Return _repository.GetAll()
        End Function

        Public Function GetById(configId As Integer) As CsvImportConfig
            Return _repository.FindById(configId)
        End Function

        Public Function GetMappings(configId As Integer) As IReadOnlyList(Of CsvColumnMapping)
            Return _repository.GetMappings(configId)
        End Function

        Public Sub Save(config As CsvImportConfig, mappings As IReadOnlyList(Of CsvColumnMapping))
            Validate(config, mappings)
            Dim sameName = _repository.FindByName(config.ConfigName.Trim())
            If sameName IsNot Nothing AndAlso sameName.ConfigId <> config.ConfigId Then Throw New ArgumentException("同じ設定名が既に存在します。")
            config.ConfigName = config.ConfigName.Trim()
            _repository.Save(config, mappings)
        End Sub

        Public Sub Delete(configId As Integer)
            _repository.Delete(configId)
        End Sub

        Public Sub SaveMonitoringSettings(configId As Integer, mode As ImportMode, folderPath As String, intervalSeconds As Integer)
            If configId <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(configId))
            If Not [Enum].IsDefined(mode) Then Throw New ArgumentOutOfRangeException(NameOf(mode))
            If intervalSeconds < 1 OrElse intervalSeconds > 604800 Then Throw New ArgumentOutOfRangeException(NameOf(intervalSeconds))
            Dim normalized = If(String.IsNullOrWhiteSpace(folderPath), Nothing, Path.GetFullPath(folderPath.Trim()))
            _repository.SaveMonitoringSettings(configId, mode, normalized, intervalSeconds)
        End Sub

        Private Shared Sub Validate(config As CsvImportConfig, mappings As IReadOnlyList(Of CsvColumnMapping))
            If config Is Nothing OrElse String.IsNullOrWhiteSpace(config.ConfigName) Then Throw New ArgumentException("設定名を入力してください。")
            If String.IsNullOrWhiteSpace(config.Encoding) OrElse String.IsNullOrWhiteSpace(config.Delimiter) Then Throw New ArgumentException("文字コードと区切り文字を指定してください。")
            Dim fields = {StandardFields.AcquiredAt, StandardFields.EquipmentId, StandardFields.EquipmentName, StandardFields.ItemName, StandardFields.Value, StandardFields.Unit}
            If mappings Is Nothing OrElse mappings.Count <> fields.Length OrElse fields.Any(Function(fieldName) Not mappings.Any(Function(mapping) mapping.FieldName = fieldName)) OrElse mappings.Select(Function(mapping) mapping.FieldName).Distinct().Count() <> fields.Length OrElse mappings.Any(Function(mapping) mapping.CsvColumnIndex < 1) OrElse mappings.Select(Function(mapping) mapping.CsvColumnIndex).Distinct().Count() <> mappings.Count Then
                Throw New ArgumentException("6項目すべてに重複しない1以上のCSV列番号を設定してください。")
            End If
        End Sub
    End Class
End Namespace
