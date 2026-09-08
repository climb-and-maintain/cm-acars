using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ClimbAndMaintain.Acars.SimConnect.Validation;

internal static class PortableExecutableExports
{
    private const int ExportDirectorySize = 40;
    private const uint MaximumExportNames = 65_536;
    private const int MaximumExportNameLength = 512;

    public static IReadOnlyList<string> Read(byte[] image, PEHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(headers);

        PEHeader peHeader = headers.PEHeader
            ?? throw new BadImageFormatException("The file does not contain a PE optional header.");

        DirectoryEntry exportDirectory = peHeader.ExportTableDirectory;
        if (exportDirectory.RelativeVirtualAddress == 0 || exportDirectory.Size < ExportDirectorySize)
        {
            return [];
        }

        int directoryOffset = RvaToFileOffset(headers, exportDirectory.RelativeVirtualAddress, ExportDirectorySize, image.Length);
        ReadOnlySpan<byte> directory = image.AsSpan(directoryOffset, ExportDirectorySize);
        uint numberOfNames = BinaryPrimitives.ReadUInt32LittleEndian(directory[24..]);
        uint addressOfNames = BinaryPrimitives.ReadUInt32LittleEndian(directory[32..]);

        if (numberOfNames > MaximumExportNames)
        {
            throw new BadImageFormatException("The export name count is unreasonably large.");
        }

        if (numberOfNames == 0)
        {
            return [];
        }

        int nameCount = checked((int)numberOfNames);
        int tableLength = checked(nameCount * sizeof(uint));
        int namesOffset = RvaToFileOffset(headers, checked((int)addressOfNames), tableLength, image.Length);
        List<string> names = new(nameCount);

        for (int index = 0; index < nameCount; index++)
        {
            int entryOffset = checked(namesOffset + (index * sizeof(uint)));
            uint nameRva = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(entryOffset, sizeof(uint)));
            int nameOffset = RvaToFileOffset(headers, checked((int)nameRva), 1, image.Length);
            names.Add(ReadNullTerminatedAscii(image, nameOffset));
        }

        names.Sort(StringComparer.Ordinal);
        return names.AsReadOnly();
    }

    private static int RvaToFileOffset(PEHeaders headers, int rva, int requiredLength, int imageLength)
    {
        if (rva < 0 || requiredLength < 0)
        {
            throw new BadImageFormatException("A PE relative virtual address is invalid.");
        }

        foreach (SectionHeader section in headers.SectionHeaders)
        {
            long sectionStart = section.VirtualAddress;
            long sectionLength = Math.Max(section.VirtualSize, section.SizeOfRawData);
            long delta = (long)rva - sectionStart;

            if (delta < 0 || delta >= sectionLength)
            {
                continue;
            }

            if (delta + requiredLength > section.SizeOfRawData)
            {
                throw new BadImageFormatException("A PE directory extends beyond its section's file data.");
            }

            long fileOffset = section.PointerToRawData + delta;
            if (fileOffset < 0 || fileOffset + requiredLength > imageLength)
            {
                throw new BadImageFormatException("A PE directory extends beyond the file.");
            }

            return checked((int)fileOffset);
        }

        throw new BadImageFormatException("A PE relative virtual address does not map to a file section.");
    }

    private static string ReadNullTerminatedAscii(byte[] image, int offset)
    {
        int available = Math.Min(MaximumExportNameLength, image.Length - offset);
        int length = 0;
        while (length < available && image[offset + length] != 0)
        {
            length++;
        }

        if (length == available)
        {
            throw new BadImageFormatException("An exported function name is not null terminated.");
        }

        return Encoding.ASCII.GetString(image, offset, length);
    }
}
