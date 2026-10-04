Imports System.Collections.Concurrent
Imports System.Reflection
Imports System.Runtime.ExceptionServices
Imports System.Threading
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Forms
Imports ManufacturingDataApp.Infrastructure.Csv
Imports ManufacturingDataApp.Infrastructure.Logging
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Presentation

Namespace TestSupport
    Friend Class Phase5ConfigRepository
        Implements ICsvImportConfigRepository
        Private ReadOnly _inner As ICsvImportConfigRepository
        Public ReadOnly Calls As New List(Of String)()
        Public FailSave As Boolean
        Public Sub New(inner As ICsvImportConfigRepository)
            _inner = inner
        End Sub
        Public Function GetAll() As IReadOnlyList(Of CsvImportConfig) Implements ICsvImportConfigRepository.GetAll
            Return _inner.GetAll()
        End Function
        Public Function FindById(id As Integer) As CsvImportConfig Implements ICsvImportConfigRepository.FindById
            Calls.Add("Load:" & id)
            Return _inner.FindById(id)
        End Function
        Public Function FindByName(name As String) As CsvImportConfig Implements ICsvImportConfigRepository.FindByName
            Return _inner.FindByName(name)
        End Function
        Public Function GetMappings(id As Integer) As IReadOnlyList(Of CsvColumnMapping) Implements ICsvImportConfigRepository.GetMappings
            Calls.Add("Mappings:" & id)
            Return _inner.GetMappings(id)
        End Function
        Public Sub Save(config As CsvImportConfig, mappings As IReadOnlyList(Of CsvColumnMapping)) Implements ICsvImportConfigRepository.Save
            Throw New InvalidOperationException("Format save must not be used")
        End Sub
        Public Sub Delete(id As Integer) Implements ICsvImportConfigRepository.Delete
            _inner.Delete(id)
        End Sub
        Public Sub SaveMonitoringSettings(id As Integer, mode As ImportMode, path As String, interval As Integer) Implements ICsvImportConfigRepository.SaveMonitoringSettings
            Calls.Add("Save:" & id)
            If FailSave Then Throw New InvalidOperationException("private DB detail")
            _inner.SaveMonitoringSettings(id, mode, path, interval)
        End Sub
    End Class

    Friend Class Phase5LogWriter
        Implements IImportLogWriter
        Public ReadOnly Operations As New ConcurrentQueue(Of OperationLog)()
        Public Fail As Boolean
        Public Sub WriteOperation(log As OperationLog) Implements IImportLogWriter.WriteOperation
            If Fail Then Throw New InvalidOperationException("private log detail")
            Operations.Enqueue(log)
        End Sub
        Public Sub WriteErrors(errors As IEnumerable(Of ErrorLog)) Implements IImportLogWriter.WriteErrors
        End Sub
    End Class

    Friend Module Phase5Support
        Public Sub RunSta(action As Action)
            Dim errorValue As Exception = Nothing
            Dim thread As New Thread(Sub()
                                         Try
                                             action()
                                         Catch ex As Exception
                                             errorValue = ex
                                         End Try
                                     End Sub)
            thread.SetApartmentState(ApartmentState.STA)
            thread.Start()
            If Not thread.Join(TimeSpan.FromSeconds(30)) Then Throw New TimeoutException("STA test timed out")
            If errorValue IsNot Nothing Then ExceptionDispatchInfo.Capture(errorValue).Throw()
        End Sub
        Public Function Field(Of T)(instance As Object, name As String) As T
            Return DirectCast(instance.GetType().GetField(name, BindingFlags.NonPublic Or BindingFlags.Instance).GetValue(instance), T)
        End Function
        Public Sub Invoke(instance As Object, name As String, ParamArray args As Object())
            instance.GetType().GetMethod(name, BindingFlags.NonPublic Or BindingFlags.Instance).Invoke(instance, args)
        End Sub
        Public Function CreateMain(db As Phase2Database, composition As PeriodicImportComposition) As MainForm
            Dim data As New MeasurementDataRepository(db.Factory)
            Dim items As New MeasurementItemRepository(db.Factory)
            Dim log As New DbLogWriter(db.Factory)
            Dim adapter As New CsvHelperAdapter()
            Return New MainForm(New MeasurementDataService(data, items, New ValidationService(), log),
                New CsvImportService(adapter, New ValidationService(), items, data, log), New CsvExportService(adapter, log),
                New CsvConfigService(New CsvImportConfigRepository(db.Factory)),
                New MasterDataService(New EquipmentRepository(db.Factory), items, log, data),
                New LogViewService(New ErrorLogRepository(db.Factory), New OperationLogRepository(db.Factory)), composition)
        End Function
    End Module
End Namespace
