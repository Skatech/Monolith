using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Skatech.IO;
using System.Diagnostics.CodeAnalysis;

namespace Skatech.Monolith.Utilities;

internal static class UtilityDoc {
    public const string Name = "doc";
    public const string Description = "Manage archived document files";
    static string  DataDirectory = "", TempDirectory = "";

    public static void Run(string[] args) {
        DataDirectory = Skatech.ComponentArchitecture.OperationGroup.ApplicationDataDirectory;
        Directory.CreateDirectory(TempDirectory = Path.Combine(DataDirectory, ".temp"));

        switch(args.FirstOrDefault()) {
            case null:
                ShowHelp();
                break;
            case "index":
                IndexMaintenance(args.Skip(1));
                break;
            case "list":
                ListRecords(args.Skip(1));
                break;
            case "sync":
                SyncRecords(args.Skip(1));
                break;
            case "edit":
                EditDocument(args.Skip(1));
                break;
            case "show":
                ShowMatchingLines(args.Skip(1));
                break;
            default:
                if (args[0].StartsWith('-')) {
                    Console.WriteLine(
                        $"Operation or record name expected. Invalid option: '{args[0]}'");
                }
                else if (args.Skip(1).All(a => a.StartsWith('-'))) {
                    EditDocument(args); // rec-name-or-part OPTIONS
                }
                else ShowMatchingLines(args); // rec-name/part MATCH [ENCODING] OPTIONS
                break;
        }
    }

    static void ShowHelp() {
        var archiveFormats = String.Join(", ", CreateStorageProviders().Keys);
        Console.WriteLine($"""
            Doc Utility, commands:
                index                        Index file maintenance operations
                list [<rec-name-or-part>]    List existing document records
                sync [<rec-name-or-part>]    Syncronize document files
                        -n, --dry-run          - perform no real operations
               [edit] <rec-name-or-part>     Open document in assigned editor
                        -n, --skip-sync        - skip syncronization
               [show] <rec-name-or-part>     Show text document matching fragments
                        <searching-text>       - searching text fragment
                        [<custom-encoding>]    - utf-8(default), ascii, windows-1251, etc
                        -n, --skip-sync        - skip syncronization check
            """);
    }

    static void IndexMaintenance(IEnumerable<string> args) {
        if (StartupParameters.Create()
                .AddKey("init", 'i').UseRef(out var optInit)
                .AddKey("edit", 'e').UseRef(out var optEdit)
                .AddKey("pack", 'p').UseRef(out var optPack)
                .TryProcess(args) is string error) {
            Console.WriteLine(error);
            return;
        }

        if (optInit.IsActive) {
            var indexPath = Index.InferFilePath(DataDirectory, optPack.IsActive);
            if (File.Exists(indexPath)) {
                Console.WriteLine($"Index already exists '{indexPath}'");
                return;
            }
            var sampleRecord = new Record("recordname", ".true-extension",
                    "absolute-or-relative-to-index-archive-path.adoc");
            var lines = $"""
                # doc-utility index file template; add your records using format below:
                # {sampleRecord}
                """.Split("\r\n");
            Console.Write($"Writing new index '{indexPath}'... ");
            TextConfig.WriteLines(lines, indexPath, Index.IsArchiveFilePath(indexPath));
            Console.WriteLine("OK");
        }
        else if (optPack.IsActive) {
            var indexPath = Index.InferFilePath(DataDirectory);
            if (Index.IsArchiveFilePath(indexPath)) {
                Console.WriteLine("Index file already packed");
                return;
            }
            else if (File.Exists(indexPath)) {
                Console.Write("Packing index... ");
                ADocFileFormat.Compress(indexPath,
                    Path.ChangeExtension(indexPath, Index.ArchiveExt));
                File.Delete(indexPath);
                Console.WriteLine("OK");
            }
            else Console.WriteLine("Index file not exists");
        }
        else if (optEdit.IsNotActive) {
            var indexPath = Index.InferFilePath(DataDirectory);
            string indexFileStatus = File.Exists(indexPath)
                ? Index.IsArchiveFilePath(indexPath)
                    ? "exists (packed)" : "exists"
                : "not found";
            Console.WriteLine($"""
                Index file {indexFileStatus}, command options:
                    -i, --init  - create new index if not exists
                    -e, --edit  - open index in assinged editor
                    -p, --pack  - pack existing or init index packed
                """);
        }

        if (optEdit.IsActive) {
            var indexPath = Index.InferFilePath(DataDirectory);
            if (File.Exists(indexPath)) {
                Console.Write("Awaiting editor...");
                Console.WriteLine(TextConfig.TryOpenWithAppSync(indexPath, Index.DefaultExt)
                    ? " OK (updated)"
                    : " OK (not changed)");
            }
            else Console.WriteLine("Index file not exists");
        }
    }

