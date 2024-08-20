using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Skatech.IO;

static class FileSync {
    // Return positive when working file newer than upstream, negative when upstream newer, otherwise zero
    public static long Look(string workingFile, string upstreamFile) {
        return LastWriteTime(workingFile) - LastWriteTime(upstreamFile);
    }

    // Copy working over upstream file if newer. Return null on success, otherwise error message
    public static string? Push(string workingFile, string upstreamFile) {
        long workingTime = LastWriteTime(workingFile), upstreamTime = LastWriteTime(upstreamFile);
        if (workingTime > upstreamTime) {
            if (Directory.Exists(Path.GetDirectoryName(upstreamFile))) {
                FileBackupIndex.FromFile(upstreamFile)?.BackupFile(upstreamFile, true);
                File.Copy(workingFile, upstreamFile, true);
            }
            else return "Upstream directory missing";
        }
        else if (workingTime < upstreamTime) {
            return workingTime > 0L
                ? "Working file outdated"
                : "Working file missing";
        }
        else return workingTime > 0L
            ? "Both files up to date"
            : "Both files missing";            
        return null;
    }

    // Copy upstream over working file if newer. Return null on success, otherwise error message
    public static string? Pull(string workingFile, string upstreamFile) {
        long workingTime = LastWriteTime(workingFile), upstreamTime = LastWriteTime(upstreamFile);
        if (upstreamTime > workingTime) {
            if (Directory.Exists(Path.GetDirectoryName(workingFile))) {
                FileBackupIndex.FromFile(workingFile)?.BackupFile(workingFile, true);
                File.Copy(upstreamFile, workingFile, true);
            }
            else return "Working directory missing";
        }
        else if (upstreamTime < workingTime) {
            return upstreamTime > 0L
                ? "Upstream file outdated"
                : "Upstream file missing";
        }
        else return upstreamTime > 0L
            ? "Both files up to date"
            : "Both files missing";
        return null;
    }

    // Syncronize files silently. Return null on success, otherwise error message
    public static string? Sync(string workingFile, string upstreamFile) {
        long comp = Look(workingFile, upstreamFile);
        if (comp < 0L) {
            if (Pull(workingFile, upstreamFile) is string error)
                return $"Pull: {error}";
        }
        if (comp > 0L) {
            if (Push(workingFile, upstreamFile) is string error)
                return $"Push: {error}";
        }
        return null;
    }

    ///<summary>Enumerate all possible selector matching files from working and upstream directories,
    /// with paths relative to corresponding directories, exclude internal service files</summary>
    public static IEnumerable<string> EnumerateMatchingFiles(string fileNameOrSelector,
            string workingDir, string upstreamDir, bool includeSubDirs) {
        if (FilePath.IsPattern(fileNameOrSelector)) {
            var searchOption = includeSubDirs
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            return Enumerable.Union(
                Directory.EnumerateFiles(workingDir, fileNameOrSelector,
                    searchOption).Select(f => Path.GetRelativePath(workingDir, f)),
                Directory.EnumerateFiles(upstreamDir, fileNameOrSelector,
                    searchOption).Select(f => Path.GetRelativePath(upstreamDir, f)),
                        StringComparer.OrdinalIgnoreCase).Order();
        }
        return Enumerable.Repeat(fileNameOrSelector, 1);
    }

