using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace Skatech.IO;

static class FileBackup {
    public const string BackupDirectoryName = ".backup";
    public static string GetBackupFileName(string file) {
        var fn = Path.GetFileNameWithoutExtension(file.AsSpan());
        var fx = Path.GetExtension(file.AsSpan());
        var fw = File.GetLastWriteTime(file).ToFileTimeUtc();
        return $"{fn}_backup#{fw}{fx}";
    }

    public static IEnumerable<string> EnumerateBackupFiles(string file, string backupDir) {
        var fn = Path.GetFileNameWithoutExtension(file.AsSpan());
        var fx = Path.GetExtension(file.AsSpan());
        string pattern = $"{fn}_backup#*{fx}";
        return Directory.Exists(backupDir)
            ? Directory.EnumerateFiles(backupDir, pattern, SearchOption.TopDirectoryOnly)
            : Enumerable.Empty<string>();
    }

    // Clean excessing backup files which not in protected period
    public static void Clean(string file,
                int copiesLimit, TimeSpan protectedPeriod = default, string? backupDir = default) {
        backupDir = backupDir ?? GetDefaultBackupDirectoryForFile(file);
        foreach(var copy in EnumerateBackupFiles(file, backupDir).Reverse().SkipWhile(
                s => DateTime.Now - File.GetLastWriteTime(s) < protectedPeriod).Skip(copiesLimit)) {
            File.Delete(copy);
        }
    }

    // Restore file from latest backup copy, return false when missing backup files
    public static bool Restore(string file, string? backupDir = default) {
        backupDir = backupDir ?? GetDefaultBackupDirectoryForFile(file);
        var latest = EnumerateBackupFiles(file, backupDir).LastOrDefault();
        if (latest is not null) {
            File.Copy(latest, file, true);
            return true;
        }
        return false;
    }

    // Return false when failed: source file not exists or backup file already exists
    public static bool Backup(string file, bool moveFile = false, string? backupDir = default) {
        backupDir = backupDir ?? GetDefaultBackupDirectoryForFile(file);
        var path = Path.Combine(backupDir, GetBackupFileName(file));
        if (File.Exists(file)) {
            if (moveFile) {
                Directory.CreateDirectory(backupDir);
                File.Move(file, path, true);
            }
            else if (File.Exists(path)) {
                return false;
            }
            else {
                Directory.CreateDirectory(backupDir);
                File.Copy(file, path, false);
            }
            return true;
        }
        return false;
    }

    public static string GetDefaultBackupDirectory(string directory) {
        return Path.Combine(directory, BackupDirectoryName);
    }

    public static string GetDefaultBackupDirectoryForFile(string file) {
        return Path.Join(Path.GetDirectoryName(file.AsSpan()),
            GetDefaultBackupDirectory(String.Empty));
    }
}

class FileBackupIndex {
    public const string IndexFileName = "backup-index.csv";
    public readonly string WorkingDir, BackupDir, IndexFile;
    public IEnumerable<Record> Records => _records.Values;

    readonly Dictionary<string, Record> _records;

    // public FileBackupIndex(string workingDir) {

    private FileBackupIndex(string workingDir, string backupDir, string indexFile) {
        WorkingDir = workingDir; BackupDir = backupDir; IndexFile = indexFile;
        _records = File.ReadAllLines(IndexFile, Encoding.UTF8)
            .Where(s => !(s.StartsWith('#') || string.IsNullOrWhiteSpace(s))).Select(Record.Parse)
            .ToDictionary(r => r.FileNameOrPattern, StringComparer.OrdinalIgnoreCase);
    }

    public void SaveIndex() {
        Directory.CreateDirectory(BackupDir);
        File.WriteAllLines(IndexFile,
            _records.Values.Select(r => r.ToString()), Encoding.UTF8);
    }

