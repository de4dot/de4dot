/*
    Copyright (C) 2011 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.IO;

namespace de4dot.PE {
	struct DataDirectory {
		public uint virtualAddress;
		public uint size;

		public override string ToString() {
			return string.Format("{0:X8} {1:X8}", virtualAddress, size);
		}
	}

	class OptionalHeader {
		public ushort magic;
		public byte majorLinkerVersion;
		public byte minorLinkerVersion;
		public uint sizeOfCode;
		public uint sizeOfInitializedData;
		public uint sizeOfUninitializedData;
		public uint addressOfEntryPoint;
		public uint baseOfCode;
		public uint baseOfData;	// 32-bit only
		public ulong imageBase;
		public uint sectionAlignment;
		public uint fileAlignment;
		public ushort majorOperatingSystemVersion;
		public ushort minorOperatingSystemVersion;
		public ushort majorImageVersion;
		public ushort minorImageVersion;
		public ushort majorSubsystemVersion;
		public ushort minorSubsystemVersion;
		public uint win32VersionValue;
		public uint sizeOfImage;
		public uint sizeOfHeaders;
		public uint checkSum;
		public ushort subsystem;
		public ushort dllCharacteristics;
		public ulong sizeOfStackReserve;
		public ulong sizeOfStackCommit;
		public ulong sizeOfHeapReserve;
		public ulong sizeOfHeapCommit;
		public uint loaderFlags;
		public uint numberOfRvaAndSizes;
		public DataDirectory[] dataDirectories;

		public OptionalHeader(BinaryReader reader) {
			magic = reader.ReadUInt16();
			majorLinkerVersion = reader.ReadByte();
			minorLinkerVersion = reader.ReadByte();
			sizeOfCode = reader.ReadUInt32();
			sizeOfInitializedData = reader.ReadUInt32();
			sizeOfUninitializedData = reader.ReadUInt32();
			addressOfEntryPoint = reader.ReadUInt32();
			baseOfCode = reader.ReadUInt32();
			if (is32bit())
				baseOfData = reader.ReadUInt32();
			imageBase = read4Or8(reader);
			sectionAlignment = reader.ReadUInt32();
			fileAlignment = reader.ReadUInt32();
			majorOperatingSystemVersion = reader.ReadUInt16();
			minorOperatingSystemVersion = reader.ReadUInt16();
			majorImageVersion = reader.ReadUInt16();
			minorImageVersion = reader.ReadUInt16();
			majorSubsystemVersion = reader.ReadUInt16();
			minorSubsystemVersion = reader.ReadUInt16();
			win32VersionValue = reader.ReadUInt32();
			sizeOfImage = reader.ReadUInt32();
			sizeOfHeaders = reader.ReadUInt32();
			checkSum = reader.ReadUInt32();
			subsystem = reader.ReadUInt16();
			dllCharacteristics = reader.ReadUInt16();
			sizeOfStackReserve = read4Or8(reader);
			sizeOfStackCommit = read4Or8(reader);
			sizeOfHeapReserve = read4Or8(reader);
			sizeOfHeapCommit = read4Or8(reader);
			loaderFlags = reader.ReadUInt32();
			numberOfRvaAndSizes = reader.ReadUInt32();

			dataDirectories = new DataDirectory[16];
			for (int i = 0; i < dataDirectories.Length; i++) {
				dataDirectories[i].virtualAddress = reader.ReadUInt32();
				dataDirectories[i].size = reader.ReadUInt32();
			}
		}

		ulong read4Or8(BinaryReader reader) {
			if (is32bit())
				return reader.ReadUInt32();
			return reader.ReadUInt64();
		}

		public bool is32bit() {
			return magic != 0x20B;
		}
	}
}