    ///<summary>Syncronize files with console detailed log</summary>
    public static void SyncAnnotated(string workingFile, string upstreamFile, bool allowPush, bool allowPull) {
        Console.Write($"{Path.GetFileName(workingFile.AsSpan()),-19} ");
        if (IsDriveAvailable(upstreamFile)) {
            long comp = Look(workingFile, upstreamFile);
            if (comp < 0L) {
                Console.Write(File.Exists(workingFile)
                    ? "working file outdated"
                    : "working file missing");
                if (allowPull) {
                    Console.Write(" - Pull... ");
                    if (Pull(workingFile, upstreamFile) is string error) {
                        using var cc = ConsoleColors.FromForeground(ConsoleColor.Yellow);
                        Console.WriteLine(error);
                    }
                    else Console.WriteLine("OK");
                }
                else Console.WriteLine(" (pull available)");
            }
            else if (comp > 0L) {
                Console.Write(File.Exists(upstreamFile)
                    ? "upstream file outdated"
                    : "upstream file missing");
                if (allowPush) {
                    Console.Write(" - Push... ");
                    if (Push(workingFile, upstreamFile) is string error) {
                        using var cc = ConsoleColors.FromForeground(ConsoleColor.Yellow);
                        Console.WriteLine(error);
                    }
                    else Console.WriteLine("OK");
                }
                else Console.WriteLine(" (push available)");
            }
            else {
                if (!File.Exists(workingFile)) {
                    using var cc = ConsoleColors.FromForeground(ConsoleColor.Yellow);
                    Console.WriteLine("both files missing");
                }
                else Console.WriteLine("up to date");
            }
        }
        else {
            using var cc = ConsoleColors.FromForeground(ConsoleColor.DarkBlue);
            Console.WriteLine("upstream drive offline");
        }
    }

    public static long LastWriteTime(string filePath) {
        return File.Exists(filePath) ? File.GetLastWriteTime(filePath).ToFileTime() : 0L;
    }

    public static bool IsDriveAvailable(string fileOrDirectory) {
        return Directory.Exists(Directory.GetDirectoryRoot(fileOrDirectory));
    }
}

class FileSyncIndex {
    public const string FileNameNoExt = "sync-index", DefaultExt = ".csv", ArchiveExt = ".acsv";
    public const string IgnoreListFileName = "sync-ignore.lst";
    readonly List<Record> _records;
    public readonly string IndexFile, IndexDir;

    ///<summary>Enumerate records with selector matched given file path, which can be relative or rooted</summary>
    public IEnumerable<Record> GetMatchingRecords(string filePath) {
        string relativePath = Path.IsPathRooted(filePath)
            ? Path.GetRelativePath(IndexDir, filePath) : filePath;
        return _records.Where(r => FilePath.IsMatch(relativePath, r.FileSelector));
    }

    public FileSyncIndex(IEnumerable<Record> records, string indexFile) {
        Debug.Assert(Path.IsPathRooted(indexFile),
                "File sync index file path must be rooted");
        IndexDir = Path.GetDirectoryName(IndexFile = indexFile)!;
        _records = records.ToList();
    }

    ///<summary>Save index to file</summary>
    public void Save() {
        SaveIndex(_records, IndexFile);
    }

    ///<summary>Return new record created and added to index</summary>
    public Record CreateRecord(string workingFile, string upstreamFile) {
        var record = new Record(workingFile, upstreamFile);
        _records.Add(record);
        return record;
    }

    ///<summary>Syncronize files with console detailed log</summary>
    public void SyncAnnotated(bool allowPush,
            bool allowPull, bool includeSubDirs, string? fileFilterPattern) {
        var ignoreList = LoadCombinedIgnoreList(IndexDir);
        foreach (var record in _records)
            record.SyncAnnotated(IndexDir, allowPush, allowPull,
                    includeSubDirs, fileFilterPattern, ignoreList);
    }

    ///<summary>Open index file in editor</summary>
    public static void CreateAndEditIndexFileAnnotated(string directoryOrIndexFilePathMaybeNoExt) {
        Console.Write(CreateIndexFile(directoryOrIndexFilePathMaybeNoExt)
            ? "Creating file... Awaiting editor... " : "Awaiting editor... ");
        string indexPath = InferIndexFilePath(directoryOrIndexFilePathMaybeNoExt);
        Console.WriteLine(TextConfig.TryOpenWithAppSync(indexPath, DefaultExt)
            ? "OK" : "OK (unchanged)");
    }

