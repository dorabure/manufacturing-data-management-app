Imports System.IO
Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Csv
Imports ManufacturingDataApp.Infrastructure.Logging
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class AtomicCsvImportTests
    Private Shared Function Row(index As Integer, Optional equipment As String = "EQ001", Optional value As String = "10") As String
        Return $"2026-09-21T09:{index:00}:00.0000000Z,{equipment},設備,温度,{value},℃"
    End Function

    Private Shared Function Mixed() As String()
        Return {Row(0), Row(1), Row(2), Row(3, value:="abc"), "2026-09-21T09:04:00.0000000Z,,設備,温度,10,"}
    End Function

    <Fact>
    Public Sub Atomic_AllValid_SavesOnce()
        Using f As New Fixture()
            Dim result = f.Service.ImportAndSaveAtomic(f.Request(Row(0), Row(1), Row(2)))
            Counts(result, 3, 3, 0, 0, 0)
            Assert.Equal(1, f.Data.AddRangeCalls)
            Assert.Equal(3L, f.Count("MeasurementData"))
            Assert.True(result.LogRecorded)
            Assert.Equal(3, f.Data.SearchCalls)
        End Using
    End Sub

    <Theory>
    <InlineData(False, ImportMode.SingleFile)>
    <InlineData(False, ImportMode.PeriodicFolder)>
    <InlineData(True, ImportMode.SingleFile)>
    <InlineData(True, ImportMode.PeriodicFolder)>
    Public Sub MixedRows_EntryNotModeDeterminesPolicy(atomic As Boolean, mode As ImportMode)
        Using f As New Fixture()
            Dim request = f.Request(Mixed())
            request.Config.ImportMode = mode
            Dim result = If(atomic, f.Service.ImportAndSaveAtomic(request), f.Service.ImportAndSave(request))
            Counts(result, 5, If(atomic, 0, 3), If(atomic, 5, 2), 2, If(atomic, 3, 0))
            Assert.Equal(If(atomic, 0, 1), f.Data.AddRangeCalls)
            Assert.Equal(If(atomic, 0L, 3L), f.Count("MeasurementData"))
            Assert.Equal(3, result.ValidationResult.Errors.Count)
            Assert.Equal(3L, f.Count("ErrorLog"))
            Assert.True(result.ValidationResult.Errors.Select(Function(e) e.RowNumber.Value).Distinct().SequenceEqual({5, 6}))
            Assert.Equal(If(atomic, 3, 0), f.Equipment.FindCalls)
        End Using
    End Sub

    <Fact>
    Public Sub Atomic_AllInvalid_NeverCallsAddRange()
        Using f As New Fixture()
            Dim result = f.Service.ImportAndSaveAtomic(f.Request(Row(0, value:="abc"), Row(1, value:="200")))
            Counts(result, 2, 0, 2, 2, 0)
            Assert.Equal(0, f.Data.AddRangeCalls)
            Assert.Equal(0L, f.Count("MeasurementData"))
        End Using
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub Atomic_DuplicateRejectsEntireFile(existingDb As Boolean)
        Using f As New Fixture()
            Dim request = f.Request(Row(0), Row(0), Row(1))
            Dim expectedRow = 3
            If existingDb Then
                request = f.Request(Row(0), Row(1))
                f.RealData.Add(f.Service.Import(request).ValidMeasurements(0))
                expectedRow = 2
            End If
            Dim result = f.Service.ImportAndSaveAtomic(request)
            Assert.Equal(0, f.Data.AddRangeCalls)
            Assert.Equal(If(existingDb, 1L, 0L), f.Count("MeasurementData"))
            Assert.Equal(0, result.SuccessCount)
            Assert.Equal(result.TotalCount, result.FailureCount)
            Dim err = Assert.Single(result.ValidationResult.Errors)
            Assert.Equal(ErrorTypes.Duplicate, err.ErrorType)
            Assert.Equal(StandardFields.AcquiredAt, err.FieldName)
            Assert.Equal(expectedRow, err.RowNumber)
            Assert.Equal("input.csv", err.FileName)
            Assert.Equal(1, result.ValidationErrorRowCount)
            Assert.Equal(If(existingDb, 1, 2), result.HeldValidRowCount)
        End Using
    End Sub

    <Fact>
    Public Sub Atomic_UnknownEquipment_IsRowError()
        Using f As New Fixture()
            Dim result = f.Service.ImportAndSaveAtomic(f.Request(Row(0), Row(1, "MISSING")))
            Counts(result, 2, 0, 2, 1, 1)
            Dim err = Assert.Single(result.ValidationResult.Errors)
            Assert.Equal(StandardFields.EquipmentId, err.FieldName)
            Assert.Equal(ErrorTypes.System, err.ErrorType)
            Assert.Contains("設備マスタに存在しない", err.Message)
            Assert.Equal(3, err.RowNumber)
            Assert.Equal("input.csv", err.FileName)
            Assert.Equal(0, f.Data.AddRangeCalls)
            Assert.Equal(0L, f.Count("MeasurementData"))
        End Using
    End Sub

    <Fact>
    Public Sub Manual_UnknownEquipment_StillThrowsForeignKeyWithoutPrecheck()
        Using f As New Fixture()
            f.Equipment.Failure = New InvalidOperationException("must not precheck")
            Dim ex = Assert.Throws(Of SqliteException)(Function() f.Service.ImportAndSave(f.Request(Row(0), Row(1, "MISSING"))))
            Assert.Equal(19, ex.SqliteErrorCode)
            Assert.Equal(0, f.Equipment.FindCalls)
            Assert.Equal(1, f.Data.AddRangeCalls)
            Assert.Equal(0L, f.Count("MeasurementData"))
        End Using
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub Atomic_EmptyAndHeaderOnly_AreSuccessful(header As Boolean)
        Using f As New Fixture()
            Dim request = f.Request()
            request.Config.HasHeader = header
            If Not header Then File.WriteAllText(request.FilePath, String.Empty)
            Dim result = f.Service.ImportAndSaveAtomic(request)
            Counts(result, 0, 0, 0, 0, 0)
            Assert.True(result.LogRecorded)
            Assert.Equal(0, f.Data.AddRangeCalls)
            Assert.Equal(0L, f.Count("MeasurementData"))
        End Using
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub Atomic_RealTransactionSecondInsertFailure_RollsBackFirst(uniqueRace As Boolean)
        Using f As New Fixture()
            Dim request = f.Request(Row(0), Row(1, "EQ002"))
            Dim second = f.Service.Import(request).ValidMeasurements(1)
            f.Data.BeforeAddRange = Sub()
                                       If uniqueRace Then
                                           f.RealData.Add(second)
                                       Else
                                           f.Database.Execute("DELETE FROM EquipmentMaster WHERE EquipmentId='EQ002';")
                                       End If
                                   End Sub
            Dim ex = Assert.Throws(Of SqliteException)(Function() f.Service.ImportAndSaveAtomic(request))
            Assert.Equal(19, ex.SqliteErrorCode)
            Assert.Equal(1, f.Data.AddRangeCalls)
            Assert.Equal(2, f.Equipment.FindCalls)
            Assert.Equal(If(uniqueRace, 1L, 0L), f.Count("MeasurementData"))
            Assert.Empty(f.RealData.Search("EQ001", Nothing, Nothing, Nothing))
            Assert.Equal(0, f.Log.ErrorsCalls)
            Assert.Equal(0, f.Log.OperationCalls)
        End Using
    End Sub

    <Theory>
    <InlineData(False, False)>
    <InlineData(True, False)>
    <InlineData(True, True)>
    Public Sub OperationLogFailure_PreservesDbResultAndDoesNotRepeatErrors(atomic As Boolean, validOnly As Boolean)
        Using f As New Fixture()
            f.Log.FailOperation = True
            Dim request = f.Request(If(validOnly, New String() {Row(0), Row(1), Row(2)}, Mixed()))
            Dim result = If(atomic, f.Service.ImportAndSaveAtomic(request), f.Service.ImportAndSave(request))
            Dim saved = If(atomic AndAlso Not validOnly, 0, 3)
            Assert.Equal(saved, result.SuccessCount)
            Assert.Equal(CLng(saved), f.Count("MeasurementData"))
            Assert.False(result.LogRecorded)
            Assert.Equal("ログの記録に失敗しました。", result.LogErrorMessage)
            Assert.Equal(1, f.Log.ErrorsCalls)
            Assert.Equal(1, f.Log.OperationCalls)
            Assert.Equal(If(validOnly, 0L, 3L), f.Count("ErrorLog"))
            Assert.Equal(0L, f.Count("OperationLog"))
        End Using
    End Sub

    <Theory>
    <InlineData("Search")>
    <InlineData("Equipment")>
    <InlineData("AddRange")>
    <InlineData("Parse")>
    Public Sub FatalExceptions_PropagateUnchanged(stage As String)
        Using f As New Fixture()
            Dim failure As New IOException("injected " & stage)
            Select Case stage
                Case "Search" : f.Data.SearchFailure = failure
                Case "Equipment" : f.Equipment.Failure = failure
                Case "AddRange" : f.Data.BeforeAddRange = Sub() Throw failure
                Case "Parse" : f.Adapter.Failure = failure
            End Select
            Assert.Same(failure, Assert.Throws(Of IOException)(Function() f.Service.ImportAndSaveAtomic(f.Request(Row(0)))))
            Assert.Equal(0L, f.Count("MeasurementData"))
            Assert.Equal(0, f.Log.ErrorsCalls)
            Assert.Equal(0, f.Log.OperationCalls)
            Assert.Equal(If(stage = "AddRange", 1, 0), f.Data.AddRangeCalls)
        End Using
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub LogDecorator_PreservesCountsAndDistinguishesType(periodic As Boolean)
        Using f As New Fixture(periodic)
            Dim result = If(periodic, f.Service.ImportAndSaveAtomic(f.Request(Mixed())), f.Service.ImportAndSave(f.Request(Mixed())))
            Assert.Equal(If(periodic, "周期CSV取込", "CSV取込"), CStr(f.Database.Scalar("SELECT OperationType FROM OperationLog;")))
            Assert.Equal(CLng(result.TotalCount), CLng(f.Database.Scalar("SELECT TotalCount FROM OperationLog;")))
            Assert.Equal(CLng(result.SuccessCount), CLng(f.Database.Scalar("SELECT SuccessCount FROM OperationLog;")))
            Assert.Equal(CLng(result.FailureCount), CLng(f.Database.Scalar("SELECT FailureCount FROM OperationLog;")))
            Assert.Equal(3L, f.Count("ErrorLog"))
        End Using
    End Sub

    <Theory>
    <InlineData(3)>
    <InlineData(5)>
    <InlineData(0)>
    <InlineData(1)>
    <InlineData(2)>
    <InlineData(4)>
    Public Sub Atomic_MissingDependency_FailsBeforeReadingOrSaving(missing As Integer)
        Using f As New Fixture()
            Dim service As CsvImportService
            Select Case missing
                Case 3 : service = New CsvImportService(f.Adapter, New ValidationService(), f.Items)
                Case 5 : service = New CsvImportService(f.Adapter, New ValidationService(), f.Items, f.Data, f.Log)
                Case Else
                    service = New CsvImportService(If(missing = 0, Nothing, f.Adapter), If(missing = 1, Nothing, New ValidationService()), If(missing = 2, Nothing, f.Items), f.Data, If(missing = 4, Nothing, f.Log), f.Equipment)
            End Select
            Assert.Throws(Of InvalidOperationException)(Function() service.ImportAndSaveAtomic(f.Request(Row(0))))
            Assert.Equal(0, f.Adapter.ReadCalls)
            Assert.Equal(0, f.Data.AddRangeCalls)
        End Using
    End Sub

    <Fact>
    Public Sub Atomic_UnknownMeasurementItem_RemainsAllowed()
        Using f As New Fixture()
            Dim result = f.Service.ImportAndSaveAtomic(f.Request(Row(0).Replace("温度", "未登録項目")))
            Counts(result, 1, 1, 0, 0, 0)
            Assert.Equal(1L, f.Count("MeasurementData"))
        End Using
    End Sub

    <Fact>
    Public Sub PeriodicDto_DefaultDoesNotClaimKnownDbOutcome()
        Dim result As New PeriodicFileResultDto()
        Assert.Equal(PeriodicFileOutcome.Unknown, result.Outcome)
        Assert.False(result.DbOutcomeKnown)
        Assert.False(result.LogRecorded)
        Assert.Equal(5, [Enum].GetValues(Of PeriodicFileOutcome)().Length)
    End Sub

    Private Shared Sub Counts(result As CsvImportExecutionResultDto, total As Integer, success As Integer, failure As Integer, errorRows As Integer, held As Integer)
        Assert.Equal(total, result.TotalCount)
        Assert.Equal(success, result.SuccessCount)
        Assert.Equal(failure, result.FailureCount)
        Assert.Equal(errorRows, result.ValidationErrorRowCount)
        Assert.Equal(held, result.HeldValidRowCount)
    End Sub

    Private Class Fixture
        Implements IDisposable
        ' このFixtureは直後に一時DBを削除するため、接続プールを共有しない。
        Public ReadOnly Database As New Phase2Database(pooling:=False)
        Public ReadOnly RealData As MeasurementDataRepository
        Public ReadOnly Data As DataSpy
        Public ReadOnly Equipment As EquipmentSpy
        Public ReadOnly Items As MeasurementItemRepository
        Public ReadOnly Adapter As New AdapterSpy()
        Public ReadOnly Log As LogSpy
        Public ReadOnly Service As CsvImportService
        Public Sub New(Optional periodic As Boolean = False)
            Database.Initialize()
            RealData = New MeasurementDataRepository(Database.Factory)
            Data = New DataSpy(RealData)
            Equipment = New EquipmentSpy(New EquipmentRepository(Database.Factory))
            Items = New MeasurementItemRepository(Database.Factory)
            Log = New LogSpy(New DbLogWriter(Database.Factory))
            Dim writer As IImportLogWriter = Log
            If periodic Then writer = New PeriodicImportLogWriter(writer)
            Service = New CsvImportService(Adapter, New ValidationService(), Items, Data, writer, Equipment)
        End Sub
        Public Function Request(ParamArray rows As String()) As CsvImportRequestDto
            Dim configs As New CsvImportConfigRepository(Database.Factory)
            Dim result As New CsvImportRequestDto With {.FilePath = Path.Combine(Database.DirectoryPath, "input.csv"), .Config = configs.GetAll()(0)}
            result.Config.Encoding = "utf-8"
            result.Config.HasHeader = True
            result.ColumnMappings.AddRange(configs.GetMappings(result.Config.ConfigId))
            File.WriteAllLines(result.FilePath, {"日時,設備ID,設備名,項目,値,単位"}.Concat(rows))
            Return result
        End Function
        Public Function Count(table As String) As Long
            Return CLng(Database.Scalar($"SELECT COUNT(*) FROM {table};"))
        End Function
        Public Sub Dispose() Implements IDisposable.Dispose
            Database.Dispose()
        End Sub
    End Class

    Private Class DataSpy
        Implements IMeasurementDataRepository
        Private ReadOnly _inner As IMeasurementDataRepository
        Public AddRangeCalls As Integer
        Public SearchCalls As Integer
        Public SearchFailure As Exception
        Public BeforeAddRange As Action
        Public Sub New(inner As IMeasurementDataRepository)
            _inner = inner
        End Sub
        Public Function Search(equipmentId As String, itemName As String, fromDate As DateTime?, toDate As DateTime?) As IReadOnlyList(Of MeasurementData) Implements IMeasurementDataRepository.Search
            SearchCalls += 1
            Assert.NotNull(equipmentId)
            Assert.NotNull(itemName)
            Assert.True(fromDate.HasValue)
            Assert.Equal(fromDate.Value.AddTicks(1), toDate.Value)
            If SearchFailure IsNot Nothing Then Throw SearchFailure
            Return _inner.Search(equipmentId, itemName, fromDate, toDate)
        End Function
        Public Sub AddRange(entities As IEnumerable(Of MeasurementData)) Implements IMeasurementDataRepository.AddRange
            AddRangeCalls += 1
            BeforeAddRange?.Invoke()
            _inner.AddRange(entities)
        End Sub
        Public Sub Add(entity As MeasurementData) Implements IMeasurementDataRepository.Add
            Throw New InvalidOperationException("Individual inserts prohibited")
        End Sub
        Public Sub Delete(ids As IEnumerable(Of Long)) Implements IMeasurementDataRepository.Delete
            Throw New InvalidOperationException("Compensation prohibited")
        End Sub
        Public Sub Update(entity As MeasurementData) Implements IMeasurementDataRepository.Update
            Throw New NotSupportedException()
        End Sub
        Public Function FindById(id As Long) As MeasurementData Implements IMeasurementDataRepository.FindById
            Throw New NotSupportedException()
        End Function
        Public Function IsItemNameInUse(itemName As String) As Boolean Implements IMeasurementDataRepository.IsItemNameInUse
            Throw New NotSupportedException()
        End Function
    End Class

    Private Class EquipmentSpy
        Implements IEquipmentRepository
        Private ReadOnly _inner As IEquipmentRepository
        Public Failure As Exception
        Public FindCalls As Integer
        Public Sub New(inner As IEquipmentRepository)
            _inner = inner
        End Sub
        Public Function FindById(id As String) As EquipmentMaster Implements IEquipmentRepository.FindById
            FindCalls += 1
            If Failure IsNot Nothing Then Throw Failure
            Return _inner.FindById(id)
        End Function
        Public Function GetAll() As IReadOnlyList(Of EquipmentMaster) Implements IEquipmentRepository.GetAll
            Throw New NotSupportedException()
        End Function
        Public Sub Add(entity As EquipmentMaster) Implements IEquipmentRepository.Add
            Throw New NotSupportedException()
        End Sub
        Public Sub Update(entity As EquipmentMaster) Implements IEquipmentRepository.Update
            Throw New NotSupportedException()
        End Sub
        Public Sub Delete(id As String) Implements IEquipmentRepository.Delete
            Throw New NotSupportedException()
        End Sub
    End Class

    Private Class AdapterSpy
        Implements ICsvFileAdapter
        Public Failure As Exception
        Public ReadCalls As Integer
        Public Function Read(path As String, encoding As String, delimiter As String, header As Boolean) As IReadOnlyList(Of IReadOnlyDictionary(Of String, String)) Implements ICsvFileAdapter.Read
            ReadCalls += 1
            If Failure IsNot Nothing Then Throw Failure
            Return New CsvHelperAdapter().Read(path, encoding, delimiter, header)
        End Function
        Public Sub Write(path As String, rows As IEnumerable(Of IReadOnlyDictionary(Of String, String)), encoding As String, delimiter As String, header As Boolean) Implements ICsvFileAdapter.Write
            Throw New NotSupportedException()
        End Sub
    End Class

    Private Class LogSpy
        Implements IImportLogWriter
        Private ReadOnly _inner As IImportLogWriter
        Public ErrorsCalls As Integer
        Public OperationCalls As Integer
        Public FailOperation As Boolean
        Public Sub New(inner As IImportLogWriter)
            _inner = inner
        End Sub
        Public Sub WriteErrors(errors As IEnumerable(Of ErrorLog)) Implements IImportLogWriter.WriteErrors
            ErrorsCalls += 1
            _inner.WriteErrors(errors)
        End Sub
        Public Sub WriteOperation(operation As OperationLog) Implements IImportLogWriter.WriteOperation
            OperationCalls += 1
            If FailOperation Then Throw New IOException("operation log failure")
            _inner.WriteOperation(operation)
        End Sub
    End Class
End Class
