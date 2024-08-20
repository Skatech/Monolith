using System;
using System.IO;
using System.Text;
using Skatech.IO;

namespace Skatech.Monolith.Utilities;

internal static class UtilityFlake {
    public const string Name = "flake";
    public const string Description = "Obfuscate file names using Base64 format";
    public const string ObfuscatedFileExtension = ".flake", IgnoreListFileName = "flake-ignore.lst";

    static void PrintHelp() {
        Console.WriteLine($"""
            Flake - file name obfuscation utility
                Usage: <directory-with-optional-file-pattern> OPTIONS
                    -d, --deep          - search in subdirectories too
                    -u, --unobfuscate   - search files to unobfuscate
                    -o, --obfuscate     - search files to obfuscate
                    -p, --perfom        - perform operation on files
                        --ignore        - open directory ignore list in editor
                        --ignore-global - open global ignore list in editor
            """);
    }

    public static void Run(string[] args) {
        if (args.Length < 1) {
            PrintHelp(); return;
        }
        if (StartupParameters.Create()
                .AddArg("directory-with-optional-file-pattern")
                    .StrongOrdered(1).Required().UseRef(out var argDirectory)
                .AddKey("obfuscate", 'o').UseRef(out var optFlake)
                .AddKey("unobfuscate", 'u').UseRef(out var optUnflake)
                .AddKey("deep", 'd').UseRef(out var optDeep)
                .AddKey("perform", 'p').UseRef(out var optPerform)
                .AddKey("ignore").UseRef(out var optIgnoreList)
                    .MismatchedWith(optFlake, optUnflake, optDeep, optPerform)
                .AddKey("ignore-global").UseRef(out var optIgnoreListGlobal)
                    .MismatchedWith(optFlake, optUnflake, optDeep, optPerform, optIgnoreList)
                .TryProcess(args) is string error) {
            Console.WriteLine(error);
            return;
        }

        var filePattern = FilePath.SplitFile(Path.GetFullPath(
            Path.TrimEndingDirectorySeparator(argDirectory.Value)), out string workingDir);

        if (optIgnoreList.IsActive || optIgnoreListGlobal.IsActive) {
            CreateAndEditIgnoreListAnnotated(optIgnoreListGlobal.IsActive ? null : workingDir);
            return;
        }

        var ignoreList = LoadCombinedIgnoreList(workingDir);

        int filecount = 0, errorcount = 0;
        foreach(var file in Directory.EnumerateFiles(workingDir, "*",
                optDeep.IsActive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly)) {            
            string filename = FilePath.SplitFile(file, out string directory)!;
            string? destname = null;
            bool unflaking;
            if (ignoreList.ContainsTemplatesMatching(filename)) {
                continue;
            }
            if (unflaking = FilePath.IsExtensionEqual(file, ObfuscatedFileExtension)) {
                string tempname = UnobfuscateFileName(Path.GetFileNameWithoutExtension(file));
                if (optUnflake.IsActive && FilePath.IsMatch(tempname, filePattern)) {
                    destname = tempname;
                }
            }
            else if (optFlake.IsActive && FilePath.IsMatch(filename, filePattern)) {
                destname = ObfuscateFileName(Path.GetFileName(file)) + ObfuscatedFileExtension;
            }

            if (destname is not null) {
                Console.Write($"{filename,-27} {destname,-27}");
                string destfile = Path.Combine(directory, destname);
                if (File.Exists(destfile)) {
                    using var cc = ConsoleColors.FromForeground(ConsoleColor.Red);
                    Console.Write(" - Target file exists");
                    errorcount++;
                }
                else if (optPerform.IsActive) {
                    Console.Write(" - Processing... ");
                    File.Move(file, destfile);
                    Console.Write("OK");
                }
                Console.WriteLine();
                filecount++;
            }
        }

        if (filecount < 1) {
            Console.WriteLine("No files found");
        }
        if (errorcount > 0) {
            using var cc = ConsoleColors.FromForeground(ConsoleColor.Red);
            Console.WriteLine($"Finished with {errorcount} error(s)");
        }
    }

    ///<summary>Open ignore list in editor, create when not exists</summary>
    static void CreateAndEditIgnoreListAnnotated(string? directoryOrNullForGlobal = null) {
        Console.Write(CreateIgnoreList(out string filePath, directoryOrNullForGlobal)
            ? "Creating file... Awaiting editor... " : "Awaiting editor... ");
        Console.WriteLine(FilePath.TryOpenWithAppSync(filePath)
            ? "OK" : "OK (unchanged)");
    }

    ///<summary>Load records from local and global ignore lits, creates global ignore list when not exists</summary>
    static FileList LoadCombinedIgnoreList(string workingDir) {
        CreateIgnoreList(out string globalFileListPath);
        return FileList.FromFiles(globalFileListPath,
            FileList.GetPath(IgnoreListFileName, workingDir));
    }

    static bool CreateIgnoreList(out string filePath, string? directoryOrNullForGlobal = null) {
        if (File.Exists(filePath = FileList.GetPath(IgnoreListFileName, directoryOrNullForGlobal)))
            return false;
        string commentText = "file ignore list for flake utility, add lines with file names or patterns";
        return directoryOrNullForGlobal is null
            ? FileList.CreateFile(filePath, "global " + commentText,
                IgnoreListFileName, FileSyncIndex.FileNameNoExt + FileSyncIndex.DefaultExt,
                FileSyncIndex.FileNameNoExt + FileSyncIndex.ArchiveExt)
            : FileList.CreateFile(filePath, commentText);
    }

    static string ObfuscateFileName(string fileName) {
        Span<byte> buffer = stackalloc byte[64];
        int length = Encoding.UTF8.GetBytes(fileName, buffer);
        return Convert.ToBase64String(buffer.Slice(0, length));
    }

    static string UnobfuscateFileName(string fileName) {
        Span<byte> buffer = stackalloc byte[64];
        return (Convert.TryFromBase64String(fileName, buffer, out int length))
            ? Encoding.UTF8.GetString(buffer.Slice(0, length))
            : throw new Exception($"Invalid flake file name: '{fileName}'");
    }
}
