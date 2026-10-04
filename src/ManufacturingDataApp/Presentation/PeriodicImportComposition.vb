Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Infrastructure.Logging

Namespace Presentation
    Public NotInheritable Class PeriodicImportComposition
        Public ReadOnly Property DatabaseIdentity As String
        Public ReadOnly Property Presenter As PeriodicImportStatusPresenter
        Public ReadOnly Property Scheduler As PeriodicImportService
        Public ReadOnly Property Recovery As PeriodicImportRecoveryService
        Friend Sub New(databaseIdentity As String, presenter As PeriodicImportStatusPresenter, scheduler As PeriodicImportService, recovery As PeriodicImportRecoveryService)
            Me.DatabaseIdentity = databaseIdentity
            Me.Presenter = presenter
            Me.Scheduler = scheduler
            Me.Recovery = recovery
        End Sub
        Public Sub New(factory As DatabaseConnectionFactory, Optional log As IImportLogWriter = Nothing)
            DatabaseIdentity = factory.DatabasePath
            Presenter = New PeriodicImportStatusPresenter()
            Dim files As New PeriodicImportFileStore()
            Dim journal As New JsonImportProcessingJournal()
            Dim executor As New PeriodicCsvImportExecutor(factory)
            Dim cycle As New PeriodicImportCycle(files, journal, executor, sink:=Presenter)
            Scheduler = New PeriodicImportService(cycle, sink:=Presenter, log:=If(log, New DbLogWriter(factory)))
            Recovery = New PeriodicImportRecoveryService(files, journal, Function() Scheduler.Status.State = PeriodicImportState.Stopped)
        End Sub
    End Class
End Namespace
