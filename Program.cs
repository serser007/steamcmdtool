using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DesktopNotifications;
using DesktopNotifications.Apple;
using DesktopNotifications.FreeDesktop;
using DesktopNotifications.Windows;
using Microsoft.Win32;
using SteamCMD.Properties;
using WebSocketSharp;
using WebSocketSharp.Server;

File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "steamcmd.exe"), Resources.steamcmd);

var path = Path.Combine(AppContext.BaseDirectory, Assembly.GetExecutingAssembly().GetName().Name ?? "steamcmdtool");
var rk = Registry.CurrentUser.OpenSubKey
    ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

rk?.SetValue("SteamCMD", path.Replace(".dll", ".exe"));

var downloadFolder = SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0);
var downloadFileThread = new Thread(DownloadFileThread) {IsBackground = true};
[DllImport("shell32",
    CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
static extern string SHGetKnownFolderPath(
    [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags,
    nint hToken = 0);

downloadFileThread.Start();

var listener = new WebSocketServer(IPAddress.Parse("127.0.0.1"), 1024);
listener.AddWebSocketService<ReceiveCommandBehaviour>("/");
listener.Start();

while (true)
{
   Thread.Sleep(1);
}

void DownloadFileThread()
{
    SteamCmd.Initialize();
    while (true)
    {
        Thread.Sleep(1);
        if (!ReceiveCommandBehaviour.Commands.Any())
            continue;
        var command = ReceiveCommandBehaviour.Commands.Dequeue();
        try
        {
            DownloadFile(command);
        }
        catch
        {
            var notification = new Notification
            {
                Title = "Download failed",
                Body = command.ItemName
            };
            Notifications.Manager.ShowNotification(notification);
        }
    }
}

void DownloadFile(Command command)
{
    
    var pathString = SteamCmd.Download(command);

    var filename = command.ItemName;
    var invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());

    filename = invalid.Aggregate(filename, (current, c) => current.Replace(c.ToString(), ""));

    var name = Path.Combine(downloadFolder, command.ItemId + "_" + filename);

    var fineName = name;
    var i = 0;
    while (File.Exists(fineName + ".zip"))
    {
        fineName = name + $"({i++})";
    }
    ZipFile.CreateFromDirectory(pathString, fineName + ".zip");
    
    var notification = new Notification
    {
        Title = "Download ended",
        Body = command.ItemName
    };
    Notifications.Manager.ShowNotification(notification);
    Directory.Delete(pathString, true);
}

internal static class SteamCmd
{
    private static readonly Dictionary<string, bool> Memory = new();

    static SteamCmd()
    {
        Process.Start(new ProcessStartInfo("steamcmd", " +quit") { CreateNoWindow = true })?.WaitForExit();
    }

    public static void Initialize()
    {

    }

    public static string Download(Command command)
    {
        var output = RunCommand($"workshop_download_item {command.AppId} {command.ItemId}");
        if (!output.ToLower().Contains("success"))
        {
            throw new Exception();
        }
        var pathString = output.Split('"').SkipLast(1).Last();
        return pathString;
    }
    
    public static bool IsAppAvailable(string appId)
    {
        if (Memory.TryGetValue(appId, out var result))
            return result;

        var output = RunCommand($"licenses_for_app {appId}");
        result = Memory[appId] = output.Contains("packageID");
        return result;
    }

    private static string RunCommand(string command)
    {
        var process = Process.Start(new ProcessStartInfo("steamcmd", $"+login anonymous +{command} +quit") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
        if (process is null)
            throw new Exception();
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        return output;
    }
}

internal static class Notifications
{
    public static readonly INotificationManager Manager;

    private static INotificationManager CreateManager()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new FreeDesktopNotificationManager();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsNotificationManager();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new AppleNotificationManager();
        }

        throw new PlatformNotSupportedException();
    }
    static Notifications()
    {
        Manager = CreateManager();
        Manager.Initialize().GetAwaiter().GetResult();
    }
}

internal class ReceiveCommandBehaviour : WebSocketBehavior
{
    public static readonly Queue<Command> Commands = new ();
    protected override void OnMessage(MessageEventArgs e)
    {
        var jsonObject = JsonSerializer.Deserialize<Command>(e.Data);

        if (jsonObject is null)
            return;

        switch (jsonObject.ActionID)
        {
            case 0:
                AddCommand(jsonObject);
                break;
            case 1:
                CheckApp(jsonObject);
                break;
        }
    }

    private static void AddCommand(Command command)
    {
        Commands.Enqueue(command);
        var notification = new Notification
        {
            Title = "Download queued",
            Body = command.ItemName
        };
        Notifications.Manager.ShowNotification(notification);
    }

    private void CheckApp(Command command)
    {
        Send(SteamCmd.IsAppAvailable(command.AppId) ? "yes" : "no");
    }
}

internal record Command(string AppId, string ItemId, string ItemName, int ActionID);