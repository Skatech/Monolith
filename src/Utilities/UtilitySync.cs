using System;
using System.IO;
using Skatech.IO;

namespace Skatech.Monolith.Utilities;

internal static class UtilitySync {
    public const string Name = "sync";
    public const string Description = "Syncronize files using batch configuration";

    static void PrintHelp() {
        string csvIndexFile = FileSyncIndex.FileNameNoExt + FileSyncIndex.DefaultExt;
        string arcIndexFile = FileSyncIndex.FileNameNoExt + FileSyncIndex.ArchiveExt;
        Console.WriteLine($"""
            Sync - batch files syncronization utility
                Usage: <directory-or-custom-index-file[.ext]> [<file-matching-pattern>] OPTIONS
                    -d, --deep          - include subdirectories (NOT TESTED)
                    -p, --push          - perform push operations
                    -u, --pull          - perform pull operations
                        --index-edit    - open index in assinged editor, create if not exists
                        --index-pack    - pack index file
                        --ignore        - open directory ignore list in editor
                        --ignore-global - open global ignore list in editor
                Default index file: {csvIndexFile} or {arcIndexFile}(adoc-packed)
            """);
    }

    public static void Run(string[] args) {
        if (args.Length < 1) {
            PrintHelp(); return;
        }
        if (StartupParameters.Create()
                .AddArg("directory-or-index-file").StrongOrdered(1).Required().UseRef(out var argIndex)
                .AddArg("file-matching-pattern").StrongOrdered(2).UseRef(out var argMatch)
                .AddKey("deep", 'd').UseRef(out var optDeep)
                .AddKey("push", 'p').UseRef(out var optPush)
                .AddKey("pull", 'u').UseRef(out var optPull)
                .AddKey("index-edit").StrongOrdered(2).UseRef(out var optEdit)
                .AddKey("index-pack").StrongOrdered(2).UseRef(out var optPack)
                .AddKey("ignore").StrongOrdered(2).UseRef(out var optIgnoreList)
                .AddKey("ignore-global").StrongOrdered(2).UseRef(out var optIgnoreListGlobal)
                .TryProcess(args) is string error) {
            Console.WriteLine(error);
            return;
        }

        if (optEdit.IsActive) {
            FileSyncIndex.CreateAndEditIndexFileAnnotated(Path.GetFullPath(argIndex.Value));
            return;
        }
        if (optPack.IsActive) {
            FileSyncIndex.PackIndexFileAnnotated(Path.GetFullPath(argIndex.Value));
            return;
        }

        if (optIgnoreList.IsActive || optIgnoreListGlobal.IsActive) {
            FileSyncIndex.CreateAndEditIgnoreListAnnotated(optIgnoreListGlobal.IsActive
                ? null : Path.GetDirectoryName(
                    FileSyncIndex.InferIndexFilePath(Path.GetFullPath(argIndex.Value))));
            return;
        }

        var syncIndex = FileSyncIndex.TryOpen(Path.GetFullPath(argIndex.Value));
        if (syncIndex is null) {
            Console.WriteLine($"Index file not found '{Path.GetFullPath(argIndex.Value)}'");
            return;
        }

        syncIndex.SyncAnnotated(optPush.IsActive,
            optPull.IsActive, optDeep.IsActive, argMatch.ValueOrNull);
    }
}
