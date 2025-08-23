# MediaIntegrityChecker

**MediaIntegrityChecker** is a lightweight, multithreaded tool for verifying the integrity of your media files, including images, videos, and audio. It scans directories recursively, checks each file, logs results, and can resume interrupted scans.

---

## Features

* ✅ Image validation (`.jpg`, `.jpeg`, `.png`, `.bmp`, `.gif`, `.tiff`) using .NET's `System.Drawing`.
* ✅ Video/audio validation (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.mp3`, `.flac`, `.wav`, `.aac`) using FFmpeg.
* ✅ Multithreaded processing for faster scans.
* ✅ Persistent state: keeps track of already scanned files in `Processed.json`.
* ✅ Detailed logging of errors and warnings to `IntegrityLog.txt`.

---

## Dependencies

* **.NET 9 SDK or Runtime**

  * Required to build or run the application.
  * [Download from Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

* **FFmpeg** (system-wide installation)

  * Required for video/audio integrity checks.
  * Must be accessible in your system PATH.
  * [Download FFmpeg](https://ffmpeg.org/download.html)

---

## Build

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

* Creates a standalone executable in `bin\Release\net9.0\win-x64\publish`.
* Single-file publishing supported via `-p:PublishSingleFile=true`.

---

## Usage

```bash
MediaIntegrityChecker.exe <RootFolder>
```

* `<RootFolder>`: The folder to scan recursively for media files.
* Example:

```bash
MediaIntegrityChecker.exe "C:\Users\Username\Pictures"
```

* Logs and processed file tracking are saved to your Desktop:

  * `IntegrityLog.txt` – contains detailed scan results.
  * `Processed.json` – tracks already scanned files.

---

## How It Works

1. **Images**: Loaded using `System.Drawing.Image.FromFile`. If the image fails to load, it’s marked as corrupted.
2. **Videos/Audio**: Validated via FFmpeg (`ffmpeg -v error -i <file> -f null -`). Any errors detected during decoding are logged as failures.
3. **Other files**: A simple read test is performed.

The program uses multiple worker threads to scan files in parallel and ensures safe logging and processed-file updates.

---

## Example Output

```
Worker 0 started
OK: C:\Users\User\Pictures\image1.jpg
WARN/FAIL: C:\Users\User\Videos\badvideo.mp4 | [ffmpeg error details]
OK: C:\Users\User\Music\song.mp3
✅ Scan finished. Log saved to C:\Users\User\Desktop\IntegrityLog.txt
```

---

## Notes

* Make sure FFmpeg is installed and available in your system PATH before running video/audio checks.
* The program can resume an interrupted scan automatically using `Processed.json`.
