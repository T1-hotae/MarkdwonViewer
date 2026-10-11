// MdViewer — 마크다운 파일을 자체 창에서 보고 편집하는 프로그램.
// 화면 표시는 Windows에 내장된 WebView2를 쓰고, 문서는 https://md.local/<드라이브>/<경로> 주소로
// 프로그램 안에서 직접 제공한다(임시 HTML 파일을 만들지 않음).
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("MdViewer")]
[assembly: AssemblyProduct("MdViewer")]
[assembly: AssemblyDescription("Markdown Viewer")]
[assembly: AssemblyVersion("1.0.0.0")]

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string file = args.Length > 0 ? args[0] : PickFile(null);
        if (file == null) return;
        Application.Run(new ViewerForm(Path.GetFullPath(file)));
    }

    public static string PickFile(IWin32Window owner)
    {
        using (var dlg = new OpenFileDialog())
        {
            dlg.Title = "마크다운 파일 열기";
            dlg.Filter = "마크다운 (*.md;*.markdown)|*.md;*.markdown;*.mdown;*.mkd|모든 파일 (*.*)|*.*";
            return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FileName : null;
        }
    }
}

class ViewerForm : Form
{
    const string Host = "md.local";
    const string Origin = "https://" + Host;

    readonly WebView2 web = new WebView2();
    readonly string startFile;
    string currentFile;
    FileSystemWatcher watcher;
    readonly Timer reloadTimer = new Timer();
    bool dirty;
    bool closeAfterSave;
    DateTime ignoreChangesUntil = DateTime.MinValue;