    public bool AddRecord(string file, int copiesLimit, int protectedHours) {
        string relativeFilePath = Path.GetRelativePath(WorkingDir, file);
        return _records.TryAdd(relativeFilePath,
            new Record(relativeFilePath, copiesLimit, protectedHours));
    }

    public bool RemoveRecord(string file) {
        string relativeFilePath = Path.GetRelativePath(WorkingDir, file);
        return _records.Remove(relativeFilePath);
    }

    /// <summary>Searches record by file relative path to directory,
    /// also check for template records matching</summary>
    public bool TryGetRecord(string relativeFilePath,
                [MaybeNullWhen(false)] out Record record) {
        if (!_records.TryGetValue(relativeFilePath, out record)) {
            record = _records.Values.FirstOrDefault(r =>
                FilePath.IsPattern(r.FileNameOrPattern) &&
                    FilePath.IsMatch(relativeFilePath, r.FileNameOrPattern));
        }
        return record is not null;
    }

    public bool BackupFile(string file, bool moveFile = false) {
        if (File.Exists(file)) {
            string relativeFilePath = Path.GetRelativePath(WorkingDir, file);
            if (TryGetRecord(relativeFilePath, out Record? record)) {
                if (FileBackup.Backup(file, moveFile, BackupDir)) {
                    FileBackup.Clean(file, record.CopiesLimit,
                        TimeSpan.FromHours(record.ProtectHours), BackupDir);
                    return true;
                }
            }
        }
        return false;
    }

    public bool RestoreFile(string file) {
        return FileBackup.Restore(file, BackupDir);
    }

    public static FileBackupIndex? FromFile(string filePath) {
        return FromDirectory(Path.GetDirectoryName(filePath)
            ?? throw new Exception("Can't get file directory"));
    }

    public static FileBackupIndex? FromDirectory(string workingDir) {
        Debug.Assert(Path.IsPathRooted(workingDir), "Backup directory path must be rooted");
        string backupDir = FileBackup.GetDefaultBackupDirectory(workingDir);
        string indexFile = GetIndexFilePath(backupDir);
        return File.Exists(indexFile)
            ? new FileBackupIndex(workingDir, backupDir, indexFile) : null;
    }

    public static string GetIndexFilePath(string backupDir) {
        return Path.Combine(backupDir, IndexFileName);
    }

    ///<summary>Create backup directory and index file if not exists, return error message or null</summary>
    public static string? InitBackupForDirectory(string workingDir) {
        Debug.Assert(Path.IsPathRooted(workingDir), "Backup directory path must be rooted");
        string backupDir = FileBackup.GetDefaultBackupDirectory(workingDir);
        string indexFile = GetIndexFilePath(backupDir);
        if (File.Exists(indexFile)) {
            return $"Index already exists for '{workingDir}'";
        }
        var content = $"""
            # backup index template; add your records using format below:
            # "file-name-or-pattern", "copies-limit", "protect-hours"
            """;
        Directory.CreateDirectory(backupDir);
        File.WriteAllLines(indexFile, content.Split("\r\n"), Encoding.UTF8);
        return null;
    }

    public class Record {
        static readonly Regex Parser = new(
            @"\A\""([\w\s-+$@%\(\)\\/,.:'*?]+)\"",\s*(\d+),\s*(\d+)\z",
            RegexOptions.Compiled);

        public readonly string FileNameOrPattern;
        public readonly int CopiesLimit, ProtectHours; 

        public Record(string fileName, int copiesLimit, int protectHours) {
            FileNameOrPattern = Path.GetFileName(fileName);
            CopiesLimit = copiesLimit; ProtectHours = protectHours;
        }

        public override string ToString() {
            return $"\"{FileNameOrPattern}\", {CopiesLimit}, {ProtectHours}";
        }

        public static Record Parse(string input) {
            var match = Parser.Match(input);
            return match.Success
                ? new (match.Result("$1"),
                    int.Parse(match.Result("$2")), int.Parse(match.Result("$3")))
                : throw new FormatException("Backup record incorrect format");
        }
    }
}
