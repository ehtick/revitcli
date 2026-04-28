using System;
using Autodesk.Revit.UI;
using RevitCli.Addin.Bridge;
using RevitCli.Addin.Server;
using RevitCli.Addin.Services;
using RevitCli.Shared;

namespace RevitCli.Addin;

public sealed class RevitCliApp : IExternalApplication
{
    private ApiServer? _server;
    private RevitBridge? _bridge;
    private IRevitOperations? _operations;
    private const int DefaultPort = ServerInfo.DefaultPort;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            _bridge = new RevitBridge();
            _operations = new RealRevitOperations(_bridge);
            var revitVersion = application.ControlledApplication.VersionNumber ?? "";
            _server = new ApiServer(DefaultPort, _operations, revitVersion);
            _server.Start();
            return Result.Succeeded;
        }
        catch
        {
            try { _server?.Stop(); }
            catch { /* cleanup failure must not mask startup error */ }
            finally { _server = null; }

            try { _bridge?.Dispose(); }
            catch { /* cleanup failure must not mask startup error */ }
            finally { _bridge = null; }

            _operations = null;
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try { _server?.Stop(); }
        catch { /* Server cleanup failure must not block bridge disposal */ }
        finally { _server = null; }

        try { _bridge?.Dispose(); }
        catch { /* Bridge cleanup failure must not crash Revit on exit */ }
        finally { _bridge = null; }

        _operations = null;
        return Result.Succeeded;
    }
}
