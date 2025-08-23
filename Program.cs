using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing; // for image validation

class Program
{
    static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "IntegrityLog.txt"
    );
    static readonly string ProcessedFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "Processed.json"
    );

    static readonly string[] SystemFolders = { "System Volume Information", "$RECYCLE.BIN" };
    static readonly BlockingCollection<string> FileQueue = new BlockingCollection<string>(new ConcurrentQueue<string>());
    static readonly object LogLock = new object();
    static readonly object ProcessedLock = new object();

    static HashSet<string> Processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: MediaIntegrityChecker <RootFolder>");
            return;
        }
        string RootPath = args[0];
        if (!Directory.Exists(RootPath))
        {
            Console.WriteLine($"Error: Directory '{RootPath}' does not exist.");
            return;
        }

        Console.WriteLine($"Scanning root folder: {RootPath}");

        LoadProcessed();

        Write("Collecting files...", false, true);

        // Gather files
        Task.Run(() => CollectFiles(RootPath));

        int threadCount = 4;
        var workers = Enumerable.Range(0, threadCount)
            .Select(i => Task.Run(() => Worker(i)))
            .ToArray();

        Task.WaitAll(workers);

        Write("✅ Scan finished. Log saved to " + LogFile, true, true);
    }

    static void LoadProcessed()
    {
        if (File.Exists(ProcessedFile))
        {
            try
            {
                var list = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(ProcessedFile));
                if (list != null)
                    Processed = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);

                Write($"Loaded {Processed.Count} already processed files.", false, true);
            }
            catch (Exception ex)
            {
                Write($"⚠ Failed to load Processed.json: {ex.Message}", false, true);
            }
        }
    }

    static void SaveProcessed(string file)
    {
        lock (ProcessedLock)
        {
            Processed.Add(file);
            File.WriteAllText(ProcessedFile, JsonSerializer.Serialize(Processed, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    static void CollectFiles(string root)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                string dirName = Path.GetFileName(dir);

                if (dirName.StartsWith(".") || SystemFolders.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                    continue;

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (!Processed.Contains(file)) // skip already checked
                        FileQueue.Add(file);
                }
                Write($"{FileQueue.Count} Files queued", false, true);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Write($"Access denied: {root} ({ex.Message})", true, true);
        }
        finally
        {
            FileQueue.CompleteAdding();
        }
    }

    static void Worker(int id)
    {
        Console.WriteLine($"Worker {id} started");
        foreach (var file in FileQueue.GetConsumingEnumerable())
        {
            try
            {
                if (CheckFileIntegrity(file))
                {
                    Write($"OK: {file}", false, true);
                }
                else
                {
                    Write($"ERROR: {file} -> Failed integrity check", true, true);
                }
                SaveProcessed(file); // always mark processed
            }
            catch (Exception ex)
            {
                Write($"ERROR: {file} -> {ex.Message}", true, true);
                SaveProcessed(file);
            }
        }
    }

    static bool CheckFileIntegrity(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        // Validate images
        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".tiff")
        {
            try
            {
                using (var img = Image.FromFile(filePath))
                {
                    // If loaded successfully → good
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Validate videos/audio with ffmpeg
        if (ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" || ext == ".wmv" ||
            ext == ".mp3" || ext == ".flac" || ext == ".wav" || ext == ".aac")
        {
            return RunFFmpeg(filePath);
        }

        // Default: just check if file can be opened
        try
        {
            using var fs = File.OpenRead(filePath);
            fs.ReadByte();
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool RunFFmpeg(string file)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-v error -i \"{file}\" -f null -",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // Only fail if there are real errors, not warnings
        if (process.ExitCode != 0)
        {
            Write($"FAIL: {file} | ffmpeg exit code {process.ExitCode}", true, true);
            return false;
        }

        // Sometimes corrupted frames show up as errors in stderr
        if (!string.IsNullOrEmpty(errors))
        {
            Write($"WARN/FAIL: {file} | {errors}", true, true);

            // Decide if you want to fail on any stderr output:
            return false;
        }

        return true;
    }

    static void Write(string message, bool toLog, bool toConsole)
    {
        if (toLog)
        {
            lock (LogLock)
            {
                File.AppendAllText(LogFile, message + Environment.NewLine);
            }
        }
        if (toConsole)
        {
            Console.WriteLine(message);
        }
    }
}