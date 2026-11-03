namespace ScrcpySend.App;

internal static class AppLog
{
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScrcpySend-Windows");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, "diagnostics.log");
            if (File.Exists(path) && new FileInfo(path).Length >= 1024 * 1024)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path,
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
    }
}
