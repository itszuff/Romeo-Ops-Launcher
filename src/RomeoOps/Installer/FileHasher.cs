using System.Security.Cryptography;

namespace RomeoOps.Installer;

public static class FileHasher
{
    public static string Compute(string path, string algorithm)
    {
        using var stream = File.OpenRead(path);
        using HashAlgorithm hash = algorithm.ToUpperInvariant() switch
        {
            "MD5" => MD5.Create(),
            "SHA256" => SHA256.Create(),
            _ => throw new ArgumentException("Unsupported hash algorithm: " + algorithm)
        };
        return Convert.ToHexString(hash.ComputeHash(stream));
    }

    public static void AssertMatch(string path, string algorithm, string expected)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Missing archive: " + path, path);
        var actual = Compute(path, algorithm);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{algorithm} mismatch for '{path}'. Expected {expected}; found {actual}. Check the exact release and redownload.");
        }
    }
}