    static void ListRecords(IEnumerable<string> args) {
        if (!TryLoadIndexAnnotated(out Index? index, out string indexPath)) {
            return;
        }
        if (StartupParameters.Create()
                .AddArg("rec-name-or-part").StrongOrdered(1).UseRef(out var argFilter)
                .TryProcess(args) is string error) {
            Console.WriteLine(error);
            return;
        }

        var records = argFilter.IsActive
            ? index.Records.Where(r => r.Name.Contains(argFilter.Value))
            : index.Records;
        var names = String.Join("\r\n",
            records.Select(r => $"{r.Name,-11} {r.TrueExtension, -7} {r.ArchivePath}"));
        Console.WriteLine(names.Any()
            ? names : "No records found");
    }
    
    static void SyncRecords(IEnumerable<string> args) {
        if (!TryLoadIndexAnnotated(out Index? index, out string indexPath)) {
            return;
        }
        if (StartupParameters.Create()
                .AddArg("rec-name-or-part").StrongOrdered(1).UseRef(out var argFilter)
                .AddKey("dry-run", 'n').UseRef(out var optDry)
                .TryProcess(args) is string error) {
            Console.WriteLine(error);
            return;
        }

        var syncIndexCache = new Dictionary<string, FileSyncIndex?>(StringComparer.OrdinalIgnoreCase);
        int reccount = 0, recmatch = 0;
        foreach (var record in index.Records) {
            if (argFilter.ValueOrNull is null || record.Name.StartsWith(argFilter.Value)) {
                Console.Write($"{record.Name,-11} ");
                if (FileSyncIndex.EnumerateUpstreamFilesForFile(record.ArchivePath, null, syncIndexCache)
                                is IEnumerable<string> upstreamFiles) {
                    int synccount = 0;
                    foreach (var upstreamFile in upstreamFiles) {
                        FileSync.SyncAnnotated(record.ArchivePath, upstreamFile,
                            optDry.IsNotActive, optDry.IsNotActive);
                        synccount++;
                    }
                    if (synccount < 1) {
                        Console.WriteLine("-- (no sync records)");
                    }
                }
                else Console.WriteLine("-- (no sync index)");
                recmatch++;
            }
            reccount++;
        }

        if (reccount < 1) {
            Console.WriteLine("No records found");
        }
        else if (recmatch < 1) {
            Console.WriteLine("No records matching");
        }
    }
    