    public ViewerForm(string file)
    {
        startFile = file;
        Text = Path.GetFileName(file) + " - MdViewer";
        ClientSize = new Size(1150, 860);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        web.Dock = DockStyle.Fill;
        web.DefaultBackgroundColor = Color.Transparent;
        Controls.Add(web);

        reloadTimer.Interval = 250;
        reloadTimer.Tick += delegate
        {
            reloadTimer.Stop();
            if (DateTime.Now < ignoreChangesUntil) return; // 이 창에서 저장한 변경
            if (web.CoreWebView2 != null) web.CoreWebView2.ExecuteScriptAsync("window.reloadDoc && window.reloadDoc()");
        };

        Load += async delegate
        {
            try
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdViewer");
                var env = await CoreWebView2Environment.CreateAsync(null, data);
                await web.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                MessageBox.Show("WebView2 런타임을 시작할 수 없습니다.\n\n" + ex.Message, "MdViewer");
                Close();
                return;
            }
            Setup(web.CoreWebView2);
            web.CoreWebView2.Navigate(ToUrl(startFile));
        };
    }

    void Setup(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;

        core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnRequest;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            OpenLink(e.Uri);
        };
        core.SourceChanged += delegate { OnDocumentChanged(core.Source); };
        core.DocumentTitleChanged += delegate { UpdateTitle(); };
        core.WebMessageReceived += OnMessage;
    }

    // ---- 주소 <-> 파일 경로 ----

    static string ToUrl(string path)
    {
        string full = Path.GetFullPath(path);
        string drive = full.Substring(0, 1).ToLowerInvariant();
        string[] parts = full.Substring(3).Split('\\');
        var sb = new StringBuilder(Origin + "/" + drive);
        foreach (string p in parts) sb.Append('/').Append(Uri.EscapeDataString(p));
        return sb.ToString();
    }

    static string ToPath(Uri uri)
    {
        string[] segs = uri.AbsolutePath.TrimStart('/').Split('/');
        if (segs.Length < 2 || segs[0].Length != 1) return null;
        var sb = new StringBuilder(segs[0].ToUpperInvariant() + ":");
        for (int i = 1; i < segs.Length; i++) sb.Append('\\').Append(Uri.UnescapeDataString(segs[i]));
        return sb.ToString();
    }

    static bool IsMarkdown(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".md" || ext == ".markdown" || ext == ".mdown" || ext == ".mkd";
    }

    // ---- 요청 처리: 앱 리소스, 마크다운 원문, 이미지 등 ----

    void OnRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = web.CoreWebView2.Environment;
        Uri uri = new Uri(e.Request.Uri);
        string path = uri.AbsolutePath;

        if (path.StartsWith("/__app/"))
        {
            Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream(path.Substring(7));
            e.Response = res == null ? NotFound(env) : Ok(env, res, Mime(path));
            return;
        }

        string file = ToPath(uri);
        if (file == null || !File.Exists(file))
        {
            e.Response = NotFound(env);
            return;
        }

        bool raw = uri.Query.Contains("raw");
        if (IsMarkdown(file) && !raw)
        {
            Stream page = Assembly.GetExecutingAssembly().GetManifestResourceStream("viewer.html");
            e.Response = Ok(env, page, "text/html; charset=utf-8");
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(file);
            string mime = raw ? "text/plain; charset=utf-8" : Mime(file);
            e.Response = Ok(env, new MemoryStream(bytes), mime);
        }
        catch (IOException)
        {
            e.Response = NotFound(env);
        }
    }

    static CoreWebView2WebResourceResponse Ok(CoreWebView2Environment env, Stream body, string mime)
    {
        return env.CreateWebResourceResponse(body, 200, "OK", "Content-Type: " + mime + "\r\nCache-Control: no-store");
    }

    static CoreWebView2WebResourceResponse NotFound(CoreWebView2Environment env)
    {
        return env.CreateWebResourceResponse(null, 404, "Not Found", "");
    }

    static string Mime(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".html": case ".htm": return "text/html; charset=utf-8";
            case ".css": return "text/css; charset=utf-8";
            case ".js": return "text/javascript; charset=utf-8";
            case ".png": return "image/png";
            case ".jpg": case ".jpeg": return "image/jpeg";
            case ".gif": return "image/gif";
            case ".svg": return "image/svg+xml";
            case ".webp": return "image/webp";
            case ".bmp": return "image/bmp";
            case ".ico": return "image/x-icon";
            case ".pdf": return "application/pdf";
            case ".txt": case ".csv": case ".json": case ".py": case ".cs": case ".ts": return "text/plain; charset=utf-8";
            default: return "application/octet-stream";
        }
    }

    // ---- 이동 처리 ----

    void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        Uri uri;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri)) return;
        if (uri.Host == Host) return;

        e.Cancel = true;
        // 창에 파일을 끌어다 놓은 경우
        if (uri.IsFile && IsMarkdown(uri.LocalPath))
        {
            web.CoreWebView2.Navigate(ToUrl(uri.LocalPath));
            return;
        }
        OpenLink(e.Uri);
    }

    void OpenLink(string link)
    {
        Uri uri;
        if (!Uri.TryCreate(link, UriKind.Absolute, out uri)) return;
        string target = uri.Host == Host ? ToPath(uri) : link;
        if (target == null) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    void OnDocumentChanged(string source)
    {
        Uri uri;
        if (!Uri.TryCreate(source, UriKind.Absolute, out uri) || uri.Host != Host) return;
        string file = ToPath(uri);
        if (file == null || string.Equals(file, currentFile, StringComparison.OrdinalIgnoreCase)) return;
        currentFile = file;
        dirty = false;
        Watch(file);
    }

    // 파일이 수정되면 자동으로 다시 그린다
    void Watch(string file)
    {
        if (watcher != null) watcher.Dispose();
        watcher = null;
        try
        {
            watcher = new FileSystemWatcher(Path.GetDirectoryName(file), Path.GetFileName(file));
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
            watcher.SynchronizingObject = this;
            FileSystemEventHandler h = delegate { reloadTimer.Stop(); reloadTimer.Start(); };
            watcher.Changed += h;
            watcher.Created += h;
            watcher.Renamed += delegate { reloadTimer.Stop(); reloadTimer.Start(); };
            watcher.EnableRaisingEvents = true;
        }
        catch (ArgumentException) { }
    }

    // ---- 편집·저장, 단축키 (viewer.html에서 전달) ----

    void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string msg = e.TryGetWebMessageAsString();
        if (msg.StartsWith("save\n"))
        {
            Save(msg.Substring(5));
        }
        else if (msg.StartsWith("copy\n"))
        {
            string text = msg.Substring(5).Replace("\r\n", "\n").Replace("\n", "\r\n");
            try
            {
                Clipboard.SetText(text.Length > 0 ? text : " ");
                web.CoreWebView2.ExecuteScriptAsync("window.onCopied()");
            }
            catch (System.Runtime.InteropServices.ExternalException) { }
        }
        else if (msg == "dirty" || msg == "clean")
        {
            dirty = msg == "dirty";
            UpdateTitle();
        }
        else if (msg == "open")
        {
            string f = Program.PickFile(this);
            if (f != null) web.CoreWebView2.Navigate(ToUrl(f));
        }
        else if (msg == "close")
        {
            Close();
        }
    }

    void Save(string text)
    {
        if (currentFile == null) return;
        try
        {
            bool bom = HasBom(currentFile);
            ignoreChangesUntil = DateTime.Now.AddSeconds(1.5);
            File.WriteAllText(currentFile, text, new UTF8Encoding(bom));
            ignoreChangesUntil = DateTime.Now.AddSeconds(1.5);
            web.CoreWebView2.ExecuteScriptAsync("window.onSaved()");
            if (closeAfterSave)
            {
                dirty = false;
                Close();
            }
        }
        catch (Exception ex)
        {
            closeAfterSave = false;
            string m = ex.Message.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", " ").Replace("\n", " ");
            web.CoreWebView2.ExecuteScriptAsync("window.onSaveFailed('" + m + "')");
        }
    }

    static bool HasBom(string file)
    {
        try
        {
            using (var fs = File.OpenRead(file))
            {
                var b = new byte[3];
                return fs.Read(b, 0, 3) == 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
            }
        }
        catch (IOException) { return false; }
    }

    void UpdateTitle()
    {
        string name = web.CoreWebView2 != null ? web.CoreWebView2.DocumentTitle : Path.GetFileName(startFile);
        Text = (dirty ? "● " : "") + name + " - MdViewer";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (dirty && web.CoreWebView2 != null)
        {
            var r = MessageBox.Show(this, "변경 내용을 저장할까요?", "MdViewer",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel)
            {
                e.Cancel = true;
            }
            else if (r == DialogResult.Yes)
            {
                e.Cancel = true;
                closeAfterSave = true;
                web.CoreWebView2.ExecuteScriptAsync("window.saveAndClose()");
            }
            else
            {
                dirty = false;
            }
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (watcher != null) watcher.Dispose();
        base.OnFormClosed(e);
    }
}
