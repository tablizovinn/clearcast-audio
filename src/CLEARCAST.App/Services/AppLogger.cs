using System;
using System.IO;

namespace CLEARCAST.App.Services;

public static class AppLogger
{
    private static readonly object SyncLock = new();

    private static readonly string LogDirectory =
        Path.Combine(
            AppContext.BaseDirectory,
            "Logs");

    private static string LogFilePath =>
        Path.Combine(
            LogDirectory,
            $"clearcast-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(
        string level,
        string message,
        Exception? exception)
    {
        try
        {
            lock (SyncLock)
            {
                Directory.CreateDirectory(LogDirectory);

                string entry =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] " +
                    $"[{level}] {message}";

                if (exception != null)
                {
                    entry += Environment.NewLine + exception;
                }

                File.AppendAllText(
                    LogFilePath,
                    entry + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }
}
