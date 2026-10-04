Imports System.Collections.Generic

Namespace TestSupport
    Friend NotInheritable Class LegacyDatabaseSchema
        Private Sub New()
        End Sub

        Public Shared ReadOnly Property CreateStatements As IReadOnlyList(Of String) = {
            "CREATE TABLE IF NOT EXISTS EquipmentMaster (EquipmentId TEXT NOT NULL PRIMARY KEY, EquipmentName TEXT NOT NULL, FactoryName TEXT NULL, LineName TEXT NULL, CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);",
            "CREATE TABLE IF NOT EXISTS MeasurementItemMaster (ItemId INTEGER PRIMARY KEY AUTOINCREMENT, ItemName TEXT NOT NULL UNIQUE, Unit TEXT NOT NULL, MinValue REAL NULL, MaxValue REAL NULL, CHECK (MinValue IS NULL OR MaxValue IS NULL OR MinValue <= MaxValue));",
            "CREATE TABLE IF NOT EXISTS MeasurementData (Id INTEGER PRIMARY KEY AUTOINCREMENT, MeasuredAt TEXT NOT NULL, EquipmentId TEXT NOT NULL, EquipmentName TEXT NOT NULL, ItemName TEXT NOT NULL, Value REAL NOT NULL, Unit TEXT NOT NULL, CONSTRAINT FK_MeasurementData_Equipment FOREIGN KEY (EquipmentId) REFERENCES EquipmentMaster(EquipmentId), CONSTRAINT UQ_MeasurementData_Duplicate UNIQUE (EquipmentId, ItemName, MeasuredAt));",
            "CREATE TABLE IF NOT EXISTS CsvImportConfig (ConfigId INTEGER PRIMARY KEY AUTOINCREMENT, ConfigName TEXT NOT NULL UNIQUE, Encoding TEXT NOT NULL DEFAULT 'UTF-8', Delimiter TEXT NOT NULL DEFAULT ',', HasHeader INTEGER NOT NULL DEFAULT 1, CHECK (HasHeader IN (0, 1)));",
            "CREATE TABLE IF NOT EXISTS CsvColumnMapping (MappingId INTEGER PRIMARY KEY AUTOINCREMENT, ConfigId INTEGER NOT NULL, FieldName TEXT NOT NULL, CsvColumnIndex INTEGER NOT NULL, CONSTRAINT FK_CsvColumnMapping_Config FOREIGN KEY (ConfigId) REFERENCES CsvImportConfig(ConfigId) ON DELETE CASCADE, CONSTRAINT UQ_CsvColumnMapping_Field UNIQUE (ConfigId, FieldName), CONSTRAINT UQ_CsvColumnMapping_Index UNIQUE (ConfigId, CsvColumnIndex), CHECK (CsvColumnIndex >= 1));",
            "CREATE TABLE IF NOT EXISTS OperationLog (LogId INTEGER PRIMARY KEY AUTOINCREMENT, ExecutedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, OperationType TEXT NOT NULL, ElapsedMs INTEGER NOT NULL DEFAULT 0, TotalCount INTEGER NOT NULL DEFAULT 0, SuccessCount INTEGER NOT NULL DEFAULT 0, FailureCount INTEGER NOT NULL DEFAULT 0, CHECK (ElapsedMs >= 0), CHECK (TotalCount >= 0), CHECK (SuccessCount >= 0), CHECK (FailureCount >= 0));",
            "CREATE TABLE IF NOT EXISTS ErrorLog (ErrorId INTEGER PRIMARY KEY AUTOINCREMENT, OccurredAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, ConfigId INTEGER NULL, FileName TEXT NULL, RowNumber INTEGER NULL, FieldName TEXT NULL, ErrorType TEXT NOT NULL, ErrorMessage TEXT NOT NULL, CONSTRAINT FK_ErrorLog_Config FOREIGN KEY (ConfigId) REFERENCES CsvImportConfig(ConfigId));",
            "CREATE INDEX IF NOT EXISTS IX_MeasurementData_Equipment_MeasuredAt ON MeasurementData (EquipmentId, MeasuredAt);",
            "CREATE INDEX IF NOT EXISTS IX_MeasurementData_Item_MeasuredAt ON MeasurementData (ItemName, MeasuredAt);",
            "CREATE INDEX IF NOT EXISTS IX_ErrorLog_Config_OccurredAt ON ErrorLog (ConfigId, OccurredAt);",
            "CREATE INDEX IF NOT EXISTS IX_OperationLog_ExecutedAt ON OperationLog (ExecutedAt);"
        }

        Public Const SeedDataStatement As String = "INSERT OR IGNORE INTO EquipmentMaster (EquipmentId, EquipmentName, FactoryName, LineName) VALUES ('EQ001','搬送装置01','サンプル工場','ライン1'),('EQ002','加工装置01','サンプル工場','ライン2'),('EQ003','組立装置01','サンプル工場','ライン3'); INSERT OR IGNORE INTO MeasurementItemMaster (ItemName, Unit, MinValue, MaxValue) VALUES ('温度','℃',0,100),('圧力','MPa',0,5),('振動','mm/s',0,10); INSERT OR IGNORE INTO CsvImportConfig (ConfigName, Encoding, Delimiter, HasHeader) VALUES ('SCADA_A','UTF-8',',',1); INSERT OR IGNORE INTO CsvColumnMapping (ConfigId, FieldName, CsvColumnIndex) SELECT ConfigId, '取得日時', 1 FROM CsvImportConfig WHERE ConfigName = 'SCADA_A'; INSERT OR IGNORE INTO CsvColumnMapping (ConfigId, FieldName, CsvColumnIndex) SELECT ConfigId, '設備ID', 2 FROM CsvImportConfig WHERE ConfigName = 'SCADA_A'; INSERT OR IGNORE INTO CsvColumnMapping (ConfigId, FieldName, CsvColumnIndex) SELECT ConfigId, '設備名', 3 FROM CsvImportConfig WHERE ConfigName = 'SCADA_A'; INSERT OR IGNORE INTO CsvColumnMapping (ConfigId, FieldName, CsvColumnIndex) SELECT ConfigId, '項目名', 4 FROM CsvImportConfig WHERE ConfigName = 'SCADA_A'; INSERT OR IGNORE INTO CsvColumnMapping (ConfigId, FieldName, CsvColumnIndex) SELECT ConfigId, '測定値', 5 FROM CsvImportConfig WHERE ConfigName = 'SCADA_A'; INSERT OR IGNORE INTO CsvColumnMapping (ConfigId, FieldName, CsvColumnIndex) SELECT ConfigId, '単位', 6 FROM CsvImportConfig WHERE ConfigName = 'SCADA_A';"
    End Class
End Namespace
