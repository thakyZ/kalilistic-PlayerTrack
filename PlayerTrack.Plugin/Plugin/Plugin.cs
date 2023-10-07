using System;
using System.IO;
using System.Reflection;
using Dalamud.DrunkenToad.Core;
using Dalamud.DrunkenToad.Extensions;
using Dalamud.DrunkenToad.Gui.Windows;

using Dalamud.Plugin;
using FluentDapperLite.Runner;
using PlayerTrack.API;
using PlayerTrack.Domain;
using PlayerTrack.Infrastructure;
using PlayerTrack.Migration;

namespace PlayerTrack.Plugin;

using System.Threading.Tasks;
using Dalamud.DrunkenToad.Helpers;

public class Plugin : IDalamudPlugin
{
    private ErrorWindow? errorWindow;

    public Plugin(DalamudPluginInterface pluginInterface)
    {
        if (!DalamudContext.Initialize(pluginInterface))
        {
            return;
        }

        var isDatabaseLoadedSuccessfully = this.LoadDatabase();
        if (!isDatabaseLoadedSuccessfully)
        {
            return;
        }

        var configDir = DalamudContext.PluginInterface.GetPluginConfigDirectory();
        RepositoryContext.Initialize(configDir);
        ServiceContext.Initialize();
        this.RunPostStartup();
    }

    public string Name => "PlayerTrack";

    private PlayerTrackProvider? PlayerTrackProvider { get; set; }

    public void Dispose()
    {
        DalamudContext.PluginLog.Verbose("Entering Plugin.Dispose()");
        GC.SuppressFinalize(this);
        try
        {
            ServiceContext.LodestoneService.Stop();
            this.errorWindow?.Dispose();
            this.PlayerTrackProvider?.Dispose();
            LiteDBMigrator.Dispose();
            CommandHandler.Dispose();
            NameplateHandler.Dispose();
            EventDispatcher.Dispose();
            ContextMenuHandler.Dispose();
            GuiController.Dispose();
            ServiceContext.Dispose();
            RepositoryContext.Dispose();
            DalamudContext.Dispose();
        }
        catch (Exception ex)
        {
            DalamudContext.PluginLog.Error(ex, "Failed to dispose plugin.");
        }
    }

    private static void SetPluginVersion()
    {
        DalamudContext.PluginLog.Verbose("Entering Plugin.SetPluginVersion()");
        var pluginVersion = Assembly.GetExecutingAssembly().VersionNumber();
        var config = ServiceContext.ConfigService.GetConfig();
        config.PluginVersion = pluginVersion;
        ServiceContext.ConfigService.SaveConfig(config);
    }

    private void RunPostStartup() => Task.Run(() =>
    {
        DalamudContext.PluginLog.Verbose("Entering Plugin.RunPostStartup()");
        if (!LiteDBMigrator.Run())
        {
            DalamudContext.PluginLog.Error("Terminating plugin early since migration failed.");
            return;
        }

        ServiceContext.ConfigService.SyncIcons();
        ServiceContext.PlayerDataService.ReloadPlayerCache();
        SetPluginVersion();
        ServiceContext.BackupService.Startup();
        GuiController.Start();
        ContextMenuHandler.Start();
        EventDispatcher.Start();
        NameplateHandler.Start();
        CommandHandler.Start();
        DalamudContext.PlayerLocationManager.Start();
        DalamudContext.PlayerEventDispatcher.Start();
        this.PlayerTrackProvider = new PlayerTrackProvider(DalamudContext.PluginInterface, new PlayerTrackAPI());
        PlayerProcessService.CheckForDuplicates();
    });

    private bool LoadDatabase()
    {
        DalamudContext.PluginLog.Verbose("Entering Plugin.LoadDatabase()");
        var isAvailable = this.IsDBAvailable();
        if (!isAvailable)
        {
            return false;
        }

        var dataSource = Path.Combine(DalamudContext.PluginInterface.GetPluginConfigDirectory(), "data.db");
        var assemblyWithMigrations = Assembly.Load("PlayerTrack.Infrastructure");
        SQLiteFluentMigratorRunner.Run(dataSource, assemblyWithMigrations);
        return true;
    }

    private bool IsDBAvailable()
    {
        DalamudContext.PluginLog.Verbose("Entering Plugin.IsDBAvailable()");
        var isAvailable = FileHelper.VerifyFileAccess(Path.Combine(DalamudContext.PluginInterface.GetPluginConfigDirectory(), "data.db"));
        if (isAvailable)
        {
            return true;
        }

        DalamudContext.PluginLog.Error("Failed to load database since it's not available.");
        this.errorWindow = new ErrorWindow(
            DalamudContext.PluginInterface,
            "PlayerTrack failed to load since something else is using your database. Make sure it\'s not open anywhere else and try restarting your game and computer.");
        return false;
    }
}
