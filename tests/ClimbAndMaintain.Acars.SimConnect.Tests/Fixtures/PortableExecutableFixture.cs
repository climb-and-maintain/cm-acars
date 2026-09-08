using System.Buffers.Binary;
using System.Text;
using ClimbAndMaintain.Acars.SimConnect.Validation;

namespace ClimbAndMaintain.Acars.SimConnect.Tests.Fixtures;

internal static class PortableExecutableFixture
{
    private const int PeOffset = 0x80;
    private const int OptionalHeaderOffset = PeOffset + 24;
    private const int SectionHeaderOffset = OptionalHeaderOffset + 0xF0;
    private const int SectionRawOffset = 0x200;
    private const uint SectionRva = 0x1000;

    public static byte[] Create(
        IEnumerable<string>? exports = null,
        ushort machine = 0x8664,
        bool pe32Plus = true,
        bool managed = false)
    {
        string[] exportNames = (exports ?? SimConnectExportSurface.RequiredExports).ToArray();
        byte[] image = new byte[0x800];

        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        WriteUInt32(image, 0x3C, PeOffset);
        image[PeOffset] = (byte)'P';
        image[PeOffset + 1] = (byte)'E';
        WriteUInt16(image, PeOffset + 4, machine);
        WriteUInt16(image, PeOffset + 6, 1);
        WriteUInt16(image, PeOffset + 20, 0xF0);
        WriteUInt16(image, PeOffset + 22, 0x2022);

        WriteUInt16(image, OptionalHeaderOffset, pe32Plus ? (ushort)0x20B : (ushort)0x10B);
        WriteUInt32(image, OptionalHeaderOffset + 16, 0);
        WriteUInt32(image, OptionalHeaderOffset + 20, SectionRva);
        WriteUInt64(image, OptionalHeaderOffset + 24, 0x0000000140000000);
        WriteUInt32(image, OptionalHeaderOffset + 32, 0x1000);
        WriteUInt32(image, OptionalHeaderOffset + 36, 0x200);
        WriteUInt32(image, OptionalHeaderOffset + 56, 0x2000);
        WriteUInt32(image, OptionalHeaderOffset + 60, 0x200);
        WriteUInt16(image, OptionalHeaderOffset + 68, 3);
        WriteUInt32(image, OptionalHeaderOffset + 108, 16);
        WriteUInt32(image, OptionalHeaderOffset + 112, SectionRva);
        WriteUInt32(image, OptionalHeaderOffset + 116, 0x300);

        if (managed)
        {
            int clrDirectory = OptionalHeaderOffset + 112 + (14 * 8);
            WriteUInt32(image, clrDirectory, SectionRva + 0x500);
            WriteUInt32(image, clrDirectory + 4, 0x48);

            int clrHeader = SectionRawOffset + 0x500;
            WriteUInt32(image, clrHeader, 0x48);
            WriteUInt16(image, clrHeader + 4, 2);
            WriteUInt16(image, clrHeader + 6, 5);
            WriteUInt32(image, clrHeader + 8, SectionRva + 0x550);
            WriteUInt32(image, clrHeader + 12, 0x20);
            WriteUInt32(image, clrHeader + 16, 1);
        }

        Encoding.ASCII.GetBytes(".rdata").CopyTo(image, SectionHeaderOffset);
        WriteUInt32(image, SectionHeaderOffset + 8, 0x600);
        WriteUInt32(image, SectionHeaderOffset + 12, SectionRva);
        WriteUInt32(image, SectionHeaderOffset + 16, 0x600);
        WriteUInt32(image, SectionHeaderOffset + 20, SectionRawOffset);
        WriteUInt32(image, SectionHeaderOffset + 36, 0x40000040);

        int exportDirectory = SectionRawOffset;
        WriteUInt32(image, exportDirectory + 12, SectionRva + 0x1C0);
        WriteUInt32(image, exportDirectory + 16, 1);
        WriteUInt32(image, exportDirectory + 20, checked((uint)exportNames.Length));
        WriteUInt32(image, exportDirectory + 24, checked((uint)exportNames.Length));
        WriteUInt32(image, exportDirectory + 28, SectionRva + 0x40);
        WriteUInt32(image, exportDirectory + 32, SectionRva + 0x100);
        WriteUInt32(image, exportDirectory + 36, SectionRva + 0x180);
        Encoding.ASCII.GetBytes("Fixture.dll\0").CopyTo(image, SectionRawOffset + 0x1C0);

        int stringOffset = SectionRawOffset + 0x200;
        for (int index = 0; index < exportNames.Length; index++)
        {
            WriteUInt32(image, SectionRawOffset + 0x40 + (index * 4), SectionRva + 0x300);
            WriteUInt32(image, SectionRawOffset + 0x100 + (index * 4), checked(SectionRva + (uint)(stringOffset - SectionRawOffset)));
            WriteUInt16(image, SectionRawOffset + 0x180 + (index * 2), checked((ushort)index));
            byte[] name = Encoding.ASCII.GetBytes(exportNames[index] + '\0');
            name.CopyTo(image, stringOffset);
            stringOffset += name.Length;
        }

        return image;
    }

    private static void WriteUInt16(byte[] destination, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset, sizeof(ushort)), value);

    private static void WriteUInt32(byte[] destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, sizeof(uint)), value);

    private static void WriteUInt32(byte[] destination, int offset, int value) =>
        WriteUInt32(destination, offset, checked((uint)value));

    private static void WriteUInt64(byte[] destination, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(destination.AsSpan(offset, sizeof(ulong)), value);
}
