using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Mime;
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

var isRunning = true;
Process.GetProcessesByName("SteamCMDTool").Where(p => p.Id != Process.GetCurrentProcess().Id).ToList().ForEach(p => p.Kill(true));

try
{
    File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "steamcmd.exe"), Resources.steamcmd);
}
catch
{
    Notifications.Send("Error!", "steamcmd copy failed");
}

new Thread(() => {
    var trayIcon = new NotifyIcon()
    {
        Icon = Resources.Icon,
        ContextMenuStrip = new ContextMenuStrip()
        {
            Items =
            {
                new ToolStripMenuItem("quque", null, new EventHandler((_, _) => { new QueueForm().Show(); }), "quque"),
                new ToolStripMenuItem("close", null, new EventHandler((_, _) => { isRunning = false; }), "close"),
            }
        },
        Visible = true
    };
    Application.Run();
}) {IsBackground = true}.Start();



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

while (isRunning)
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
        ReceiveCommandBehaviour.Current.Value = command;
        try
        {
            DownloadFile(command);
        }
        catch
        {
            if (command.ActionID == (int) Actions.download)
                ReceiveCommandBehaviour.Commands.EnqueueFirst(command with { ActionID = (int) Actions.downloadAlternative});
            else
            Notifications.Send("Download failed", command.ItemName);
        }
        ReceiveCommandBehaviour.Current.Value = null;
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

    fineName += ".zip";

    if (pathString.EndsWith(".zip"))
    {
        File.Move(pathString, fineName);
    }
    else {
        ZipFile.CreateFromDirectory(pathString, fineName);
        Directory.Delete(pathString, true);
    }
    Notifications.Send("Download ended", command.ItemName);
 
}

internal static class SteamCmd
{
    private static readonly Dictionary<string, bool> Memory = new();
    private static string DownloadsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
    static SteamCmd()
    {
        Process.Start(new ProcessStartInfo("steamcmd", " +quit") { CreateNoWindow = true })?.WaitForExit();
        Directory.CreateDirectory(DownloadsFolder);
    }

    public static void Initialize()
    {
        
    }

    public static string Download(Command command)
    {
        if (command.ActionID == (int)Actions.downloadAlternative)
        {
            var request = GetDownloadRequest(command.AppId, command.ItemId);
            var file = File.Create(Path.Combine(DownloadsFolder, $"{command.AppId}_{command.ItemId}.zip"));
            request.GetResponseStream().CopyTo(file);
            file.Close();
            return file.Name;
        }

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

    public static bool IsModAvailable(string appId, string itemId)
    {
     return       GetDownloadRequest(appId, itemId) != null;
     
    }

    private static WebResponse? GetDownloadRequest(string appId, string itemId)
    {
        for (int idx = -1 ; idx < 17; idx ++)
        {
            var s = idx == -1 ? "" : idx.ToString();
            try { 
                return WebRequest.CreateHttp($"http://workshop{s}.abcvg.info/archive/{appId}/{itemId}.zip").GetResponse();
            }
            catch { }
        }
        return null;
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
    private static readonly INotificationManager Manager;

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

    public static void Send(string title, string @base)
    {
        var notification = new Notification
        {
            Title = title,
            Body = @base
        };
        Manager.ShowNotification(notification);
    }
}

internal class ReceiveCommandBehaviour : WebSocketBehavior
{
    public static readonly ReactQueue<Command> Commands = new ();
    public static ReactField<Command?> Current = new ReactField<Command?>();

    protected override void OnMessage(MessageEventArgs e)
    {
        var jsonObject = JsonSerializer.Deserialize<Command>(e.Data);

        if (jsonObject is null)
            return;

        switch (jsonObject.ActionID)
        {
            case (int)Actions.download or (int) Actions.downloadAlternative:
                AddCommand(jsonObject);
                break;
            case (int)Actions.check:
                CheckApp(jsonObject);
                break;
        }
    }

    private static void AddCommand(Command command)
    {
        Commands.Enqueue(command);
        Notifications.Send("Download queued", command.ItemName);
    }

    private void CheckApp(Command command)
    {
        Send(SteamCmd.IsAppAvailable(command.AppId) ? "yes" : SteamCmd.IsModAvailable(command.AppId, command.ItemId) ? "~" : "no");
    }
}

internal class ReactField<T>
{
    private T t;
    public event Action OnChange;
    public T Value { get => t; set { t = value; OnChange?.Invoke(); } }
}

internal class ReactQueue<T>: IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, ICollection
{
    private AutoResetEvent _ = new AutoResetEvent(true);

    public event Action OnChange;

    private LinkedList<T> queue = new LinkedList<T>();
    
    public int Count => ((IReadOnlyCollection<T>)queue).Count;

    public bool IsSynchronized => ((ICollection)queue).IsSynchronized;

    public object SyncRoot => ((ICollection)queue).SyncRoot;

    public void CopyTo(Array array, int index)
    {
        ((ICollection)queue).CopyTo(array, index);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)queue).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)queue).GetEnumerator();
    }

    public void Enqueue(T t)
    {
        _.WaitOne();
        queue.AddLast(t);
        OnChange?.Invoke();
        _.Set();
    }

    public void EnqueueFirst(T t)
    {
        _.WaitOne();
        queue.AddFirst(t);
        OnChange?.Invoke();
        _.Set();
    }

    public void RemoveAt(int idx)
    {
        _.WaitOne();
        queue.Remove(queue.ElementAt(idx));
        OnChange?.Invoke();
        _.Set();
    }

    public T Dequeue()
    {
        _.WaitOne();
        var t = queue.First.Value;
        queue.RemoveFirst();
        OnChange?.Invoke();
        _.Set();
        return t;
    }

    public void Clear()
    {
        _.WaitOne();
        queue.Clear();
        OnChange?.Invoke();
        _.Set();
    }
}

