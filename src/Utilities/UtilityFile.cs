using System;
using System.IO;
using System.Linq;
using Skatech.IO;

namespace Skatech.Monolith.Utilities;

internal static class UtilityFile {
    public const string Name = "file";
    public const string Description = "Processing files using special formats";

    interface IFormatProvider {
        string Name { get; }
        void Execute(string[] args);
    }

    class ADocFormatProvider : IFormatProvider {
        public const string FilesExtension = ADocFileFormat.DefaultFileExtension;

        public string Name => FilesExtension;

        public void Execute(string[] args) {
            if (args.Length < 1) {
                Console.WriteLine($"""
                    ADoc file processor. Arguments: source-file [output-file]
                      - source-file - path to source document or archive file
                      - output-file - path to output file or direcotry, optional

                    """);
                PrintFileResolutionRules(FilesExtension);
            }
            else {
                string sourceFile = Path.GetFullPath(args[0]);
                if (!File.Exists(sourceFile)) {
                    Console.WriteLine($"Source file not found: \"{sourceFile}\"");
                    return;
                }

                string outputFile = PathX.DeduceOutputPathAndFileName(sourceFile,
                    args.ElementAtOrDefault(1), FilesExtension);

                if (IsArchiveFile(sourceFile)) {
                    if (IsArchiveFile(outputFile)) {
                        Console.WriteLine($"Input or output file must be normal type (not .{FilesExtension})");
                        return;
                    }
                    Console.Write($"Restoring: \"{sourceFile}\" >> \"{outputFile}\" ... ");
                    ADocFileFormat.Decompress(sourceFile, outputFile);
                    Console.WriteLine("OK");
                }
                else if (IsArchiveFile(outputFile)) {
                    Console.Write($"Archiving: \"{sourceFile}\" >> \"{outputFile}\" ... ");
                    ADocFileFormat.Compress(sourceFile, outputFile);
                    Console.WriteLine("OK");
                }
                else {
                    Console.WriteLine($"Input or output file must be archive type (.{FilesExtension})");
                }
            }
        }

        static bool IsArchiveFile(string path) {
            return PathX.HasExtensionEqualTo(path, FilesExtension);
        }
    }

    class SDocFormatProvider : IFormatProvider {
        public const string FilesExtension = SDocFileFormat.DefaultFileExtension;

        public string Name => FilesExtension;

        public void Execute(string[] args) {
            if (args.Length < 1) {
                Console.WriteLine($"""
                    SDoc file processor. Arguments: source-file [output-file]
                      - source-file - path to source document or archive file
                      - output-file - path to output file or direcotry, optional

                    """);
                PrintFileResolutionRules(FilesExtension);
            }
            else {
                string sourceFile = Path.GetFullPath(args[0]);
                if (!File.Exists(sourceFile)) {
                    Console.WriteLine($"Source file not found: \"{sourceFile}\"");
                    return;
                }

                string outputFile = PathX.DeduceOutputPathAndFileName(sourceFile,
                    args.ElementAtOrDefault(1), FilesExtension);


                var session = new SecuritySession();
                if (IsArchiveFile(sourceFile)) {
                    if (IsArchiveFile(outputFile)) {
                        Console.WriteLine($"Input or output file must be normal type (not .{FilesExtension})");
                        return;
                    }
                    Console.Write($"Restoring: \"{sourceFile}\" >> \"{outputFile}\" ... ");
                    SDocFileFormat.Decrypt(sourceFile, outputFile, session.GetPassword());
                    Console.WriteLine("OK");
                }
                else if (IsArchiveFile(outputFile)) {
                    Console.Write($"Archiving: \"{sourceFile}\" >> \"{outputFile}\" ... ");
                    SDocFileFormat.Encrypt(sourceFile, outputFile, session.GetPassword());
                    Console.WriteLine("OK");
                }
                else {
                    Console.WriteLine($"Input or output file must be archive type (.{FilesExtension})");
                }
            }
        }

        static bool IsArchiveFile(string path) {
            return PathX.HasExtensionEqualTo(path, FilesExtension);
        }
    }

