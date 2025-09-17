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
    // Put logs and json in a ./logs directory (running folder)
    static readonly string LogsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
    static readonly string LogFile = Path.Combine(LogsDir, "IntegrityLog.txt");       // fallback / aggregate
    static readonly string ProcessedFile = Path.Combine(LogsDir, "Processed.json");   // moved from Desktop into ./logs

    static readonly string[] SystemFolders = { "System Volume Information", "$RECYCLE.BIN" };
    static readonly BlockingCollection<string> FileQueue = new BlockingCollection<string>(new ConcurrentQueue<string>());
    static readonly object LogLock = new object();
    static readonly object ProcessedLock = new object();

    static HashSet<string> Processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // New small additions (thread-safe)
    static ConcurrentBag<string> CorruptedFiles = new ConcurrentBag<string>();
    static int TotalFiles = 0;
    static int VerifiedFiles = 0;
    static int CorruptedCount = 0;

    // Current run files
    static string CurrentRunLogFile = null!;
    static string CurrentRunJsonFile = null!;
    static int CurrentRunId = 0;

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

        // ensure logs directory exists
        Directory.CreateDirectory(LogsDir);

        // determine run id
        CurrentRunId = GetNextRunId();
        CurrentRunLogFile = Path.Combine(LogsDir, $"run_{CurrentRunId}.log");
        CurrentRunJsonFile = Path.Combine(LogsDir, $"run_{CurrentRunId}.json");

        Console.WriteLine($"Scanning root folder: {RootPath}");
        Console.WriteLine($"Run #{CurrentRunId} log: {CurrentRunLogFile}");
        Console.WriteLine($"Run #{CurrentRunId} json: {CurrentRunJsonFile}");

        LoadProcessed();

        Write("Collecting files...", false, true);

        // Gather files
        Task.Run(() => CollectFiles(RootPath));

        int threadCount = 4;
        var workers = Enumerable.Range(0, threadCount)
            .Select(i => Task.Run(() => Worker(i)))
            .ToArray();

        Task.WaitAll(workers);

        // After workers finished — write run JSON summary
        var runData = new RunData
        {
            RunId = CurrentRunId,
            Timestamp = DateTime.UtcNow,
            Stats = new Stats
            {
                FilesTotal = TotalFiles,
                FilesVerified = VerifiedFiles,
                FilesCorrupted = CorruptedCount
            },
            CorruptedFiles = CorruptedFiles.ToList()
        };

        try
        {
            File.WriteAllText(CurrentRunJsonFile, JsonSerializer.Serialize(runData, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Write($"⚠ Failed to save run JSON: {ex.Message}", true, true);
        }

        // Compare with previous run if available
        int newCorruptedCount = 0;
        if (CurrentRunId > 1)
        {
            string prevJson = Path.Combine(LogsDir, $"run_{CurrentRunId - 1}.json");
            if (File.Exists(prevJson))
            {
                try
                {
                    var prevRun = JsonSerializer.Deserialize<RunData>(File.ReadAllText(prevJson));
                    var newCorrupted = runData.CorruptedFiles
                        .Except(prevRun!.CorruptedFiles, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    newCorruptedCount = newCorrupted.Count;

                    foreach (var file in newCorrupted)
                    {
                        Write($"NEW CORRUPTED: {file}", true, true);
                    }

                    Console.WriteLine($"🔎 Comparison done. {newCorrupted.Count} new corrupted files found.");
                }
                catch (Exception ex)
                {
                    Write($"⚠ Failed to compare with previous run: {ex.Message}", true, true);
                }
            }
            else
            {
                Write("ℹ Previous run JSON not found for comparison.", true, true);
            }
        }
        else
        {
            Write("ℹ First run detected. No comparison performed.", true, true);
        }

        // Summary appended to this run log
        Write("--- SUMMARY ---", true, true);
        Write($"Files total: {runData.Stats.FilesTotal}", true, true);
        Write($"Files verified: {runData.Stats.FilesVerified}", true, true);
        Write($"Files corrupted: {runData.Stats.FilesCorrupted}", true, true);
        if (CurrentRunId > 1)
            Write($"New corrupted (since last run): {newCorruptedCount}", true, true);

        Write("✅ Scan finished. Log saved to " + CurrentRunLogFile, true, true);
    }

    static int GetNextRunId()
    {
        try
        {
            var existing = Directory.GetFiles(LogsDir, "run_*.json")
                                .Select(f => Path.GetFileNameWithoutExtension(f)!)
                                .Where(f => f.StartsWith("run_"))
                                .Select(f => int.TryParse(f.Replace("run_", ""), out int id) ? id : 0)
                                .ToList();

            return existing.Count == 0 ? 1 : existing.Max() + 1;
        }
        catch
        {
            return 1;
        }
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
            try
            {
                File.WriteAllText(ProcessedFile, JsonSerializer.Serialize(Processed, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Write($"⚠ Failed to save Processed.json: {ex.Message}", true, true);
            }
        }
    }

    static void CollectFiles(string root)
    {
        try
        {
            // include files in the root folder itself
            foreach (var file in Directory.EnumerateFiles(root))
            {
                if (!Processed.Contains(file)) // skip already checked
                {
                    FileQueue.Add(file);
                    Interlocked.Increment(ref TotalFiles);
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                string dirName = Path.GetFileName(dir);

                if (dirName.StartsWith(".") || SystemFolders.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                    continue;

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (!Processed.Contains(file)) // skip already checked
                    {
                        FileQueue.Add(file);
                        Interlocked.Increment(ref TotalFiles);
                    }
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
                    Interlocked.Increment(ref VerifiedFiles);
                    Write($"OK: {file}", false, true);
                }
                else
                {
                    CorruptedFiles.Add(file);
                    Interlocked.Increment(ref CorruptedCount);
                    Write($"CORRUPTED: {file}", true, true);
                }
                SaveProcessed(file); // always mark processed
            }
            catch (Exception ex)
            {
                CorruptedFiles.Add(file);
                Interlocked.Increment(ref CorruptedCount);
                Write($"CORRUPTED: {file} -> {ex.Message}", true, true);
                SaveProcessed(file);
            }
        }
    }

    static bool CheckFileIntegrity(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        // Validate images (force full decode)
        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".tiff")
        {
            try
            {
                using (var img = Image.FromFile(filePath))
                {
                    // create a new bitmap from the image to force decoding the pixel data
                    using (var bmp = new Bitmap(img))
                    {
                        // access properties to help force load/validation
                        int w = bmp.Width;
                        int h = bmp.Height;
                    }
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

        // Default: read the whole file (not just one byte) to catch truncated files
        try
        {
            using var fs = File.OpenRead(filePath);
            byte[] buffer = new byte[81920];
            while (fs.Read(buffer, 0, buffer.Length) > 0) { /* read to EOF */ }
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

        try
        {
            using var process = Process.Start(psi)!;
            if (process == null)
            {
                Write($"WARN: ffmpeg start returned null for {file}", true, true);
                // be permissive if ffmpeg couldn't be started
                return true;
            }

            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // Fail on non-zero exit code or any stderr output
            if (process.ExitCode != 0)
            {
                Write($"FAIL: {file} | ffmpeg exit code {process.ExitCode}", true, true);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(errors))
            {
                Write($"FAIL: {file} | ffmpeg stderr: {errors.Trim()}", true, true);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // If ffmpeg isn't present or fails to start, we log a warning and skip media deep-check
            Write($"WARN: ffmpeg failed to run for {file}: {ex.Message}", true, true);
            return true;
        }
    }

    static void Write(string message, bool toLog, bool toConsole)
    {
        if (toLog)
        {
            lock (LogLock)
            {
                try
                {
                    // Prefer per-run log; fall back to aggregate LogFile
                    var path = CurrentRunLogFile ?? LogFile;
                    File.AppendAllText(path, message + Environment.NewLine);
                }
                catch
                {
                    // best-effort: if logging fails, still print to console below if requested
                }
            }
        }
        if (toConsole)
        {
            Console.WriteLine(message);
        }
    }
}

class RunData
{
    public int RunId { get; set; }
    public DateTime Timestamp { get; set; }
    public Stats Stats { get; set; } = new Stats();
    public List<string> CorruptedFiles { get; set; } = new List<string>();
}

class Stats
{
    public int FilesTotal { get; set; }
    public int FilesVerified { get; set; }
    public int FilesCorrupted { get; set; }
}
