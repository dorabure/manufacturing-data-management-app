Namespace Data
    Friend NotInheritable Class DatabaseMigrations
        Public Const CurrentVersion As Integer = 110
        Public Const LegacyConfigTable As String = "CREATE TABLE IF NOT EXISTS CsvImportConfig (ConfigId INTEGER PRIMARY KEY AUTOINCREMENT, ConfigName TEXT NOT NULL UNIQUE, Encoding TEXT NOT NULL DEFAULT 'UTF-8', Delimiter TEXT NOT NULL DEFAULT ',', HasHeader INTEGER NOT NULL DEFAULT 1, CHECK (HasHeader IN (0, 1)));"
        Public Shared ReadOnly Statements As String() = {
            "ALTER TABLE CsvImportConfig ADD COLUMN ImportMode TEXT NOT NULL DEFAULT 'SingleFile' CHECK (ImportMode IN ('SingleFile','PeriodicFolder'));",
            "ALTER TABLE CsvImportConfig ADD COLUMN WatchFolderPath TEXT NULL DEFAULT NULL;",
            "ALTER TABLE CsvImportConfig ADD COLUMN WatchIntervalSeconds INTEGER NOT NULL DEFAULT 60 CHECK (typeof(WatchIntervalSeconds)='integer' AND WatchIntervalSeconds BETWEEN 1 AND 604800);",
            "PRAGMA user_version=110;"
        }
    End Class
End Namespace
