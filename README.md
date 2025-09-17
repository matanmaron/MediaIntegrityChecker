**MediaIntegrityChecker**

MediaIntegrityChecker is a lightweight, multithreaded tool for verifying the integrity of your media files, including images, videos, and audio. It scans directories recursively, checks each file, logs results, and can resume interrupted scans.

---

**Features**

* ✅ Image validation (.jpg, .jpeg, .png, .bmp, .gif, .tiff) using .NET's System.Drawing.
* ✅ Video/audio validation (.mp4, .mkv, .avi, .mov, .wmv, .mp3, .flac, .wav, .aac) using FFmpeg.
* ✅ Multithreaded processing for faster scans.
* ✅ Persistent state: keeps track of already scanned files in `logs/Processed.json`.
* ✅ Detailed logging of errors, warnings, and corruption to `logs/run_N.log`.
* ✅ Keeps a rolling history of scans (run\_N.json) for comparison.
* ✅ Detects new corruptions compared to the previous run and marks them as `NEW CORRUPTED:` in logs.
* ✅ Provides a summary at the end of each run (total files, verified, corrupted, new corrupted).

---

**Dependencies**

* **.NET 9 SDK or Runtime**

  * Required to build or run the application.
  * [Download from Microsoft](https://dotnet.microsoft.com/download)

* **FFmpeg (system-wide installation)**

  * Required for video/audio integrity checks.
  * Must be accessible in your system PATH.
  * [Download FFmpeg](https://ffmpeg.org/download.html)

---

**Build**

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

* Creates a standalone executable in `bin\Release\net9.0\win-x64\publish`.
* Single-file publishing supported via `-p:PublishSingleFile=true`.

---

**Usage**

```bash
MediaIntegrityChecker.exe <RootFolder>
```

* `<RootFolder>`: The folder to scan recursively for media files.

**Example:**

```bash
MediaIntegrityChecker.exe "C:\Users\Username\Pictures"
```

* Logs and processed file tracking are saved to `logs/` in the running folder:

  * `run_N.log` – contains detailed scan results with corruption markers.
  * `run_N.json` – keeps scan stats and list of corrupted files.
  * `Processed.json` – tracks already scanned files for faster resuming.

---

**How It Works**

* **Images:** Loaded using `System.Drawing.Image.FromFile`. Forced decoding ensures truncated images are detected. Failed loads are marked as corrupted.
* **Videos/Audio:** Validated via FFmpeg (`ffmpeg -v error -i <file> -f null -`). Any errors detected during decoding are logged as failures.
* **Other files:** Fully read to detect truncated or inaccessible files.
* The program uses multiple worker threads to scan files in parallel and ensures safe logging and processed-file updates.
* Each run is saved as `run_N.json`. On the second and later runs, new corruptions compared to the previous run are marked as `NEW CORRUPTED:` in the log.
* At the end of each run, a summary is appended to the log:

  * Files total
  * Files verified
  * Files corrupted
  * New corrupted (since last run)

---

**Example Output**

```
Worker 0 started
OK: C:\Users\User\Pictures\image1.jpg
CORRUPTED: C:\Users\User\Videos\badvideo.mp4
NEW CORRUPTED: C:\Users\User\Videos\badvideo.mp4
OK: C:\Users\User\Music\song.mp3
--- SUMMARY ---
Files total: 100
Files verified: 97
Files corrupted: 3
New corrupted (since last run): 1
✅ Scan finished. Log saved to logs/run_2.log
```

---

**Notes**

* Make sure FFmpeg is installed and available in your system PATH before running video/audio checks.
* The program can resume an interrupted scan automatically using `Processed.json`.
* All logs and JSON files are stored in the `logs/` folder in the program's running directory.
* Comparisons of new corruption are only performed from the second run onwards.