    static void EditDocument(IEnumerable<string> args) {
        if (!TryLoadIndexAnnotated(out Index? index, out string indexPath)) {
            return;
        }
        if (StartupParameters.Create()
                .AddArg("rec-name-or-part").StrongOrdered(1).Required().UseRef(out var argRecord)
                .AddKey("skip-sync", 'n').UseRef(out var optNoSync)
                .TryProcess(args) is string error) {
            Console.WriteLine(error);
            return;
        }
        var record = index.FindRecord(argRecord.Value, true);
        if (record is null) {
            Console.WriteLine($"No record found: \"{argRecord.Value}\"");
            return;
        }

        var archiveFormat = Path.GetExtension(record.ArchivePath).TrimStart('.');
        var storageProvider = CreateStorageProviders().GetValueOrDefault(archiveFormat);
        if (storageProvider is null) {
            Console.WriteLine($"Unknown archive format: \"{archiveFormat}\"");
            return;
        }
        
        var documentFile = Path.Combine(TempDirectory, record.GetExtractedFileName());
        if (File.Exists(documentFile)) {
            Console.WriteLine("Extracted document exists. Clean first!");
            return;
        }
        var syncIndexCache = new Dictionary<string, FileSyncIndex?>(StringComparer.OrdinalIgnoreCase);
        if (optNoSync.IsNotActive) {
            if (CheckArchiveSyncronized(record.ArchivePath, syncIndexCache) is string message) {
                Console.WriteLine($"{message} - sync operation required");
                return;
            }
        }

        Console.Write($"Extracting \"{record.Name}\"...");
        storageProvider.Restore(record.ArchivePath, documentFile);
        var createTime = File.GetLastWriteTime(documentFile);
        
        Console.Write(" Editing...");
        if (Process.Start(
            new ProcessStartInfo(documentFile) { UseShellExecute = true }) is Process editor) {
            editor.WaitForExit();

            if (File.Exists(documentFile)) {
                if (createTime < File.GetLastWriteTime(documentFile)) {
                    Console.Write(" Archiving...");
                    string tempFile = Path.ChangeExtension(documentFile, archiveFormat);
                    storageProvider.Archive(documentFile, tempFile);
                    try {
                        FileBackupIndex.FromFile(record.ArchivePath)
                            ?.BackupFile(record.ArchivePath, true);
                    }
                    catch (Exception ex) {
                        using var cc = ConsoleColors.FromForeground(ConsoleColor.Yellow);
                        Console.Write($" Backup...({ex.Message})");
                    }
                    File.Move(tempFile, record.ArchivePath, true);

                    if (optNoSync.IsNotActive &&
                            FileSyncIndex.EnumerateUpstreamFilesForFile(record.ArchivePath,
                                null, syncIndexCache) is IEnumerable<string> upstreamFiles) {
                        Console.Write(" Syncronizing...");
                        foreach (var upstreamFile in upstreamFiles) {
                            string? message;
                            if (FileSync.IsDriveAvailable(upstreamFile)) {
                                if (FileSync.Look(record.ArchivePath, upstreamFile) > 0) { // upstream outdated
                                    message = FileSync.Push(record.ArchivePath, upstreamFile);
                                }
                                else message = "Working file not properly updated!"; // something went wrong
                            }
                            else message = "Upstream drive offline";
                            if (message is not null) {
                                using var cc = ConsoleColors.FromForeground(ConsoleColor.Yellow);
                                Console.Write($" ({message})");
                            }
                        }
                    }
                }
            }
        }
        else {
            using var cc = ConsoleColors.FromForeground(ConsoleColor.Yellow);
            Console.Write(" FAILED (Open editor)");
        }

        if (File.Exists(documentFile)) {
            Console.Write(" Cleaning...");
            File.Delete(documentFile);
        }
        Console.WriteLine(" OK");
    }
    
