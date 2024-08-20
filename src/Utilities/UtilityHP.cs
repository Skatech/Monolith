using System;
using System.Globalization;

namespace Skatech.Monolith.Utilities;

internal static class UtilityHP {
    public const string Name = "hp";
    public const string Description = "Hashes strings using legacy x86 algorithm";

    public static void Run(string[] args) {
        if (args.Length > 0) {
            foreach (var str in args) {
                var hash = GetHashCodeLegacyX86(str).ToString("X8", CultureInfo.InvariantCulture);
                Console.WriteLine($"{str} ({hash})");
            }
        }
        else Console.WriteLine("HP Utility, usage:\r\n  hp string [string [string ...]]");
    }
    
    private  static int GetHashCodeLegacyX86(string str) {
        int acc1 = (5381 << 16) + 5381;
        int acc2 = acc1;
        for (int i = 0; i < str.Length; ++i) {
            int mix = str[i];
            if (++i < str.Length) {
                mix |= str[i] << 16;
            }
            if (i % 4 < 2) {
                acc1 = (acc1 << 5) + acc1 + (acc1 >> 27) ^ mix;
            }
            else acc2 = (acc2 << 5) + acc2 + (acc2 >> 27) ^ mix;
        }
        return acc1 + acc2 * 1566083941;
    }
}
