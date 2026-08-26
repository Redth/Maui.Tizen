using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// A minimal ELF reader, just enough to list the shared libraries a Linux binary requires
/// (its <c>DT_NEEDED</c> entries).
/// </summary>
/// <remarks>
/// This exists so the packaging tests can assert what the shipped native SkiaSharp actually
/// depends on, from any operating system and without shelling out to <c>ldd</c> or
/// <c>objdump</c> - neither of which exists on a macOS or Windows developer machine, and the
/// former of which cannot inspect a foreign architecture anyway.
/// </remarks>
internal static class ElfReader
{
	private const int DT_NULL = 0;
	private const int DT_NEEDED = 1;
	private const int DT_STRTAB = 5;

	private const uint PT_DYNAMIC = 2;

	public static bool IsElf(string path)
	{
		using var stream = File.OpenRead(path);
		Span<byte> magic = stackalloc byte[4];
		return stream.Read(magic) == 4
			&& magic[0] == 0x7F && magic[1] == (byte)'E' && magic[2] == (byte)'L' && magic[3] == (byte)'F';
	}

	/// <summary>
	/// Returns the <c>DT_NEEDED</c> shared library names, e.g. <c>libc.so.6</c>.
	/// </summary>
	/// <remarks>
	/// Both ELF32 and ELF64 are handled: the shipped natives include a 32-bit arm build alongside
	/// the 64-bit ones, and skipping it would leave exactly one architecture unchecked.
	/// </remarks>
	public static IReadOnlyList<string> GetNeededLibraries(string path)
	{
		var bytes = File.ReadAllBytes(path);

		if (bytes.Length < 52 || bytes[0] != 0x7F || bytes[1] != (byte)'E' || bytes[2] != (byte)'L' || bytes[3] != (byte)'F')
			throw new InvalidDataException($"'{path}' is not an ELF binary.");

		var is64Bit = bytes[4] == 2;
		var isLittleEndian = bytes[5] == 1;

		ulong ReadWord(int offset) => is64Bit
			? (isLittleEndian
				? BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset))
				: BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset)))
			: (isLittleEndian
				? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset))
				: BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)));

		uint ReadU32(int offset) => isLittleEndian
			? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset))
			: BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));

		ushort ReadU16(int offset) => isLittleEndian
			? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset))
			: BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));

		// Header offsets and the size of a dynamic entry both differ between the two classes.
		var programHeaderOffset = (int)(is64Bit ? ReadWord(0x20) : ReadU32(0x1C));
		var programHeaderSize = ReadU16(is64Bit ? 0x36 : 0x2A);
		var programHeaderCount = ReadU16(is64Bit ? 0x38 : 0x2C);
		var wordSize = is64Bit ? 8 : 4;
		var dynamicEntrySize = wordSize * 2;

		var segments = new List<(ulong VirtualAddress, ulong FileOffset, ulong FileSize)>();
		var dynamicOffset = 0UL;
		var dynamicSize = 0UL;

		for (var i = 0; i < programHeaderCount; i++)
		{
			var header = programHeaderOffset + (i * programHeaderSize);

			var type = ReadU32(header);

			// ELF32: p_offset 0x04, p_vaddr 0x08, p_filesz 0x10.
			// ELF64: p_offset 0x08, p_vaddr 0x10, p_filesz 0x20.
			var offset = is64Bit ? ReadWord(header + 0x08) : ReadU32(header + 0x04);
			var virtualAddress = is64Bit ? ReadWord(header + 0x10) : ReadU32(header + 0x08);
			var fileSize = is64Bit ? ReadWord(header + 0x20) : ReadU32(header + 0x10);

			segments.Add((virtualAddress, offset, fileSize));

			if (type == PT_DYNAMIC)
			{
				dynamicOffset = offset;
				dynamicSize = fileSize;
			}
		}

		if (dynamicOffset == 0)
			return Array.Empty<string>();

		ulong ToFileOffset(ulong virtualAddress)
		{
			foreach (var (segmentAddress, segmentOffset, segmentSize) in segments)
			{
				if (segmentSize != 0 && virtualAddress >= segmentAddress && virtualAddress < segmentAddress + segmentSize)
					return segmentOffset + (virtualAddress - segmentAddress);
			}

			return virtualAddress;
		}

		var stringTableAddress = 0UL;
		for (var cursor = (int)dynamicOffset; cursor + dynamicEntrySize <= (int)(dynamicOffset + dynamicSize); cursor += dynamicEntrySize)
		{
			var tag = (long)ReadWord(cursor);
			if (tag == DT_NULL)
				break;

			if (tag == DT_STRTAB)
				stringTableAddress = ReadWord(cursor + wordSize);
		}

		if (stringTableAddress == 0)
			return Array.Empty<string>();

		var stringTableOffset = (int)ToFileOffset(stringTableAddress);
		var needed = new List<string>();

		for (var cursor = (int)dynamicOffset; cursor + dynamicEntrySize <= (int)(dynamicOffset + dynamicSize); cursor += dynamicEntrySize)
		{
			var tag = (long)ReadWord(cursor);
			if (tag == DT_NULL)
				break;

			if (tag != DT_NEEDED)
				continue;

			var nameOffset = stringTableOffset + (int)ReadWord(cursor + wordSize);
			if (nameOffset < 0 || nameOffset >= bytes.Length)
				continue;

			var end = nameOffset;
			while (end < bytes.Length && bytes[end] != 0)
				end++;

			needed.Add(System.Text.Encoding.ASCII.GetString(bytes, nameOffset, end - nameOffset));
		}

		return needed;
	}
}