    static void ShowMatchingLines(IEnumerable<string> args) {
        if (!TryLoadIndexAnnotated(out Index? index, out string indexPath)) {
            return;
        }
        if (StartupParameters.Create()
                .AddArg("rec-name-or-part").StrongOrdered(1).Required().UseRef(out var argRecordName)
                .AddArg("search-text").StrongOrdered(2).Required().UseRef(out var argSearchText)
                .AddArg("custom-encoding").StrongOrdered(3).UseRef(out var argCustomEncoding)
                .AddKey("skip-sync", 'n').UseRef(out var optNoSync)
                .TryProcess(args) is string error) {
            Console.WriteLine(error);
            return;
        }

        var record = index.FindRecord(argRecordName.Value, true);
        if (record is null) {
            Console.WriteLine($"No record found: \"{argRecordName.Value}\"");
            return;
        }

        if (optNoSync.IsNotActive) {
            var syncIndexCache = new Dictionary<string, FileSyncIndex?>(StringComparer.OrdinalIgnoreCase);
            if (CheckArchiveSyncronized(record.ArchivePath, syncIndexCache) is string message) {
                Console.WriteLine($"{message} - sync operation required");
                return;
            }
        }

        if (record.TrueExtension != ".txt") {
            Console.WriteLine("Matching fragments mode for text files only");
            return;
        }

        var archiveFormat = Path.GetExtension(record.ArchivePath).TrimStart('.');
        var storageProvider = CreateStorageProviders().GetValueOrDefault(archiveFormat);
        if (storageProvider is null) {
            Console.WriteLine($"Unknown archive format: \"{archiveFormat}\"");
            return;
        }

        // System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        // Encoding.GetEncodings().Any(e => args[2].Equals(e.Name))
        var encoding = Encoding.Default;
        if (argCustomEncoding.ValueOrNull is not null) {
            try {
                encoding = Encoding.GetEncoding(argCustomEncoding.ValueOrNull);
            }
            catch(System.ArgumentException) {
                Console.WriteLine($"Not supported encoding name: {argCustomEncoding}");
                return;
            }
        }

        int lines = 0, matches = 0;
        foreach (var line in storageProvider.RestoreAsLines(record.ArchivePath, encoding)) {
            if (line.Contains(argSearchText.Value, StringComparison.InvariantCultureIgnoreCase)) {
                Console.WriteLine(line);
                matches++;
            }
            lines++;
        }
        if (matches < 1) {
            Console.WriteLine($"No matcing lines found in \"{record.Name}\"");
        }
    }

    static string? CheckArchiveSyncronized(string archiveFile,
                    Dictionary<string, FileSyncIndex?> syncIndexCache) {
        if (FileSyncIndex.EnumerateUpstreamFilesForFile(archiveFile,
                null, syncIndexCache) is IEnumerable<string> upstreamFiles) {
            foreach (var upstreamFile in upstreamFiles) {
                if (FileSync.IsDriveAvailable(upstreamFile)) {
                    long comp = FileSync.Look(archiveFile, upstreamFile);
                    if (comp < 0) return "Working file outdated";
                    if (comp > 0) return "Upstream file outdated";
                }
            }
        }
        return null;
    }

    static bool TryLoadIndexAnnotated(
                [NotNullWhen(true)] out Index? index, out string indexPath) {
        index = Index.TryLoadFromDirectory(DataDirectory, out indexPath);
        if (index is null) {
            Console.WriteLine("Index file not exists");
            return false;
        }
        if (index.Records.Count() < 1) {
            Console.WriteLine("Index file has no records");
            return false;
        }
        return true;
    }

    static Dictionary<string, IDocStorageProvider> CreateStorageProviders() {
        return new IDocStorageProvider[]
             { new ADocStorageProvider(), new SDocStorageProvider() }
                .ToDictionary(e => e.Name);
    }

    class Index {
        public const string FileNameNoExt = "doc-index", DefaultExt = ".csv", ArchiveExt = ".acsv";
        private readonly List<Record> _records;
        public IEnumerable<Record> Records => _records;

        public Index(string rootDir, IEnumerable<Record>? records = null) {
            foreach (var record in _records = records?.ToList() ?? new()) {
                record.AcceptIndexRoot(rootDir);
            }
        }

        public void Save(string filePath) {
            TextConfig.WriteLines(Records.Select(r => r.ToString()),
                filePath, IsArchiveFilePath(filePath));
        }

        public Record? FindRecord(string clue, bool partialSearch) {
            var record = Records.FirstOrDefault(
                r => r.Name.Equals(clue, StringComparison.OrdinalIgnoreCase));
            if (record is null && partialSearch) {
                record = Records.FirstOrDefault(
                    r => r.Name.StartsWith(clue, StringComparison.OrdinalIgnoreCase));
            }
            return record;
        }

        ///<summary>Return index when file exists, otherwise null</summary>
        public static Index? TryLoadFromDirectory(string rootDir, out string indexPath) {
            indexPath = InferFilePath(rootDir);
            return TextConfig.TryReadLines(indexPath, IsArchiveFilePath(indexPath))
                ?.TrimSpacesAndComments()?.Select(Record.Parse)
                    ?.To(r => new Index(rootDir, r));
        }

