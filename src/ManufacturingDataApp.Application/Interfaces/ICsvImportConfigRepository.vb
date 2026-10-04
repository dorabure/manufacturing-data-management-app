Imports ManufacturingDataApp.Domain.Entities

Namespace Interfaces
    Public Interface ICsvImportConfigRepository
        Function GetAll() As IReadOnlyList(Of CsvImportConfig)
        Function FindById(configId As Integer) As CsvImportConfig
        Function FindByName(configName As String) As CsvImportConfig
        Function GetMappings(configId As Integer) As IReadOnlyList(Of CsvColumnMapping)
        Sub Save(config As CsvImportConfig, mappings As IReadOnlyList(Of CsvColumnMapping))
        Sub Delete(configId As Integer)
        Sub SaveMonitoringSettings(configId As Integer, mode As ManufacturingDataApp.Domain.Constants.ImportMode, folderPath As String, intervalSeconds As Integer)
    End Interface
End Namespace
