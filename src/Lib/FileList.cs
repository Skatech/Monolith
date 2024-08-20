using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Skatech.IO;


class FileList {
    public readonly List<string> Files;
    public FileList(IEnumerable<string> files) => Files = new(files);

    ///<summary>Return true when list contains file template specified file matches</summary>
    public bool ContainsTemplatesMatching(string fileName) {
        return Files.Any(s => FilePath.IsMatch(fileName, s));
    }

    ///<summary>Return first record specified file matches, null when no matches found</summary>
    public string? FirstMatchOrDefault(string fileName) {
        return Files.FirstOrDefault(s => FilePath.IsMatch(fileName, s));
    }

    ///<summary>Return list of distinct lines from specified files, without comments and empty lines</summary>
    public static FileList FromFiles(params string[] files) {
        return new (files.Where(f => File.Exists(f)).SelectMany(f => File.ReadAllLines(f))
            .Select(s => s.Trim()).Where(s => !(s.Length < 1 || s.StartsWith('#')))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    ///<summary>Return true when file created, false when already exists</summary>
    public static bool CreateFile(
                string filePath, string commentText, params string[] fileNamesOrPatterns) {
        if (File.Exists(filePath))
            return false;
        var lines = commentText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => "# " + s).Concat(fileNamesOrPatterns);
        File.WriteAllLines(filePath, lines, Encoding.UTF8);
        return true;
    }

    ///<summary>Return path to global or local file list</summary>
    public static string GetPath(string fileName, string? directoryOrNullForGlobal = null) {
        return Path.Combine(directoryOrNullForGlobal ??
            Skatech.ComponentArchitecture.OperationGroup.ApplicationDataDirectory, fileName);
    }
}
