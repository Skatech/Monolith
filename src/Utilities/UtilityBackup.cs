using System;
using System.IO;
using System.Linq;
using Skatech.IO;

namespace Skatech.Monolith.Utilities;

internal static class UtilityBackup {
    public const string Name = "backup";
    public const string Description = "Batch files backup utility";

    static void PrintHelp() {
        Console.WriteLine($"""
            Backup - batch files backup utility
                Usage: <working-directory> OPTIONS
                    -e, --edit    - open index file in text editor
                    -i, --init    - create index file if not exists
                Backup index file: {FileBackupIndex.IndexFileName}
            """);
    }

    //TODO: files by backuped, not only in workdir
    //TODO: required restore????
    static void PrintInfo(FileBackupIndex backupIndex) {
        foreach (var record in backupIndex.Records) {
            Console.WriteLine(String.Format("{0}  -->  files limit: {1}   protected time: {2}",
                Path.Combine(backupIndex.WorkingDir, record.FileNameOrPattern), record.CopiesLimit,
                TimeSpan.FromHours(record.ProtectHours)));

            var files = FilePath.IsPattern(record.FileNameOrPattern)
                ? Directory.EnumerateFiles(backupIndex.WorkingDir, record.FileNameOrPattern)
                : Enumerable.Repeat(record.FileNameOrPattern, 1);

            foreach (var file in files) {
                int count = 0, older = 0; long bytes = 0;
                foreach(var info in FileBackup.EnumerateBackupFiles(file, backupIndex.BackupDir)
                            .Select(s => new FileInfo(s))) {
                    if (DateTime.Now - info.LastWriteTime > TimeSpan.FromHours(record.ProtectHours))
                        older++;
                    bytes += info.Length;
                    count += 1;
                }
                
                var size = new FileSize(bytes);
                Console.Write($"    {Path.GetFileName(file),-19} {count, 3} files {size,10}");
                int clean = older - record.CopiesLimit;
                Console.WriteLine(clean < 1 ? "" : $"{clean, 10} excess"); // file(s) to clean
            }
        }
    }

    public static void Run(string[] args) {
        if (args.Length < 1) {
            PrintHelp(); return;
        }
        if (StartupParameters.Create()
                .AddArg("working-directory").StrongOrdered(1).Required().UseRef(out var argWorkDir)
                .AddKey("edit", 'e').StrongOrdered(2).UseRef(out var optEdit)
                .AddKey("init", 'i').StrongOrdered(2).UseRef(out var optInit)
                .TryProcess(args) is string error) {
            Console.WriteLine(error);
            return;
        }

        string workingDir = Path.GetFullPath(
            Path.TrimEndingDirectorySeparator(argWorkDir.Value));
        if (Directory.Exists(workingDir) is false) {
            Console.WriteLine($"Directory not exists '{workingDir}'");
            return;
        }
        
        if (optInit.IsActive) {
            Console.Write($"Creating index... ");
            string? message = FileBackupIndex.InitBackupForDirectory(workingDir);
            Console.WriteLine(message is null ? "OK" : message);
            return;
        }

        var backupIndex = FileBackupIndex.FromDirectory(workingDir);
        if (backupIndex is null) {
            Console.WriteLine($"Backup directory or index file not exists for '{workingDir}'");
            return;
        }

        if (optEdit.IsActive) {
            Console.Write($"Awaiting editor... ");
            Console.WriteLine(FilePath.TryOpenWithAppSync(backupIndex.IndexFile)
                ? " OK (updated)" : " OK (not changed)");
            return;
        }

        PrintInfo(backupIndex);
    }
}
