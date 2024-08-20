using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;

namespace Skatech.IO;

static class ADocFileFormat {
    public const string DefaultFileExtension = "adoc";

    public static Stream CreateDecompressStream(string archiveFile) {
        return new DeflateStream(File.OpenRead(archiveFile), CompressionMode.Decompress, false);
    }

    public static Stream CreateCompressStream(string archiveFile) {
        return new DeflateStream(File.Create(archiveFile), CompressionLevel.Optimal, false);
    }
    
    public static void Decompress(string archiveFile, string outputFile) {
        using var stream = CreateDecompressStream(archiveFile);
        using var file = File.Create(outputFile);
        stream.CopyTo(file);
    }

    public static void Compress(string sourceFile, string archiveFile) {
        using var stream = CreateCompressStream(archiveFile);
        using var file = File.OpenRead(sourceFile);
        file.CopyTo(stream);
    }

    public static IEnumerable<string> DecompressLines(string archiveFile, Encoding encoding) {
        using var stream = ADocFileFormat.CreateDecompressStream(archiveFile);
        using var reader = new StreamReader(stream, encoding);
        while(reader.ReadLine() is string line)
            yield return line;
    }

    public static void CompressLines(string archiveFile, IEnumerable<string> lines, Encoding encoding) {
        using var stream = CreateCompressStream(archiveFile);
        using var writer = new StreamWriter(stream, encoding);
        foreach (var line in lines)
            writer.WriteLine(line);
    }
}

static class SDocFileFormat {
    const int ITER_MIN = 90_000, ITER_MAX = 100_000, SALT_SIZE = 8, IVEC_SIZE = 16, KVEC_SIZE = 32;
    const uint SIGN_CODE = 0x31434453U;
    public const string DefaultFileExtension = "sdoc";

    public static Stream CreateDecryptStream(string archiveFile, string password) {
        using var algorithm = Aes.Create();
        var input = File.OpenRead(archiveFile);
        using (var reader = new BinaryReader(input, Encoding.ASCII, true)) {
            if (reader.ReadUInt32() != SIGN_CODE) {
                throw new FormatException("Invalid document prefix signature");
            }
            var iter = reader.ReadInt32();
            var salt = reader.ReadBytes(SALT_SIZE);
            var ivec = reader.ReadBytes(IVEC_SIZE);

            using (var deriver = new Rfc2898DeriveBytes(password, salt, iter, HashAlgorithmName.SHA1)) {
                algorithm.Key = deriver.GetBytes(KVEC_SIZE);
                algorithm.IV = ivec;
            }
        }
        return new DeflateStream(
            new CryptoStream(input, algorithm.CreateDecryptor(), CryptoStreamMode.Read, false),
            CompressionMode.Decompress, false);
    }

    static Stream CreateEncryptStream(string archiveFile, string password) {
        using var algorithm = Aes.Create();
        var output = File.Create(archiveFile);
        using (var writer = new BinaryWriter(output, Encoding.ASCII, true)) {
            var iter = RandomNumberGenerator.GetInt32(ITER_MIN, ITER_MAX);
            var salt = RandomNumberGenerator.GetBytes(SALT_SIZE);
            var ivec = RandomNumberGenerator.GetBytes(IVEC_SIZE);
            writer.Write(SIGN_CODE);
            writer.Write(iter);
            writer.Write(salt);
            writer.Write(ivec);

            using (var deriver = new Rfc2898DeriveBytes(password, salt, iter, HashAlgorithmName.SHA1)) {
                algorithm.Key = deriver.GetBytes(KVEC_SIZE);
                algorithm.IV = ivec;
            }
        }
        return new DeflateStream(
            new CryptoStream(output, algorithm.CreateEncryptor(), CryptoStreamMode.Write, false),
            CompressionLevel.Optimal, false);
    }
    
    public static void Decrypt(string archiveFile, string outputFile, string password) {
        using var stream = CreateDecryptStream(archiveFile, password);
        using var file = File.Create(outputFile);
        stream.CopyTo(file);
    }

    public static void Encrypt(string inputFile, string archiveFile, string password) {
        using var stream = CreateEncryptStream(archiveFile, password);
        using var file = File.OpenRead(inputFile);
        file.CopyTo(stream);
    }

    public static IEnumerable<string> DecryptLines(string archiveFile, string password, Encoding encoding) {
        using var stream = CreateDecryptStream(archiveFile, password);
        using var reader = new StreamReader(stream, encoding);
        while(reader.ReadLine() is string line)
            yield return line;
    }

    public static void EncryptLines(string archiveFile, string password, IEnumerable<string> lines, Encoding encoding) {
        using var stream = CreateEncryptStream(archiveFile, password);
        using var writer = new StreamWriter(stream, encoding);
        foreach (var line in lines)
            writer.WriteLine(line);
    }
}

class SecuritySession {
    System.Net.NetworkCredential? _cred;

    public string GetPassword() {
        _cred ??= new System.Net.NetworkCredential(null, ReadPassword(true, "Password: "));
        return _cred.Password;
    }

    public void Reset() {
        _cred = null;
    }

    public static System.Security.SecureString ReadPassword(
            bool restoreConsole = false, string? displayMessage = null, char charMask = '*') {
        var pass = new System.Security.SecureString();
        Console.Write(displayMessage);
        while (true) {
            ConsoleKeyInfo i = Console.ReadKey(true);
            if (i.Key == ConsoleKey.Enter) {
                break;
            }
            else if (i.Key == ConsoleKey.Backspace) {
                if (pass.Length > 0) {
                    pass.RemoveAt(pass.Length - 1);
                    Console.Write("\b \b");
                }
            }
            else if (i.KeyChar != '\u0000') {
                pass.AppendChar(i.KeyChar);
                Console.Write(charMask);
            }
        }
        if (restoreConsole) {
            for(int n = pass.Length + displayMessage?.Length ?? 0; n > 0; --n)
                Console.Write("\b \b");
        }
        return pass;
    }
}