using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;

namespace Skatech.IO;

static class FilePath {
    ///<summary>Return name of file and directory path in out variable, return null on directory input</summary>
    public static string? SplitFile(string directoryOrFilePath, out string directoryPath) {
        if (Directory.Exists(directoryOrFilePath)) {
            directoryPath = directoryOrFilePath;
            return null;
        }
        directoryPath = GetParentDirectory(directoryOrFilePath);
        return Path.GetFileName(directoryOrFilePath);
    }

    // public static string ReplaceFileName(ReadOnlySpan<char> path, ReadOnlySpan<char> newName) {
    //     return Path.Join(Path.GetDirectoryName(path), newName);
    // }

    public static string EnsureHasExtension(string path, ReadOnlySpan<char> defaultExt) {
        return Path.HasExtension(path) ? path : defaultExt.StartsWith(".")
            ? String.Concat(path, defaultExt) : String.Concat(path, ".", defaultExt);
    }

    // public static string NormalizeExtension(string extension) {
    //     return extension.StartsWith('.') ? extension : '.' + extension;
    // }

    public static string EnsureHasRoot(string path, ReadOnlySpan<char> defaultRoot) {
        return IsRooted(path) ? path : Path.Join(defaultRoot, path);
    }

    public static bool IsRooted(string path) {
        return Path.IsPathRooted(path) && path.Length > 1 && // windows path fix
            IsDirectorySeparatorChar(path[0]) == IsDirectorySeparatorChar(path[1]);
    }

    ///<summary>Return parent directory, throw exception when failed</summary>
    public static string GetParentDirectory(string path) {
        return Path.GetDirectoryName(path) ??
            throw new Exception($"Unable to get directory of '{path}'");
    }

    public static bool IsExtensionEqual(string path, string extension) {
        return Equals(Path.GetExtension(path.AsSpan()), extension);
    }
    
    public static bool IsExtensionEqual(ReadOnlySpan<char> path, ReadOnlySpan<char> extension) {
        return Equals(Path.GetExtension(path), extension);
    }

    public static bool Equals(string? path1, string? path2) {
        return string.Equals(path1, path2, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Equals(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2) {
        return path1.Equals(path2, StringComparison.OrdinalIgnoreCase);
    }

    ///<summary>Return true when character are path separator</summary>
    public static bool IsDirectorySeparatorChar(char character) {
        return character == Path.DirectorySeparatorChar ||
                character == Path.AltDirectorySeparatorChar;
    }

    ///<summary>Return true when characters equal in file paths</summary>
    public static bool IsPathCharactersEquals(char a, char b) {
        return a == b || char.ToLower(a) == char.ToLower(b)
            || (IsDirectorySeparatorChar(a) && IsDirectorySeparatorChar(b));
    }

    ///<summary>Return true when pattern match input</summary>
    public static bool IsMatch(ReadOnlySpan<char> input, ReadOnlySpan<char> pattern) {
        int ii = 0, ip = 0;
        while (ip < pattern.Length) {
            char cp = pattern[ip];
            if (cp == '*') {
                if (ii < input.Length) {
                    if (IsMatch(input.Slice(ii), pattern.Slice(ip + 1))) {
                        return true;
                    }
                    ii++;
                }
                else ip++;
            }
            else if (ii < input.Length && (cp == '?' || IsPathCharactersEquals(cp, input[ii]))) {
                ii++;
                ip++;
            }
            else return false;
        }        
        return ii == input.Length;
    }

    ///<summary>Return true when pattern match input or pattern is null</summary>
    public static bool IsMatch(ReadOnlySpan<char> input, string? pattern) {
        return pattern is null ? true : IsMatch(input, pattern.AsSpan());
    }

    ///<summary>Return true when pattern match input or pattern is null</summary>
    public static bool IsMatch(string input, string? pattern) {
        return pattern is null ? true : IsMatch(input, pattern.AsSpan());
    }

    ///<summary>Return true when input contains substitution symbols</summary>
    public static bool IsPattern(ReadOnlySpan<char> input) {
        return input.Contains('*') || input.Contains('?');
    }

    ///<summary>Return true when input contains substitution symbols, can accept null</summary>
    public static bool IsPattern(string? input) {
        return input is null ? false : input.Contains('*') || input.Contains('?');
    }

    ///<summary>Filter file names using pattern or exactly matching string, accepts null as no filter</summary>
    public static IEnumerable<string> WhereMatch(this IEnumerable<string> source, string? pattern) {
        return (pattern is not null)
            ? IsPattern(pattern)
                ? source.Where(n => IsMatch(n, pattern))
                : source.Where(n => Equals(n, pattern))
            : source;
    }

    ///<summary>Open file with default application.
    ///Return true when file changed, false when unchanged or file not exists</summary>
    public static bool TryOpenWithAppSync(string filePath) {
        var changed = File.GetLastWriteTime(filePath);
        if (TryOpenWithApp(filePath) is Process process) {
            try {
                process.WaitForExit();
            } catch {
                throw new Exception($"Failed to start editor for '{filePath}");
            }
            return changed < File.GetLastWriteTime(filePath);
        }
        return false;
    }

    ///<summary>Open file with default application. Return application process or null when failed</summary>
    public static Process? TryOpenWithApp(string filePath) {
       if (File.Exists(filePath)) {
            var process = new Process();
            process.StartInfo = new ProcessStartInfo() {
                UseShellExecute = true, FileName = filePath };
            process.Start();
            return process;
        }
        return null;
    }
}

struct FileSize {
    public enum Units : byte { b, Kb, Mb, Gb, Tb };
    public readonly long Length;
    public FileSize(long length) => Length = length;
    public override string ToString() => ToString(0);

    public Units GetUnits() {
        return (Units)Math.Min((int)Units.Tb, Math.Floor(Math.Log(Math.Max(1L, Length)) / Math.Log(1024)));
    }

    public double GetSize(Units units, int digitsRound) {
        return Math.Round(Length / Math.Pow(1024, (byte)units), digitsRound);
    }

    public string ToString(int digitsRound) {
        Units units = GetUnits();
        return GetSize(units, digitsRound) + Enum.GetName<Units>(units) ?? string.Empty;
    }

    public static FileSize FromFile(string filePath) {
        var info = new FileInfo(filePath);
        return new FileSize(info.Length);
    }
}