    public static void Run(string[] args) {
        string datadir = Skatech.ComponentArchitecture.OperationGroup.ApplicationDataDirectory;
        var providers = (new IFormatProvider[] {
            new ADocFormatProvider(),
            new SDocFormatProvider() }).ToDictionary(e => e.Name);
        if (args.Length < 1) {
            Console.WriteLine($"File processing utility, operations: {String.Join(", ", providers.Keys)}");
        }
        else if (providers.TryGetValue(args[0], out IFormatProvider? provider)) {
            provider.Execute(args[1..]);
        }
        else Console.WriteLine($"Invalid operation name: \"{args[0]}\"");
    }

    static void PrintFileResolutionRules(string filesExtension) {
        Console.WriteLine($"""
          output-file directory can be:
            - specified explicitly absolute
            - specified with dot-prefix to use current directory
            - specified with no prefix relative to use source-file directory
            - separator-postfixed to infer file name from source-file

            output-file name can be:
            - specified explicitly
            - specified using * wildcards for file name and extension take from source
            - inferred from source file by adding extra '.{filesExtension}' extension
            - inferred from source archive by removing extra extension
            - inferred from source archive by replacing extension to '.un{filesExtension}'
        """);
    }
}

static class PathX {
    // Can also add extra extensions
    public static string AddExtension(string path, string extension) {
        return extension.StartsWith('.') ? path + extension : path + '.' + extension;
    }

    public static string ChangeFileName(string path, string newName) {
        return Path.Combine(Path.GetDirectoryName(path) ?? "", newName);
    }

    public static bool HasExtensionEqualTo(string path, string extension) {
        return Path.HasExtension(path)
            && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            && (extension.StartsWith('.') ||
                path[path.Length - extension.Length - 1].Equals('.'));
    }

    public static bool IsPathCurrentDirectoryRelative(string path) {
        return path.StartsWith(@".\") || path.StartsWith(@"..\");
    }

    /* Deduces output file path and name by input file and special files extension using rules:

        output-file directory can be:
          - specified explicitly absolute
          - specified with dot-prefix to use current directory
          - specified with no prefix relative to use source-file directory
          - separator-postfixed to infer file name from source-file

        output-file name can be:
          - specified explicitly
          - specified using * wildcards for file name and extension take from source
          - inferred from source file by adding extra '.{EXT}' extension
          - inferred from source archive by removing extra extension
          - inferred from source archive by replacing extension to '.un{EXT}' */
    public static string DeduceOutputPathAndFileName(string sourceFile, string? outputFile, string formatExtension) {
        if (String.IsNullOrEmpty(outputFile)) { // no output file provided
            outputFile = Path.GetDirectoryName(sourceFile) + @"\"; // because Path.Combine failed
        }
        else if (PathX.IsPathCurrentDirectoryRelative(outputFile) || Path.IsPathRooted(outputFile)) {
            outputFile = Path.GetFullPath(outputFile);
        }
        else { // not rooted uri, count as relative to source file directory
            outputFile = Path.Combine(Path.GetDirectoryName(sourceFile) ?? "", outputFile);
        }
        // no filename provided
        if (Path.EndsInDirectorySeparator(outputFile) || Directory.Exists(outputFile)) {
            if (PathX.HasExtensionEqualTo(sourceFile, formatExtension)) { // for archives
                string sourceName = Path.GetFileNameWithoutExtension(sourceFile);
                outputFile = Path.Combine(outputFile, Path.HasExtension(sourceName)
                    ? sourceName
                    : Path.ChangeExtension(sourceName, "un" + formatExtension.TrimStart('.')));
            }
            else outputFile = Path.Combine(outputFile,  // for other files
                PathX.AddExtension(Path.GetFileName(sourceFile), formatExtension));
        }
        // if wildcards in file name or extension
        else if (Path.GetFileName(outputFile.AsSpan()).Contains('*')) {
            string newName = Path.GetFileNameWithoutExtension(outputFile);
            if (newName.Contains('*')) {
                newName = newName.Replace("*", Path.GetFileNameWithoutExtension(sourceFile));
            }
            string newExt = Path.GetExtension(outputFile);
            if (newExt.Contains('*')) {
                newExt = newExt.Replace("*", Path.GetExtension(sourceFile).TrimStart('.'));
            }
            outputFile = PathX.ChangeFileName(outputFile, newName + newExt);
        }
        return outputFile;
    }
}
