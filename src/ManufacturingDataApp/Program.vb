Imports System.Text
Imports System.Windows.Forms
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Infrastructure.Csv
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Infrastructure.Logging

Module Program
    <STAThread>
    Public Sub Main()
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
        System.Windows.Forms.Application.EnableVisualStyles()
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)

        Dim connectionFactory = New DatabaseConnectionFactory()
        Dim databaseInitializer = New DatabaseInitializer(connectionFactory)
        Try
            databaseInitializer.Initialize()
        Catch ex As Exception
            MessageBox.Show($"データベースの初期化に失敗しました。{Environment.NewLine}{ex.Message}", "製造業向け 業務データ管理アプリ", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim measurementRepository = New MeasurementDataRepository(connectionFactory)
        Dim measurementItemRepository = New MeasurementItemRepository(connectionFactory)
        Dim csvConfigRepository = New CsvImportConfigRepository(connectionFactory)
        Dim csvConfigService = New CsvConfigService(csvConfigRepository)
        Dim logWriter = New DbLogWriter(connectionFactory)
        Dim measurementService = New MeasurementDataService(measurementRepository, measurementItemRepository, New ValidationService(), logWriter)
        Dim masterDataService = New MasterDataService(New EquipmentRepository(connectionFactory), measurementItemRepository, logWriter, measurementRepository)
        Dim csvAdapter = New CsvHelperAdapter()
        Dim csvImportService = New CsvImportService(csvAdapter, New ValidationService(), measurementItemRepository, measurementRepository, logWriter)
        Dim csvExportService = New CsvExportService(csvAdapter, logWriter)
        Dim logViewService = New LogViewService(New ErrorLogRepository(connectionFactory), New OperationLogRepository(connectionFactory))

        Dim periodic = New Presentation.PeriodicImportComposition(connectionFactory, logWriter)
        System.Windows.Forms.Application.Run(New Forms.MainForm(measurementService, csvImportService, csvExportService, csvConfigService, masterDataService, logViewService, periodic))
    End Sub
End Module
