using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace ReadRS232.Core
{
    public enum LogKind { Info, Rx, Tx, Value, Out, Warn, Error }

    public sealed class LogEntry
    {
        public DateTime Time;
        public string Route;
        public LogKind Kind;
        public string Text;
        public byte[] Data;
    }

    /// <summary>
    /// Kênh log chung, thread-safe. Monitor trên UI rút log ra theo timer,
    /// cảnh báo / lỗi được ghi thêm vào file để tra cứu sau.
    /// </summary>
    public static class AppLog
    {
        const int MaxPending = 5000;
        static readonly ConcurrentQueue<LogEntry> Pending = new ConcurrentQueue<LogEntry>();
        static readonly object FileLock = new object();

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReadRS232");

        public static string ErrorLogPath => Path.Combine(Folder, "errors.log");

        public static void Write(string route, LogKind kind, string text, byte[] data = null)
        {
            var e = new LogEntry { Time = DateTime.Now, Route = route ?? "", Kind = kind, Text = text ?? "", Data = data };
            Pending.Enqueue(e);
            while (Pending.Count > MaxPending && Pending.TryDequeue(out _)) { }
            if (kind == LogKind.Error || kind == LogKind.Warn) WriteFile(e);
        }

        public static bool TryTake(out LogEntry e) => Pending.TryDequeue(out e);

        static void WriteFile(LogEntry e)
        {
            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(Folder);
                    var fi = new FileInfo(ErrorLogPath);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = ErrorLogPath + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(ErrorLogPath, old);
                    }
                    File.AppendAllText(ErrorLogPath,
                        $"{e.Time:yyyy-MM-dd HH:mm:ss.fff} [{e.Kind}] {e.Route}: {e.Text}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
