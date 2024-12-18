using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Collections.ObjectModel;
using Skatech.IO;

namespace Skatech.Monolith.Utilities;

internal static class UtilityIndex {
    public const string Name = "index";
    public const string Description = "Uniform access to files in distributed directories";

    static readonly string[] DefaultExtensions = {
            ".avi", ".mpg", ".mp4", ".wmv", ".flv", ".mov", ".mpeg",
            ".vob", ".mkv", ".mp3", ".wav", ".flac", ".m4a", ".webm" };    
    static Dictionary<string, Comparison<FileData>> Comparsions
                = new(StringComparer.OrdinalIgnoreCase) {
        { "~na", (a, b) => Path.GetFileName(a.Path.AsSpan()).CompareTo(
            Path.GetFileName(a.Path.AsSpan()), StringComparison.OrdinalIgnoreCase) },
        { "~nd", (a, b) => Path.GetFileName(a.Path.AsSpan()).CompareTo(
            Path.GetFileName(a.Path.AsSpan()), StringComparison.OrdinalIgnoreCase) },
        { "~pa", (a, b) => String.Compare(a.Path, b.Path, true) },
        { "~pd", (a, b) => String.Compare(b.Path, a.Path, true) },
        { "~da", (a, b) => DateTime.Compare(a.LastWrite, b.LastWrite) },
        { "~dd", (a, b) => DateTime.Compare(b.LastWrite, a.LastWrite) } };
    static Comparison<FileData> Comparison = Comparsions.Values.First();
    
    static void PrintHelp() {
        Console.WriteLine($"""
            Index Utility, usage: <DIRECTORIES> <COMMANDS> [options]
                DIRECTORIES         - Directory list separated with | symbol
                COMMANDS            - Command list separated with | symbol
                -e, --extensions    - Custom extension list separated with | symbol
                -i, --interactive   - Enable interactive mode
            Commands:
                ~help               - Show this info
                ~info               - Show current status info
                ~recache            - Reload data, rebuild cache
                ~rewrite            - Rewrite cache file
                ~exit               - Exit program
                ~na                 - Order selection by name ascending (default)
                ~nd                 - Order selection by name descending
                ~pa                 - Order selection by path ascending
                ~pd                 - Order selection by path descending
                ~da                 - Order selection by date ascending
                ~dd                 - Order selection by date descending
                ~delete:INDEX       - Delete selected file with index
                ~rename:INDEX:NAME  - Rename selected file with index
                :HOURS              - Select files newer than hours specified
                :TOKEN              - Select files by token
                INDEX               - Open selected file with index
                NAME-PART           - Select files by matching part of name or path
                NAME-PATTERN        - Select files by matching file pattern
            Default extensions:
              {string.Join(", ", DefaultExtensions)}
            """);
    }

    static void LoadDataAnnotated(FileIndex index) {
        static void WaitTaskAnimated(Task task) {
            if (task.IsCompleted is false) {
                Console.Write('-');
                int frame = 0;
                while(task.Wait(100) is false) {
                    Console.Write("\b \b");
                    Console.Write(@"-\|/"[++frame % 4]);
                }
                Console.Write("\b \b");
            }
        }

        try {
            index.LoadCache();
        }
        catch {
            async Task loadDataAsync() {
                await Task.Yield();
                index.BuildCache();
            }
            async Task saveCacheAsync() {
                await Task.Yield();
                index.SaveCache();
            }

            var sw = Stopwatch.StartNew();
            Console.Write($"Building data... ");
            var task = loadDataAsync();
            WaitTaskAnimated(task);

            Console.Write($"Storing cache... ");
            WaitTaskAnimated(saveCacheAsync());
            sw.Stop();
            Console.WriteLine($"Finished in {sw.ElapsedMilliseconds/1000.0:F2} sec");
        }
    }

    ///<summary>Return span with length shorter or equal to specified length</summary>
    static ReadOnlySpan<char> Trim(this ReadOnlySpan<char> input, int length) {
        return length >= input.Length ? input : input.Slice(0, length);
    }

    static void PrintFilesData(List<FileData> selection) {
        const int NameWidth = 65, DirWidth = 24;
        int index = 0;
        foreach (var file in selection) {
            var fn = Path.GetFileNameWithoutExtension(file.Path.AsSpan()).Trim(NameWidth);
            var fd = Path.GetFileName(Path.GetDirectoryName(file.Path.AsSpan())).Trim(DirWidth);
            Console.WriteLine(File.Exists(file.Path)
                ? $"{index,3} - {fn,-NameWidth}  {fd,-DirWidth}  {FileSize.FromFile(file.Path)}"
                : $"{index,3} - {fn,-NameWidth}  {fd,-DirWidth}  --");
            index++;
        }
    }

    static int SelectFiles(FileIndex index, Func<FileData, bool> filter, List<FileData> selection) {
        selection.Clear();
        selection.AddRange(index.Records.Values.Where(filter));
        selection.Sort(Comparison);
        return selection.Count;
    }