    ///<summary>Init index file, return false when already exists</summary>
    public static bool CreateIndexFile(string directoryOrIndexFilePathMaybeNoExt) {
        var indexPath = InferIndexFilePath(
            Path.GetFullPath(directoryOrIndexFilePathMaybeNoExt));
        if (File.Exists(indexPath)) {
            return false;
        }
        var sampleRecord = new Record(
            "file-name-or-pattern-absolute-or-relative", "upstream-directory");
        var lines = $"""
            # sync index file template; add your records using format below:
            # {sampleRecord}
            """.Split(Environment.NewLine);
        TextConfig.WriteLines(lines, indexPath, IsArchiveIndexFilePath(indexPath));
        return true;
    }

    ///<summary>Pack existing index file</summary>
    public static void PackIndexFileAnnotated(string directoryOrIndexFilePathMaybeNoExt) {
        var indexPath = InferIndexFilePath(
                Path.GetFullPath(directoryOrIndexFilePathMaybeNoExt));
        if (IsArchiveIndexFilePath(indexPath)) {
            Console.WriteLine("Index file already packed");
            return;
        }
        else if (File.Exists(indexPath)) {
            Console.Write("Packing index... ");
            ADocFileFormat.Compress(indexPath,
                Path.ChangeExtension(indexPath, ArchiveExt));
            File.Delete(indexPath);
            Console.WriteLine("OK");
        }
        else Console.WriteLine("Index file not exists");
    }

    ///<summary>Create index from directory with default index file or custom index file</summary>
    public static FileSyncIndex? TryOpen(string directoryOrIndexFilePathMaybeNoExt) {
        var indexPath = InferIndexFilePath(directoryOrIndexFilePathMaybeNoExt);
        return TextConfig.TryReadLines(indexPath, IsArchiveIndexFilePath(indexPath))
            ?.TrimSpacesAndComments()?.Select(Record.Parse)?.To(s => new FileSyncIndex(s, indexPath));
    }

    ///<summary>Save index to file, text or archived format selected by extension</summary>
    public static void SaveIndex(IEnumerable<Record> records, string filePath) {
        TextConfig.WriteLines(records.Select(r => r.ToString()),
            filePath, IsArchiveIndexFilePath(filePath));
    }

    ///<summary>Return inferred index file path, archived or normal, default or custom</summary>
    public static string InferIndexFilePath(string directoryOrFilePathMaybeNoExt) {
        Debug.Assert(Path.IsPathRooted(directoryOrFilePathMaybeNoExt),
            "Sync index clue path must be rooted");
        return TextConfig.InferConfigFilePath(
            directoryOrFilePathMaybeNoExt, FileNameNoExt, DefaultExt, ArchiveExt);
    }

    ///<summary>Return true when path to archived index file, throw exception on unsupported file type</summary>
    public static bool IsArchiveIndexFilePath(string filePath) {
        return filePath.EndsWith(ArchiveExt, StringComparison.OrdinalIgnoreCase)
            ? true : filePath.EndsWith(DefaultExt, StringComparison.OrdinalIgnoreCase)
                ? false : throw new Exception(
                    $"Unsupported index file extension '{Path.GetExtension(filePath)}'");
    }

    ///<summary>Open ignore list in editor, create when not exists</summary>
    public static void CreateAndEditIgnoreListAnnotated(string? directoryOrNullForGlobal = null) {
        Console.Write(CreateIgnoreList(out string filePath, directoryOrNullForGlobal)
            ? "Creating file... Awaiting editor... " : "Awaiting editor... ");
        Console.WriteLine(FilePath.TryOpenWithAppSync(filePath)
            ? "OK" : "OK (unchanged)");
    }

    ///<summary>Load records from local and global ignore lits, creates global ignore list when not exists</summary>
    public static FileList LoadCombinedIgnoreList(string workingDir) {
        CreateIgnoreList(out string globalFileListPath);
        return FileList.FromFiles(globalFileListPath,
            FileList.GetPath(IgnoreListFileName, workingDir));
    }