internal class QueueForm : Form
{
    ListBox listbox;
    Label label;
    Button button;
    public QueueForm()
    {
        Icon = Resources.Icon;
        listbox = new ListBox()
        { Dock = DockStyle.Fill};

        button = new Button()
        {
            Text = "Remove",
            Dock = DockStyle.Bottom,
            Height = 33,
            Visible = false
        };
        label = new Label()
        {
            Dock = DockStyle.Top,
            Height = 33,
            Visible = true
        };

        Controls.Add(listbox);
        Controls.Add(button);
        Controls.Add(label);
        ReceiveCommandBehaviour.Commands.OnChange += UpdateUI;
        ReceiveCommandBehaviour.Current.OnChange += UpdateUI;

        UpdateUI();
        
        listbox.SelectedIndexChanged += (_, _) =>
        {
            button.Visible = listbox.SelectedIndex >= 0;
        };
        button.Click += RemoveElement;
    }

    private void RemoveElement(object? _, EventArgs __)
    {
        if (listbox.SelectedIndex == -1)
            return;

        ReceiveCommandBehaviour.Commands.RemoveAt(listbox.SelectedIndex);
        button.Visible = false;
    }

    private void UpdateUI()
    {
        if (InvokeRequired)
        {
            Invoke(UpdateUI);
            return;
        }
        listbox.Items.Clear();
        int i = 0;

        if (ReceiveCommandBehaviour.Current.Value != null)
            label.Text = ($"current: [{(ReceiveCommandBehaviour.Current.Value.ActionID == (int)Actions.download ? "SteamCMD" : "download Cache")}] {ReceiveCommandBehaviour.Current.Value.ItemName}");
        else
            label.Text = "";
        ReceiveCommandBehaviour.Commands.Select(c => $"{++i} [{(c.ActionID==(int)Actions.download ? "SteamCMD" : "download Cache")}] {c.ItemName}").ToList().ForEach(_ =>listbox.Items.Add(_));

        if (listbox.SelectedIndex == -1)
        { 
            button.Visible = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        ReceiveCommandBehaviour.Commands.OnChange -= UpdateUI;
        ReceiveCommandBehaviour.Current.OnChange -= UpdateUI;
    }
}

internal enum Actions
{
    download = 0,
    check = 1,
    downloadAlternative = 2
}

internal record Command(string AppId, string ItemId, string ItemName, int ActionID);