        ///<summary>Return inferred index file path, archived or normal, default or custom</summary>
        public static string InferFilePath(string directoryOrFilePathMaybeNoExt, bool archiveAtLast = false) {
            return TextConfig.InferConfigFilePath(
                directoryOrFilePathMaybeNoExt, FileNameNoExt, DefaultExt, ArchiveExt, archiveAtLast);
        }

        ///<summary>Return true when path to archived index file, throw exception on unsupported file type</summary>
        public static bool IsArchiveFilePath(string filePath) {
            return filePath.EndsWith(ArchiveExt, StringComparison.OrdinalIgnoreCase)
                ? true : filePath.EndsWith(DefaultExt, StringComparison.OrdinalIgnoreCase)
                    ? false : throw new Exception(
                        $"Unsupported index file extension '{Path.GetExtension(filePath)}'");
        }
    }
    
    public class Record {
        static readonly Regex Parser = new(
            @"\A\""([_\-\w]+)\"",\s*\""([\.\w]+)\"",\s*\""([\w\s-+$@%\(\)\\/.:']+)\""(?:;\s*.*)?\z",
            RegexOptions.Compiled);
        string _archivePath;
        public string Name { get; }
        public string TrueExtension { get; }
        public string ArchivePath { get; private set; }

        public Record(string name, string trueExtension, string archivePath) {
            Name = name; TrueExtension = trueExtension;
            ArchivePath = _archivePath = archivePath;
        }

        public void AcceptIndexRoot(string indexRootDirectory) {
            ArchivePath = Path.IsPathRooted(ArchivePath)
                ? ArchivePath : Path.Combine(indexRootDirectory, ArchivePath);
        }

        public string GetExtractedFileName() {
            return string.Concat(Path.GetFileNameWithoutExtension(
                ArchivePath.AsSpan()), TrueExtension);
        }

        public override string ToString() {
            return $"\"{Name}\", \"{TrueExtension}\", \"{_archivePath}\"";
        }

        public static Record Parse(string input) {
            var match = Parser.Match(input);
            return match.Success
                ? new (match.Result("$1"), match.Result("$2"), match.Result("$3"))
                : throw new FormatException("Index record has incorrect format.");
        }
    }

    interface IDocStorageProvider {
        string Name { get; }
        void DisposeSession();
        void Archive(string sourceFile, string archiveFile);
        void Restore(string archiveFile, string outputFile);
        Stream RestoreAsStream(string archiveFile);
        IEnumerable<string> RestoreAsLines(string archiveFile, Encoding encoding);
    }

    class ADocStorageProvider : IDocStorageProvider {
        public string Name => ADocFileFormat.DefaultFileExtension;

        public void DisposeSession() {}
 
        public void Archive(string sourceFile, string archiveFile) {
            ADocFileFormat.Compress(sourceFile, archiveFile);
        }

        public void Restore(string archiveFile, string outputFile) {
            ADocFileFormat.Decompress(archiveFile, outputFile);
        }

        public Stream RestoreAsStream(string archiveFile) {
            return ADocFileFormat.CreateDecompressStream(archiveFile);
        }

        public IEnumerable<string> RestoreAsLines(string archiveFile, Encoding encoding) {
            return ADocFileFormat.DecompressLines(archiveFile, encoding);
        }
    }

    class SDocStorageProvider : IDocStorageProvider {
        SecuritySession _session = new();
        public string Name => SDocFileFormat.DefaultFileExtension;

        public void DisposeSession() {
            _session.Reset();
        }
        
        public void Archive(string sourceFile, string archiveFile) {
            SDocFileFormat.Encrypt(sourceFile, archiveFile, _session.GetPassword());
        }

        public void Restore(string archiveFile, string outputFile) {
            SDocFileFormat.Decrypt(archiveFile, outputFile, _session.GetPassword());
        }

        public Stream RestoreAsStream(string archiveFile) {
            return SDocFileFormat.CreateDecryptStream(archiveFile, _session.GetPassword());
        }

        public IEnumerable<string> RestoreAsLines(string archiveFile, Encoding encoding) {
            return SDocFileFormat.DecryptLines(archiveFile, _session.GetPassword(), encoding);
        }
   }
}