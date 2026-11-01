using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Gallery.Core.Ids;

public static class OpaqueId
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";
    private static readonly Regex ValidPattern = new Regex("^[a-z2-7]{26}$", RegexOptions.Compiled);

    public static string NewId()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Base32Encode(bytes);
    }

    public static bool IsValid(string? id)
    {
        if (id is null) return false;
        return ValidPattern.IsMatch(id);
    }

    private static string Base32Encode(byte[] data)
    {
        // 16 bytes = 128 bits; 128 / 5 = 25.6 → 26 chars (no padding)
        var chars = new char[26];
        var buffer = 0;
        var bitsLeft = 0;
        var index = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                chars[index++] = Alphabet[(buffer >> bitsLeft) & 0x1F];
            }
        }

        // Remaining bits (128 % 5 = 3 bits left after 25 chars)
        if (index < 26)
        {
            chars[index] = Alphabet[(buffer << (5 - bitsLeft)) & 0x1F];
        }

        return new string(chars);
    }
}
