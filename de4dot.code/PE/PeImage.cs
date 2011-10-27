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

using System;
using System.IO;

namespace de4dot.PE {
	class PeImage {
		BinaryReader reader;
		BinaryWriter writer;
		FileHeader fileHeader;
		OptionalHeader optionalHeader;
		SectionHeader[] sectionHeaders;
		Cor20Header cor20Header;
		SectionHeader dotNetSection;

		public PeImage(byte[] data)
			: this(new MemoryStream(data)) {
		}

		public PeImage(Stream stream) {
			reader = new BinaryReader(stream);
			writer = new BinaryWriter(stream);

			init();
		}

		void seek(uint position) {
			reader.BaseStream.Position = position;
		}

		void seekRva(uint rva) {
			seek(rvaToOffset(rva));
		}

		void skip(int bytes) {
			reader.BaseStream.Position += bytes;
		}

		void init() {
			seek(0);
			if (reader.ReadUInt16() != 0x5A4D)
				throw new BadImageFormatException("Not a PE file");
			skip(29 * 2);
			seek(reader.ReadUInt32());

			if (reader.ReadUInt32() != 0x4550)
				throw new BadImageFormatException("Not a PE file");
			fileHeader = new FileHeader(reader);
			optionalHeader = new OptionalHeader(reader);

			sectionHeaders = new SectionHeader[fileHeader.numberOfSections];
			for (int i = 0; i < sectionHeaders.Length; i++)
				sectionHeaders[i] = new SectionHeader(reader);

			uint netOffset = optionalHeader.dataDirectories[14].virtualAddress;
			if (netOffset != 0) {
				seekRva(netOffset);
				cor20Header = new Cor20Header(reader);
				dotNetSection = getSectionHeader(netOffset);
				seekRva(cor20Header.metaData.virtualAddress);
				cor20Header.initMetadataTable(reader);
			}
		}

		SectionHeader getSectionHeader(uint rva) {
			for (int i = 0; i < sectionHeaders.Length; i++) {
				var section = sectionHeaders[i];
				if (section.virtualAddress <= rva && rva < section.virtualAddress + section.virtualSize)
					return section;
			}
			return null;
		}

		uint rvaToOffset(uint rva) {
			var section = getSectionHeader(rva);
			if (section == null)
				throw new ApplicationException(string.Format("Invalid RVA {0:X8}", rva));
			return rva - section.virtualAddress + section.pointerToRawData;
		}

		bool intersect(uint offset1, uint length1, uint offset2, uint length2) {
			return !(offset1 + length1 <= offset2 || offset2 + length2 <= offset1);
		}

		bool intersect(uint offset, uint length, IFileLocation location) {
			return intersect(offset, length, location.Offset, location.Length);
		}

		public bool dotNetSafeWrite(uint rva, byte[] data) {
			if (cor20Header != null) {
				uint offset = rvaToOffset(rva);
				uint length = (uint)data.Length;

				if (!dotNetSection.isInside(offset, length))
					return false;
				if (intersect(offset, length, cor20Header))
					return false;
				if (intersect(offset, length, cor20Header.MetadataOffset, cor20Header.MetadataHeaderLength))
					return false;
			}

			write(rva, data);
			return true;
		}

		public void write(uint rva, byte[] data) {
			seekRva(rva);
			writer.Write(data);
		}

		public int readInt32(uint rva) {
			seekRva(rva);
			return reader.ReadInt32();
		}

		public byte[] readBytes(uint rva, int size) {
			seekRva(rva);
			return reader.ReadBytes(size);
		}
	}
}
