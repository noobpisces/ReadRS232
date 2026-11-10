using System;
using System.Deployment.Application;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ReadRS232.Core;
using ReadRS232.UI;

namespace ReadRS232
{
    internal static class Program
    {
        public static string ConfigPath { get; private set; } = AppConfig.DefaultPath;

        [DllImport("user32.dll")] static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        public static readonly int WM_SHOWME = RegisterWindowMessage("ReadRS232_ShowMe");

        /// <summary>
        /// Tham số dòng lệnh:  --config "D:\cau-hinh-can-2.xml"
        /// → chạy song song nhiều bản app, mỗi bản 1 file cấu hình riêng.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i].Equals("--config", StringComparison.OrdinalIgnoreCase))
                    ConfigPath = Path.GetFullPath(args[i + 1]);

            // Chỉ cho 1 bản chạy với mỗi file cấu hình (2 bản sẽ tranh nhau cổng COM)
            string mutexName = "Local\\ReadRS232_" + Hash(ConfigPath.ToLowerInvariant());
            using (var mutex = new Mutex(true, mutexName, out bool first))
            {
                if (!first)
                {
                    PostMessage((IntPtr)0xffff /*HWND_BROADCAST*/, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                System.ComponentModel.TypeDescriptor.AddAttributes(typeof(bool),
                    new System.ComponentModel.TypeConverterAttribute(typeof(VietBoolConverter)));
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Report(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => AppLog.Write("", LogKind.Error, "Lỗi nền: " + e.ExceptionObject);

                var cfg = AppConfig.Load(ConfigPath, out string warning);
                if (!File.Exists(ConfigPath))
                {
                    try { cfg.Save(ConfigPath); } catch { }
                }
                Application.Run(new MainForm(cfg, warning));
            }
        }

        static void Report(Exception ex)
        {
            AppLog.Write("", LogKind.Error, ex.ToString());
            MessageBox.Show(ex.Message + "\n\nChi tiết đã ghi vào:\n" + AppLog.ErrorLogPath, "ReadRS232 — lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static string Hash(string s)
        {
            using (var sha = SHA1.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(s)).Take(8).Select(b => b.ToString("x2")));
        }

        /// <summary>Lệnh để Windows tự chạy app khi đăng nhập (ClickOnce thì dùng shortcut .appref-ms để luôn chạy bản mới nhất).</summary>
        public static string StartupCommand()
        {
            string extra = ConfigPath == AppConfig.DefaultPath ? "" : $" --config \"{ConfigPath}\"";
            try
            {
                if (ApplicationDeployment.IsNetworkDeployed)
                {
                    string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    var link = Directory.EnumerateFiles(programs, "ReadRS232.appref-ms", SearchOption.AllDirectories).FirstOrDefault();
                    if (link != null) return $"\"{link}\"";
                }
            }
            catch { }
            return $"\"{Application.ExecutablePath}\"" + extra;
        }
    }
}
