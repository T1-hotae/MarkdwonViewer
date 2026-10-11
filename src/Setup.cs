// MdViewer 설치 프로그램. 실행 파일과 DLL을 리소스로 품고 있다가
// %LOCALAPPDATA%\Programs\MdViewer 에 풀고, 연결 프로그램·시작 메뉴·앱 목록에 등록한다.
// 관리자 권한 없이 현재 사용자에게만 설치된다.
//   MdViewer-Setup.exe              설치
//   uninstall.exe /uninstall        제거 (설치 폴더에 복사된 같은 프로그램)
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("MdViewer 설치")]
[assembly: AssemblyProduct("MdViewer")]
[assembly: AssemblyVersion("1.0.0.0")]

static class Setup
{
    const string AppName = "MdViewer";
    const string Version = "1.0.0";
    const string ProgId = "MdViewer.md";
    static readonly string[] Exts = { ".md", ".markdown" };
    static readonly string[] Files = {
        "MdViewer.exe", "Microsoft.Web.WebView2.Core.dll",
        "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll"
    };

    static string InstallDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\MdViewer"); }
    }

    static string ShortcutPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "MdViewer.lnk"); }
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        bool uninstall = args.Length > 0 && args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (uninstall) Uninstall();
            else Install();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppName + (uninstall ? " 제거" : " 설치"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------- 설치 ----------------

    static void Install()
    {
        if (Ask("MdViewer를 설치할까요?\n\n설치 위치: " + InstallDir +
                "\n\n.md 파일을 MdViewer로 열 수 있게 등록하고, 시작 메뉴에 추가합니다.") != DialogResult.Yes)
            return;

        if (Process.GetProcessesByName("MdViewer").Length > 0)
            throw new Exception("열려 있는 MdViewer 창을 모두 닫고 다시 실행해 주세요.");

        string dir = InstallDir;
        Directory.CreateDirectory(dir);
        foreach (string f in Files) Extract(f, Path.Combine(dir, f));
        string self = Assembly.GetExecutingAssembly().Location;
        string uninstaller = Path.Combine(dir, "uninstall.exe");
        if (!string.Equals(Path.GetFullPath(self), uninstaller, StringComparison.OrdinalIgnoreCase))
            File.Copy(self, uninstaller, true);

        string exe = Path.Combine(dir, "MdViewer.exe");
        Register(exe, uninstaller);
        CreateShortcut(exe);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

        string msg = "설치가 끝났습니다.\n\n" +
            ".md 파일을 더블클릭해도 다른 프로그램으로 열리면:\n" +
            "파일 우클릭 → 연결 프로그램 → 다른 앱 선택 → MdViewer → [항상]";
        if (!HasWebView2())
            msg += "\n\n※ 이 PC에 WebView2 런타임이 없는 것 같습니다. 실행이 안 되면\n" +
                   "https://developer.microsoft.com/microsoft-edge/webview2 에서 설치해 주세요.";
        MessageBox.Show(msg, AppName + " 설치", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void Extract(string name, string dest)
    {
        using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (res == null) throw new Exception("설치 파일이 손상되었습니다: " + name);
            using (var fs = File.Create(dest)) res.CopyTo(fs);
        }
    }

    static void Register(string exe, string uninstaller)
    {
        string open = "\"" + exe + "\" \"%1\"";
        using (var k = Classes(ProgId))
        {
            k.SetValue("", "Markdown 문서");
            using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue("", exe + ",0");
            using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", open);
        }
        using (var k = Classes(@"Applications\MdViewer.exe"))
        {
            k.SetValue("FriendlyAppName", AppName);
            using (var c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", open);
            using (var t = k.CreateSubKey("SupportedTypes"))
                foreach (string ext in Exts) t.SetValue(ext, "");
        }
        foreach (string ext in Exts)
        {
            using (var k = Classes(ext))
            {
                // 다른 기본 프로그램이 없을 때만 기본값으로 지정
                string cur = k.GetValue("") as string;
                if (string.IsNullOrEmpty(cur)) k.SetValue("", ProgId);
                using (var o = k.CreateSubKey("OpenWithProgids")) o.SetValue(ProgId, new byte[0], RegistryValueKind.None);
            }
        }
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", Version);
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("Publisher", AppName);
            k.SetValue("InstallLocation", Path.GetDirectoryName(exe));
            k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", 1500, RegistryValueKind.DWord);
        }
    }

    static RegistryKey Classes(string sub)
    {
        return Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + sub);
    }

    static void CreateShortcut(string exe)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(t);
        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
        Type lt = lnk.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exe });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(exe) });
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { exe + ",0" });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "마크다운 뷰어·편집기" });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }

    static bool HasWebView2()
    {
        string[] keys = {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            @"HKEY_CURRENT_USER\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
        };
        foreach (string k in keys)
        {
            string v = Registry.GetValue(k, "pv", null) as string;
            if (!string.IsNullOrEmpty(v) && v != "0.0.0.0") return true;
        }
        return false;
    }

    // ---------------- 제거 ----------------

    static void Uninstall()
    {
        if (Ask("MdViewer를 제거할까요?") != DialogResult.Yes) return;
        if (Process.GetProcessesByName("MdViewer").Length > 0)
            throw new Exception("열려 있는 MdViewer 창을 모두 닫고 다시 실행해 주세요.");

        var cls = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true);
        if (cls != null)
        {
            using (cls)
            {
                cls.DeleteSubKeyTree(ProgId, false);
                cls.DeleteSubKeyTree(@"Applications\MdViewer.exe", false);
                foreach (string ext in Exts)
                {
                    using (var k = cls.OpenSubKey(ext, true))
                    {
                        if (k == null) continue;
                        if (ProgId.Equals(k.GetValue("") as string)) k.DeleteValue("", false);
                        using (var o = k.OpenSubKey("OpenWithProgids", true))
                            if (o != null) o.DeleteValue(ProgId, false);
                    }
                }
            }
        }
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName, false);
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

        // 실행 중인 uninstall.exe 자신은 바로 지울 수 없으므로 잠시 뒤 폴더째 삭제
        string dir = InstallDir;
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdViewer");
        Process.Start(new ProcessStartInfo("cmd.exe",
            "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + dir + "\" & rmdir /s /q \"" + data + "\"")
        { CreateNoWindow = true, UseShellExecute = false });

        MessageBox.Show("MdViewer를 제거했습니다.", AppName + " 제거", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static DialogResult Ask(string text)
    {
        return MessageBox.Show(text, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
