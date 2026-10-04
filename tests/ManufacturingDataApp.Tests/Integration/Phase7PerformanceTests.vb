Imports System.Diagnostics
Imports System.IO
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Presentation
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase7PerformanceTests
    Private ReadOnly _output As ITestOutputHelper
    Public Sub New(output As ITestOutputHelper)
        _output = output
    End Sub

    <Fact, Trait("Category", "Performance")>
    Public Sub TenThousandRows_UiRespondsAndCloseWaitsForAtomic()
        RunUiAsync(Async Function()
                       Using f As New Phase4Fixture()
                           Write7(f.Root, "A.csv", 0, 10000)
                           Write7(f.Root, "B.csv", 10001)
                           Dim probe As New Phase7Probe(f.Database)
                           Dim entered = Signal6()
                           Dim release = Signal6()
                           probe.BeforeDb = Sub()
                                                entered.TrySetResult(True)
                                                release.Task.GetAwaiter().GetResult()
                                            End Sub
                           Dim presenter As New PeriodicImportStatusPresenter()
                           Dim service As New PeriodicImportService(New PeriodicImportCycle(f.Files, f.Journal, probe, sink:=presenter), sink:=presenter)
                           Using form = CreateMain(f.Database, Phase6MainFormTests.Compose(f.Database, service, presenter))
                               Dim handle = form.Handle
                               Dim closed = Signal6()
                               AddHandler form.FormClosed, Sub() closed.TrySetResult(True)
                               Dim memoryBefore = GC.GetTotalMemory(False)
                               Dim total = Stopwatch.StartNew()
                               Await service.StartAsync(f.Options)
                               Try
                                   Await entered.Task
                                   Dim pulse = Signal6()
                                   Dim uiDelay = Stopwatch.StartNew()
                                   form.BeginInvoke(New Action(Sub() pulse.TrySetResult(True)))
                                   Await pulse.Task
                                   uiDelay.Stop()
                                   form.Close()
                                   Assert.False(closed.Task.IsCompleted)
                                   release.SetResult(True)
                                   Await closed.Task
                                   total.Stop()
                                   Assert.Equal(10000L, f.DataCount())
                                   Assert.Equal(New String() {"A.csv"}, probe.Calls)
                                   Assert.Equal(1, probe.MaximumActive)
                                   Assert.True(File.Exists(Path.Combine(f.Root, "B.csv")))
                                   Assert.Equal(ProcessingStage.Completed, Assert.Single(f.State().Attempts).Stage)
                                   _output.WriteLine($"PERF 10000 rows: read_ms={probe.ReadMs:F2}; validation_mapping_lookup_ms={probe.ValidationMs:F2}; db_ms={probe.DbMs:F2}; atomic_ms={probe.AtomicMs:F2}; total_ms={total.Elapsed.TotalMilliseconds:F2}; ui_post_ms={uiDelay.Elapsed.TotalMilliseconds:F2}; managed_before={memoryBefore}; managed_after={GC.GetTotalMemory(False)}")
                               Finally
                                   release.TrySetResult(True)
                               End Try
                           End Using
                       End Using
                   End Function)
    End Sub

    <Fact, Trait("Category", "Performance")>
    Public Async Function CsvBatch_OrderedSingleWorkerDurableAndNoDuplicates() As Task
        ' Normal regression: 100. Explicit benchmark: MDA_PERF_CSV_COUNT=1000.
        ' The 1000-file run was measured separately; do not silently disguise its cost.
        Dim count = If(Environment.GetEnvironmentVariable("MDA_PERF_CSV_COUNT") = "1000", 1000, 100)
        Using f As New Phase4Fixture()
            For i = 0 To count - 1
                Write7(f.Root, $"{i:D4}.csv", i)
            Next
            Dim probe As New Phase7Probe(f.Database)
            Dim presenter As New PeriodicImportStatusPresenter()
            Dim service As New PeriodicImportService(New PeriodicImportCycle(f.Files, f.Journal, probe, sink:=presenter), f.Clock, presenter)
            Dim before = GC.GetTotalMemory(False)
            Dim total = Stopwatch.StartNew()
            Await service.StartAsync(f.Options)
            Await service.CurrentCycleCompletion
            Await service.StopAsync()
            total.Stop()
            Assert.Null(service.Status.LastError)
            Assert.Equal(CLng(count), f.DataCount())
            Assert.Equal(count, Directory.GetFiles(Path.Combine(f.Root, "success"), "*.csv").Length)
            Assert.Equal(Enumerable.Range(0, count).Select(Function(i) $"{i:D4}.csv"), probe.Calls)
            Assert.Equal(1, probe.MaximumActive)
            Dim state = f.State()
            PeriodicJournalValidator.Validate(state, f.Root, f.Options.DatabaseIdentity)
            Assert.Equal(count, state.Attempts.Count)
            Assert.All(state.Attempts, Sub(a) Assert.Equal(ProcessingStage.Completed, a.Stage))
            Assert.Equal(count, Assert.Single(state.Orders).Cursor)
            Assert.Equal(CLng(count), CLng(f.Database.Scalar("SELECT COUNT(*) FROM OperationLog WHERE OperationType='周期CSV取込'")))
            Assert.InRange(presenter.PendingCount, 0, 100)
            Assert.InRange(presenter.Drain().Count, 0, 100)
            Await service.StartAsync(f.Options)
            Await service.CurrentCycleCompletion
            Await service.StopAsync()
            Assert.Equal(count, probe.Calls.Count)
            Assert.Equal(CLng(count), f.DataCount())
            _output.WriteLine($"PERF {count} CSV x 1 row: total_ms={total.Elapsed.TotalMilliseconds:F2}; read_ms={probe.ReadMs:F2}; validation_mapping_lookup_ms={probe.ValidationMs:F2}; db_ms={probe.DbMs:F2}; managed_before={before}; managed_after={GC.GetTotalMemory(False)}; peak_working_set={Process.GetCurrentProcess().PeakWorkingSet64}; attempts={state.Attempts.Count}; max_concurrency={probe.MaximumActive}")
        End Using
    End Function

    <Fact>
    Public Async Function LongElapsedAndTenThousandSkipsKeepNotificationsAndLogsBounded() As Task
        Dim clock As New ManualTimeProvider()
        Dim release = Signal6()
        Dim cycle As New Phase6Cycle With {.Execution = Function(token) release.Task}
        Dim presenter As New PeriodicImportStatusPresenter()
        Dim log As New Phase5LogWriter()
        Dim service As New PeriodicImportService(cycle, clock, presenter, log)
        Await service.StartAsync(New PeriodicImportOptionsDto With {.WatchIntervalSeconds = 1})
        Await cycle.Entered.Task
        For i = 1 To 10000
            clock.Advance(TimeSpan.FromSeconds(1))
            Await clock.WaitForTimerAsync(i + 1)
            Assert.InRange(presenter.PendingCount, 0, 100)
        Next
        Assert.Equal(10000L, service.Status.SkippedCycles)
        Assert.Single(log.Operations)
        Assert.DoesNotContain("スキップ", presenter.CurrentMessage)
        Dim history = presenter.Drain()
        Assert.Equal(10000L, Assert.Single(history.Where(Function(n) n.Kind = PeriodicImportNotificationKind.CycleSkipped)).Repetitions)
        clock.Advance(TimeSpan.FromDays(3650))
        Await clock.WaitForTimerAsync(10002)
        Assert.Equal(10001L, service.Status.SkippedCycles)
        Dim stopping = service.StopAsync()
        release.SetResult(True)
        Await stopping
        Assert.Equal(2, log.Operations.Count)
        Assert.Equal(1, cycle.ExecuteCount)
        Assert.InRange(presenter.Drain().Count, 0, 100)
        Assert.True(service.Status.CleanupCompleted)
    End Function
End Class
