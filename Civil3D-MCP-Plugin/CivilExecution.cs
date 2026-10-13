using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using App = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3DMcpPlugin;

public static class CivilExecution
{
  private static readonly SemaphoreSlim HostExecutionGate = new(1, 1);

  // Below the 120 s MCP command timeout so the caller gets the plugin's own error.
  private static readonly TimeSpan IdleStartTimeout = TimeSpan.FromSeconds(90);

  // Reads and writes run on the main thread from Application.Idle (application context),
  // not through ExecuteInCommandContextAsync: any operation that triggers a deferred Civil 3D
  // build (corridor-surface TIN on Commit or on read, assembly geometry, ...) leaves AutoCAD's
  // command context open forever, which either hung the plugin awaiting it or kept AutoCAD from
  // running the next command-context callback.
  public static async Task<T> ExecuteAsync<T>(Func<Document, CivilDocument, Database, Transaction, T> action, bool write)
  {
    return await ExecuteSerializedAsync(async () =>
    {
      // AsyncLocal request state does not flow into the Idle handler; capture it here.
      var expectedDrawingIdentity = PluginRuntime.GetExpectedDrawingIdentity();
      return await RunOnIdleAsync(() => RunInTransaction(action, write, expectedDrawingIdentity));
    });
  }

  // Sends a command line to AutoCAD and completes when that command ends. SendStringToExecute
  // only queues the command, so returning right after it reported success before it ran.
  public static async Task<bool> RunCommandAsync(string commandName, string commandLine, TimeSpan timeout)
  {
    return await ExecuteSerializedAsync(async () =>
    {
      var expectedDrawingIdentity = PluginRuntime.GetExpectedDrawingIdentity();
      var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      Document? doc = null;

      void OnEnded(object? sender, CommandEventArgs e)
      {
        if (string.Equals(e.GlobalCommandName, commandName, StringComparison.OrdinalIgnoreCase))
          ended.TrySetResult(true);
      }

      void OnCancelled(object? sender, CommandEventArgs e)
      {
        if (string.Equals(e.GlobalCommandName, commandName, StringComparison.OrdinalIgnoreCase))
          ended.TrySetResult(false);
      }

      await RunOnIdleAsync(() =>
      {
        doc = App.DocumentManager.MdiActiveDocument ?? throw new JsonRpcDispatchException("CIVIL3D.NO_DRAWING", "No active drawing is open in Civil 3D.");
        EnsureExpectedDrawing(doc, expectedDrawingIdentity);
        doc.CommandEnded += OnEnded;
        doc.CommandCancelled += OnCancelled;
        doc.CommandFailed += OnCancelled;
        doc.SendStringToExecute(commandLine, true, false, false);
        return true;
      });

      try
      {
        var finished = await Task.WhenAny(ended.Task, Task.Delay(timeout));
        if (finished != ended.Task)
          throw new JsonRpcDispatchException(
            "CIVIL3D.COMMAND_FAILED",
            $"Command {commandName} did not finish within {timeout.TotalSeconds:F0} s.");
        return await ended.Task;
      }
      finally
      {
        if (doc != null)
        {
          doc.CommandEnded -= OnEnded;
          doc.CommandCancelled -= OnCancelled;
          doc.CommandFailed -= OnCancelled;
        }
      }
    });
  }

  private static async Task<T> RunOnIdleAsync<T>(Func<T> work)
  {
    var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    var started = 0;

    void OnIdle(object? sender, EventArgs e)
    {
      App.Idle -= OnIdle;
      if (Interlocked.Exchange(ref started, 1) != 0) return;
      try
      {
        completion.TrySetResult(work());
      }
      catch (Exception ex)
      {
        completion.TrySetException(ex);
      }
    }

    App.Idle += OnIdle;
    var finished = await Task.WhenAny(completion.Task, Task.Delay(IdleStartTimeout));
    if (finished != completion.Task)
    {
      App.Idle -= OnIdle;
      if (Interlocked.Exchange(ref started, 1) == 0)
        throw new JsonRpcDispatchException(
          "CIVIL3D.COMMAND_FAILED",
          $"Civil 3D did not become idle within {IdleStartTimeout.TotalSeconds:F0} s; the operation was not run.");
      // The work already started on the main thread; wait for its real outcome.
    }
    return await completion.Task;
  }

  private static void EnsureExpectedDrawing(Document doc, string? expectedDrawingIdentity)
  {
    var activeDrawingIdentity = PluginRuntime.GetDrawingIdentity(doc);
    if (!string.IsNullOrWhiteSpace(expectedDrawingIdentity) &&
        !string.Equals(expectedDrawingIdentity, activeDrawingIdentity, StringComparison.OrdinalIgnoreCase))
    {
      throw new JsonRpcDispatchException(
        "CIVIL3D.CONFLICT",
        $"The active drawing changed from '{expectedDrawingIdentity}' to '{activeDrawingIdentity}' while the operation was queued. No drawing changes were made.");
    }
  }

  private static T RunInTransaction<T>(
    Func<Document, CivilDocument, Database, Transaction, T> action,
    bool write,
    string? expectedDrawingIdentity)
  {
    var doc = App.DocumentManager.MdiActiveDocument ?? throw new JsonRpcDispatchException("CIVIL3D.NO_DRAWING", "No active drawing is open in Civil 3D.");
    EnsureExpectedDrawing(doc, expectedDrawingIdentity);
    var civilDoc = CivilApplication.ActiveDocument ?? throw new JsonRpcDispatchException("CIVIL3D.NO_DRAWING", "No active Civil 3D document is available.");
    var database = doc.Database;

    using var documentLock = doc.LockDocument();
    using var transaction = database.TransactionManager.StartTransaction();

    var result = action(doc, civilDoc, database, transaction);

    if (write)
    {
      transaction.Commit();
    }

    return result;
  }

  public static async Task<T> ExecuteInCommandContextAsync<T>(Func<Task<T>> action)
  {
    return await ExecuteSerializedAsync(async () =>
    {
      T? result = default;
      Exception? capturedException = null;

      await App.DocumentManager.ExecuteInCommandContextAsync(async _ =>
      {
        try
        {
          result = await action();
        }
        catch (Exception ex)
        {
          capturedException = ex;
        }
      }, null);

      if (capturedException != null)
      {
        throw capturedException;
      }

      return result!;
    });
  }

  public static Task<T> ReadAsync<T>(Func<Document, CivilDocument, Database, Transaction, T> action)
  {
    return ExecuteAsync(action, false);
  }

  public static Task<T> WriteAsync<T>(Func<Document, CivilDocument, Database, Transaction, T> action)
  {
    return ExecuteAsync(action, true);
  }

  private static async Task<T> ExecuteSerializedAsync<T>(Func<Task<T>> action)
  {
    var cancellationToken = PluginRuntime.GetCurrentRequestCancellationToken();
    PluginRuntime.QueueHostOperation();
    var started = false;

    try
    {
      await HostExecutionGate.WaitAsync(cancellationToken);
      started = true;
      PluginRuntime.StartHostOperation();
      cancellationToken.ThrowIfCancellationRequested();
      return await action();
    }
    finally
    {
      if (started)
      {
        PluginRuntime.CompleteHostOperation();
        HostExecutionGate.Release();
      }
      else
      {
        PluginRuntime.CancelQueuedHostOperation();
      }
    }
  }
}
