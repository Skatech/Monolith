using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Skatech.IO;

static class TextConfig {
    ///<summary>Return config path default or archive from directory or file,
    /// with or without extension, provided by arguments</summary>
    public static string InferConfigFilePath(string directoryOrFilePathMaybeNoExt,
                string defaultFileNameNoExt, string defaultExt, string archiveExt, bool archiveAtLast = false) {
        if (Directory.Exists(directoryOrFilePathMaybeNoExt)) {
            string arc, def = Path.Combine(directoryOrFilePathMaybeNoExt, defaultFileNameNoExt + defaultExt);
            return File.Exists(def) ? def
                : File.Exists(arc = Path.Combine(
                    directoryOrFilePathMaybeNoExt, defaultFileNameNoExt + archiveExt)) ? arc
                : archiveAtLast ? arc : def;
        }
        else {
            string arc, def = directoryOrFilePathMaybeNoExt;
            return FilePath.IsExtensionEqual(def, defaultExt) ? def
                    : FilePath.IsExtensionEqual(def, archiveExt) ? def
                : File.Exists(def = directoryOrFilePathMaybeNoExt + defaultExt) ? def
                    : File.Exists(arc = directoryOrFilePathMaybeNoExt + archiveExt) ? arc
                : archiveAtLast ? arc : def;
        }
    }

    ///<summary>Return lines loaded from text or archived file if exists, othrwise null</summary>
    public static IEnumerable<string>? TryReadLines(string filePath, bool asArchived) {
        return File.Exists(filePath)
            ? asArchived
                ? ADocFileFormat.DecompressLines(filePath, Encoding.UTF8)
                : File.ReadAllLines(filePath, Encoding.UTF8)
            : null;
    }

    public static IEnumerable<string> TrimSpacesAndComments(this IEnumerable<string> lines) {
        return lines.Select(s => s.Trim()).Where(s => !(s.Length < 1 || s.StartsWith('#')));
    }

    ///<summary>Save lines to file, text or archived format selected by extension</summary>
    public static void WriteLines(IEnumerable<string> lines, string filePath, bool asArchived) {
        if (asArchived)
            ADocFileFormat.CompressLines(filePath, lines, Encoding.UTF8);
        else File.WriteAllLines(filePath, lines, Encoding.UTF8);
    }

    ///<summary>Open text or archived file in editor, return true if changes commited</summary>
    public static bool TryOpenWithAppSync(string filePath, string? configTrueExtOrNull) {
        if (configTrueExtOrNull is null ||
                filePath.EndsWith(configTrueExtOrNull, StringComparison.OrdinalIgnoreCase)) {
            return FilePath.TryOpenWithAppSync(filePath);
        }
        string trueFile = Path.ChangeExtension(filePath, configTrueExtOrNull);
        trueFile = trueFile.Insert(trueFile.Length - configTrueExtOrNull.Length,
            $"-unpack{DateTime.Now.ToFileTimeUtc() % 1000000:D6}");
        ADocFileFormat.Decompress(filePath, trueFile);
        if (FilePath.TryOpenWithAppSync(trueFile) is bool result) {
            ADocFileFormat.Compress(trueFile, filePath);
        }
        File.Delete(trueFile);
        return result;
    }
}