    static bool CreateIgnoreList(out string filePath, string? directoryOrNullForGlobal = null) {
        if (File.Exists(filePath = FileList.GetPath(IgnoreListFileName, directoryOrNullForGlobal)))
            return false;
        string commentText = "file ignore list for sync utility, add lines with file names or patterns";
        return directoryOrNullForGlobal is null
            ? FileList.CreateFile(filePath, "global " + commentText,
                $"{FileBackup.BackupDirectoryName}\\*",
                $"{FileSyncIndex.FileNameNoExt}{FileSyncIndex.DefaultExt}",
                $"{FileSyncIndex.FileNameNoExt}{FileSyncIndex.ArchiveExt}",
                $"{IgnoreListFileName}",
                $"*\\{FileBackup.BackupDirectoryName}\\*",
                $"*\\{FileSyncIndex.FileNameNoExt}{FileSyncIndex.DefaultExt}",
                $"*\\{FileSyncIndex.FileNameNoExt}{FileSyncIndex.ArchiveExt}",
                $"*\\{IgnoreListFileName}")
            : FileList.CreateFile(filePath, commentText);
    }

    ///<summary>Enumerates upstream files for file. When directory-or-index parameter omited,
    /// file directory used as index root. Return null when index file not exists</summary>
    public static IEnumerable<string>? EnumerateUpstreamFilesForFile(string filePath,
                string? directoryOrIndexFilePathMaybeNoExt,
                Dictionary<string, FileSyncIndex?> indexCache) {
        string indexPath = InferIndexFilePath(directoryOrIndexFilePathMaybeNoExt
            ?? Path.GetDirectoryName(filePath)
                ?? throw new Exception("Can't get working file directory"));
        
        if (!indexCache.TryGetValue(indexPath, out FileSyncIndex? syncIndex)) {
            indexCache.Add(indexPath, syncIndex = FileSyncIndex.TryOpen(indexPath));
        }
        if (syncIndex is not null) {
            string fileName = Path.GetRelativePath(syncIndex.IndexDir, filePath);
            return syncIndex.GetMatchingRecords(fileName)
                .Select(r => Path.Combine(r.UpstreamDirectory, fileName));
        }
        return null;
    }

    public class Record {
        static readonly Regex Parser = new(
            @"\A\""([\w\s-+$@%\(\)\\/,.:'*?]+)\"",\s*\""([\w\s-+$@%\(\)\\/,.:']+)\""\z",
            RegexOptions.Compiled);

        public readonly string FileSelector, UpstreamDirectory;

        public Record(string fileSelector, string upstreamDirectory) {
            FileSelector = fileSelector; UpstreamDirectory = upstreamDirectory;
        }

        public override string ToString() {
            return $"\"{FileSelector}\", \"{UpstreamDirectory}\"";
        }

        ///<summary>Syncronize files with console detailed log</summary>
        public void SyncAnnotated(string workingDir, bool allowPush, bool allowPull,
                    bool includeSubDirs, string? fileFilterPattern, FileList? ignoreList) {
            Console.WriteLine($"{workingDir}\\{FileSelector}  -->  {UpstreamDirectory}");
            int filecount = 0, filematch = 0;
            foreach(var fileName in FileSync.EnumerateMatchingFiles(
                    FileSelector, workingDir, UpstreamDirectory, includeSubDirs)) {
                if(ignoreList?.FirstMatchOrDefault(fileName) is string ignoreMatch) {
                    // Console.WriteLine($"    {fileName,-19} ignored by match '{ignoreMatch}'");
                    continue;
                }
                if (FilePath.IsMatch(Path.GetFileName(fileName.AsSpan()), fileFilterPattern)) {
                    Console.Write("    ");
                    FileSync.SyncAnnotated(Path.Join(workingDir, fileName),
                        Path.Join(UpstreamDirectory, fileName), allowPush, allowPull);
                    filematch++;
                }
                filecount++;
            }
            if (filecount < 1) {
                Console.WriteLine("    No files found");
            }
            else if (filematch < 1) {
                Console.WriteLine("    No files matching");
            }
        }

        public static Record Parse(string input) {
            var match = Parser.Match(input);
            return match.Success
                ? new (match.Groups[1].Value, match.Groups[2].Value)
                : throw new FormatException("Sync record incorrect format");
        }
    }
}
