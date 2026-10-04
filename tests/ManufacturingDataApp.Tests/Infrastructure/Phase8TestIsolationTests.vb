Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase8TestIsolationTests
    <Fact>
    Public Sub TemporaryDatabasesCanBeDisposedWithoutAConnectionPoolHandle()
        For index = 1 To 8
            Using database As New Phase2Database(pooling:=False)
                database.Initialize()
                Assert.True(database.Scalar("SELECT COUNT(*) FROM EquipmentMaster;") IsNot Nothing)
            End Using
        Next
    End Sub
End Class
