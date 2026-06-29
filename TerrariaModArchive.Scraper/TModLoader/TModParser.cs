using System.Text;
using TerrariaModArchive.Core.Models;

namespace TerrariaModArchive.Scraper.TModLoader;

public class TModMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string TModLoaderVersion { get; set; } = string.Empty;
    public ModSide Side { get; set; } = ModSide.Both;
}

public class TModParser
{
    // A simplified parser for reading basic metadata out of a .tmod file.
    // In reality, .tmod files have a specific binary structure (header, file count, file entries, then data).
    // The "build.txt" or Info is usually stored within the file data.
    // For this MVP, we will attempt to find standard signature strings inside the binary.
    public static async Task<TModMetadata> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var metadata = new TModMetadata();

        try
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            // Magic number for .tmod format is "TMOD"
            var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (magic != "TMOD")
            {
                throw new InvalidDataException("Invalid .tmod file format.");
            }

            // tModLoader version is a string encoded with a length prefix (BinaryWriter.Write(string))
            metadata.TModLoaderVersion = reader.ReadString();

            // Hash
            reader.ReadBytes(20);

            // Signature
            reader.ReadBytes(256);

            // Data length
            reader.ReadUInt32();

            // Internal mod name
            metadata.Name = reader.ReadString();

            // Mod version
            metadata.Version = reader.ReadString();

            // Read file entries
            int count = reader.ReadInt32();

            var fileEntries = new List<(string Name, int Length, int LengthCompressed)>();
            for (int i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                var length = reader.ReadInt32();
                var lengthCompressed = reader.ReadInt32();
                fileEntries.Add((name, length, lengthCompressed));
            }

            // Deflate stream offsets (we just need build.txt which is usually at the start or can be found)
            foreach (var entry in fileEntries)
            {
                if (entry.Name == "build.txt")
                {
                    // For tModLoader, data is compressed using DeflateStream.
                    // However, we just need to decompress the block corresponding to build.txt.
                    // Usually, for build.txt the data is small enough or even uncompressed in older formats,
                    // but standard is deflated.
                    try
                    {
                        using var deflateStream = new System.IO.Compression.DeflateStream(
                            new MemoryStream(reader.ReadBytes(entry.LengthCompressed)),
                            System.IO.Compression.CompressionMode.Decompress);

                        using var streamReader = new StreamReader(deflateStream);
                        var buildTxt = streamReader.ReadToEnd();

                        if (buildTxt.Contains("side = client", StringComparison.OrdinalIgnoreCase))
                            metadata.Side = ModSide.Client;
                        else if (buildTxt.Contains("side = server", StringComparison.OrdinalIgnoreCase))
                            metadata.Side = ModSide.Server;
                        else if (buildTxt.Contains("side = nosync", StringComparison.OrdinalIgnoreCase))
                            metadata.Side = ModSide.NoSync;
                        else
                            metadata.Side = ModSide.Both; // Default if omitted

                        break;
                    }
                    catch
                    {
                        // Fallback if deflate fails
                        metadata.Side = ModSide.Both;
                        break;
                    }
                }
                else
                {
                    // Skip to next file block
                    reader.ReadBytes(entry.LengthCompressed);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing .tmod file: {ex.Message}");
        }

        return metadata;
    }
}