        static bool ProcessCommand(string? command, FileIndex index, List<FileData> selection) {
        if (string.IsNullOrWhiteSpace(command)) {
            return true;
        }
        else if (command.Equals("~help", StringComparison.OrdinalIgnoreCase)) {
            PrintHelp();
        }
        else if (command.Equals("~info", StringComparison.OrdinalIgnoreCase)) {
            var dirs = index.Directories.ToDictionary(s => s, s => index.Values.Count(
                r => r.Path.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                    && FilePath.IsDirectorySeparatorChar(r.Path[s.Length])));
            Console.WriteLine("Directories:\r\n" +
                string.Join("\r\n", dirs.Select(r => $"    {r.Key} - {r.Value} file(s)")));

            var exts = index.Extensions.ToDictionary(s => s,
                s => index.Values.Count(r => r.Path.EndsWith(s)), StringComparer.OrdinalIgnoreCase);            
            Console.WriteLine("Extensions:\r\n    " + String.Join(", ", exts.OrderByDescending(p => p.Value)
                .Select(p => p.Value < 1 ? p.Key : $"{p.Key}: {p.Value}")));

            Console.WriteLine($"Total files: {index.Values.Count}");
        }
        else if (command.Equals("~recache", StringComparison.OrdinalIgnoreCase)) {
            index.WipeCache();
            LoadDataAnnotated(index);
        }
        else if (command.Equals("~rewrite", StringComparison.OrdinalIgnoreCase)) {
            index.WipeCache();
            index.SaveCache();
        }
        else if (command.Equals("~exit", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        else if (Comparsions.TryGetValue(command, out var comparsion)) {
            selection.Sort(Comparison = comparsion);
            PrintFilesData(selection);
        }
        else if (command.StartsWith("~rename", StringComparison.OrdinalIgnoreCase)) {
            var match = Regex.Match(command, @"\A~rename\s+(\d+)\s+(.+)\z", RegexOptions.IgnoreCase);
            if (match.Success && Int32.TryParse(match.Groups[1].ValueSpan, out int number)) {
                if (selection.ElementAtOrDefault(number) is FileData fd) {                    
                    var path = Path.GetFullPath(FilePath.EnsureHasRoot(FilePath.EnsureHasExtension(
                            match.Groups[2].Value, Path.GetExtension(fd.Path.AsSpan())),
                        Path.GetDirectoryName(fd.Path.AsSpan())));

                    Console.Write($"Renaming file {fd.Path}  to '{path}'... ");
                    File.Move(fd.Path, path);
                    index.Records.Remove(fd.Path);
                    index.Records[path] = FileData.FromPath(path);
                    Console.WriteLine("OK");
                }
                else Console.WriteLine($"Invalid file index: {number}");
            }
            else Console.WriteLine($"Invalid command format");
        }
        else if (command.StartsWith("~delete", StringComparison.OrdinalIgnoreCase)) {
            var match = Regex.Match(command, @"\A~delete\s+(all|\d+)\z", RegexOptions.IgnoreCase);
            if (match.Success) {
                void DeleteFileAnnotated(FileData fd) {
                    Console.Write($"Deleting file {fd.Path}... ");
                    File.Delete(fd.Path);
                    index.Records.Remove(fd.Path);
                    Console.WriteLine("OK");
                }
                if (Int32.TryParse(match.Groups[1].ValueSpan, out int number)) {
                    if (selection.ElementAtOrDefault(number) is FileData fd) {
                        DeleteFileAnnotated(fd);
                    }
                    else Console.WriteLine($"Invalid file index: {number}");
                }
                else if (selection.Count > 0) {                    
                    foreach (var fd in selection)
                        DeleteFileAnnotated(fd);
                }
                else Console.WriteLine($"No files selected to delete");
            }
            else Console.WriteLine($"Invalid command format");
        }        
        else if (command.All(Char.IsDigit) && Int32.TryParse(command, out int number)) { // open selected file by index
            if (number < selection.Count) {
                string filePath = selection[number].Path;
                if (File.Exists(filePath)) {
                    var process = new Process();
                    process.StartInfo = new ProcessStartInfo() {
                        UseShellExecute = true, FileName = filePath};
                    process.Start();
                }
                else Console.WriteLine($"File not exists: {filePath}");
            }
            else Console.WriteLine($"Invalid file index: {number}");
        }
        else if (command.StartsWith(':')) {
            if (command.Length > 1 && command.Skip(1).All(Char.IsLetter)) { // select files marked :n token
                var tokens = new [] { $"- {command = command.Substring(1)}.",
                    $"- {command} ", $"- {command}{Path.DirectorySeparatorChar}" };
                var filter = (FileData r) => tokens.Any(
                    t => r.Path.Contains(t, StringComparison.OrdinalIgnoreCase))/* && File.Exists(fp)*/;
                if (SelectFiles(index, filter, selection) > 0) {
                    PrintFilesData(selection);
                }
                else Console.WriteLine($"No files matching: {command}");
            }
            else if (command.Length > 1 && command.Skip(1).All(Char.IsDigit)) { // select files newer than :n hours
                var period = TimeSpan.FromHours(Int32.Parse(command.AsSpan(1)));
                var filter = (FileData r) => /* File.Exists(fp) &&*/ period >= DateTime.Now - r.LastWrite;                
                if (SelectFiles(index, filter, selection) > 0) {
                    PrintFilesData(selection);
                }
                else Console.WriteLine($"No new files in: {period.Hours} hour(s)");
            }
            else Console.WriteLine($"Invalid command: {command}");
        }
        else if (command.Length > 0) {
            var pattern = FilePath.IsPattern(command) ? command : $"*{command}*";
            var filter = (FileData r) => FilePath.IsMatch(r.Path, pattern)/* && File.Exists(fp)*/;
            if (SelectFiles(index, filter, selection) > 0) {
                PrintFilesData(selection);
            }
            else Console.WriteLine($"No files matching: {command}");
        }
        return true;
    }

    public static void Run(string[] args) {
        if (args.Length < 1) {
            PrintHelp();
            return;
        }
        if (StartupParameters.Create()
                .AddArg("DIRECTORIES").StrongOrdered(1).Required().UseRef(out var argDirectories)
                .AddArg("COMMANDS").StrongOrdered(2).UseRef(out var argCommands)
                .AddKey("extensions", 'e').ValueRequired().UseRef(out var optExtensions)
                .AddKey("interactive", 'i').UseRef(out var optInteractive)
                .TryProcess(args) is string error) {
            Console.WriteLine(error);
            return;
        }
        
        var directories = argDirectories.Value.Split('|');
        if (!directories.All(s => Path.IsPathRooted(s) && !Path.EndsInDirectorySeparator(s))) {
            Console.WriteLine("Some directories has invalid format");
            return;
        }
        var extensions = optExtensions.ValueOrNull?.Split('|') ?? DefaultExtensions;
        if (!extensions.All(s => s.Length > 1 && s.StartsWith('.'))) {
            Console.WriteLine("Some extensions has invalid format");
            return;
        }

        var commands = argCommands.ValueOrNull?.Split('|') ?? Array.Empty<string>();
        var index = new FileIndex(directories, extensions);
        var selection = new List<FileData>();

        LoadDataAnnotated(index);
        if (index.Records.Count < 1) {
            Console.WriteLine("No files found");
            return;
        }

        for(int icmd = 0;;) {
            string? command;
            if (icmd < commands.Length) {
                command = commands[icmd++];
            }
            else if(optInteractive.IsActive) {
                Console.Write("command or token or pattern or index: ");
                command = Console.ReadLine();
            }
            else break;

            if (!ProcessCommand(command, index, selection))
                break;
        }
    }

    record class FileData (string Path, DateTime LastWrite) {
        public static FileData FromPath(string path) => new (path, File.GetLastWriteTime(path));
    }

    class FileIndex {
        public ReadOnlyCollection<string> Directories { get; }
        public ReadOnlyCollection<string> Extensions { get; }
        public Dictionary<string, FileData> Records { get; private set; }
        readonly string _fpath;
    
        public  IReadOnlyCollection<FileData> Values => Records.Values;

        public FileIndex(IList<string> directories, IList<string> extensions, string? cacheDir = null) {
            Directories = new (directories);
            Extensions = new (extensions);
            Records = new (StringComparer.OrdinalIgnoreCase);

            var info = Directory.CreateDirectory(
                Path.Combine(cacheDir ?? Path.GetTempPath(), ".index-cache"));
            var hash = Directories.Concat(Extensions).Aggregate(17,
                    (a, s) => a * 31 + UtilityHP.GetHashCodeLegacyX86(s.ToLower()));
            _fpath = Path.Combine(info.FullName, $"{hash:X8}.ica");
        }

        public void BuildCache() {
            Records = Directories.SelectMany(r => Directory.EnumerateFiles(r, "*", SearchOption.AllDirectories)
                    .Where(f => Extensions.Any(x => FilePath.IsExtensionEqual(f, x))))
                .ToDictionary(f => f, f => FileData.FromPath(f), StringComparer.OrdinalIgnoreCase);
        }

        public void LoadCache() {
            Records = ADocFileFormat.DecompressLines(_fpath, Encoding.UTF8).Chunk(2).ToDictionary(
                c => c[0], c => new FileData(c[0], DateTime.FromFileTime(long.Parse(c[1]))),
                StringComparer.OrdinalIgnoreCase);
        }

        public void SaveCache() {
            ADocFileFormat.CompressLines(_fpath, Records.Values.SelectMany(
                r => new[] { r.Path, r.LastWrite.ToFileTime().ToString() }), Encoding.UTF8);
        }
        
        public void WipeCache() {
            File.Delete(_fpath);
        }
    }
}
