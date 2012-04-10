/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
	public class PeImage {
		BinaryReader reader;
		BinaryWriter writer;
		FileHeader fileHeader;
		OptionalHeader optionalHeader;
		SectionHeader[] sectionHeaders;
		Cor20Header cor20Header;
		SectionHeader dotNetSection;
		Resources resources;

		public BinaryReader Reader {
			get { return reader; }
		}

		public uint ImageLength {
			get { return (uint)reader.BaseStream.Length; }
		}

		public Cor20Header Cor20Header {
			get { return cor20Header; }
		}

		public Resources Resources {
			get { return resources; }
		}

		public SectionHeader[] Sections {
			get { return sectionHeaders; }
		}

		public uint FileHeaderOffset {
			get { return fileHeader.Offset; }
		}

		public PeImage(byte[] data)
			: this(new MemoryStream(data)) {
		}

		public PeImage(Stream stream) {
			reader = new BinaryReader(stream);
			if (stream.CanWrite)
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

			uint netRva = optionalHeader.dataDirectories[14].virtualAddress;
			if (netRva != 0) {
				seekRva(netRva);
				cor20Header = new Cor20Header(reader);
				dotNetSection = getSectionHeaderRva(netRva);
				seekRva(cor20Header.metadataDirectory.virtualAddress);
				cor20Header.initMetadataTable();
			}

			uint resourceRva = optionalHeader.dataDirectories[2].virtualAddress;
			uint resourceOffset = 0;
			if (resourceRva != 0)
				resourceOffset = rvaToOffset(resourceRva);
			resources = new Resources(reader, resourceOffset, optionalHeader.dataDirectories[2].size);
		}

		SectionHeader getSectionHeaderRva(uint rva) {
			for (int i = 0; i < sectionHeaders.Length; i++) {
				var section = sectionHeaders[i];
				if (section.virtualAddress <= rva && rva < section.virtualAddress + Math.Max(section.virtualSize, section.sizeOfRawData))
					return section;
			}
			return null;
		}

		SectionHeader getSectionHeaderOffset(uint offset) {
			for (int i = 0; i < sectionHeaders.Length; i++) {
				var section = sectionHeaders[i];
				if (section.pointerToRawData <= offset && offset < section.pointerToRawData + section.sizeOfRawData)
					return section;
			}
			return null;
		}

		public uint rvaToOffset(uint rva) {
			var section = getSectionHeaderRva(rva);
			if (section == null)
				throw new ApplicationException(string.Format("Invalid RVA {0:X8}", rva));
			return rva - section.virtualAddress + section.pointerToRawData;
		}

		public uint offsetToRva(uint offset) {
			var section = getSectionHeaderOffset(offset);
			if (section == null)
				throw new ApplicationException(string.Format("Invalid offset {0:X8}", offset));
			return offset - section.pointerToRawData + section.virtualAddress;
		}

		bool intersect(uint offset1, uint length1, uint offset2, uint length2) {
			return !(offset1 + length1 <= offset2 || offset2 + length2 <= offset1);
		}

		bool intersect(uint offset, uint length, IFileLocation location) {
			return intersect(offset, length, location.Offset, location.Length);
		}

		public bool dotNetSafeWriteOffset(uint offset, byte[] data) {
			if (cor20Header != null) {
				uint length = (uint)data.Length;

				if (!dotNetSection.isInside(offset, length))
					return false;
				if (intersect(offset, length, cor20Header))
					return false;
				if (intersect(offset, length, cor20Header.MetadataOffset, cor20Header.MetadataHeaderLength))
					return false;
			}

			offsetWrite(offset, data);
			return true;
		}

		public bool dotNetSafeWrite(uint rva, byte[] data) {
			return dotNetSafeWriteOffset(rvaToOffset(rva), data);
		}

		public void write(uint rva, byte[] data) {
			seekRva(rva);
			writer.Write(data);
		}

		public void writeUint16(uint rva, ushort data) {
			seekRva(rva);
			writer.Write(data);
		}

		public void writeUint32(uint rva, uint data) {
			seekRva(rva);
			writer.Write(data);
		}

		public byte readByte(uint rva) {
			seekRva(rva);
			return reader.ReadByte();
		}

		public ushort readUInt16(uint rva) {
			seekRva(rva);
			return reader.ReadUInt16();
		}

		public uint readUInt32(uint rva) {
			seekRva(rva);
			return reader.ReadUInt32();
		}

		public int readInt32(uint rva) {
			seekRva(rva);
			return reader.ReadInt32();
		}

		public byte[] readBytes(uint rva, int size) {
			seekRva(rva);
			return reader.ReadBytes(size);
		}

		public void offsetWrite(uint offset, byte[] data) {
			seek(offset);
			writer.Write(data);
		}

		public byte[] offsetReadBytes(uint offset, int size) {
			seek(offset);
			return reader.ReadBytes(size);
		}

		public uint offsetRead(uint offset, int size) {
			if (size == 2) return offsetReadUInt16(offset);
			if (size == 4) return offsetReadUInt32(offset);
			throw new NotImplementedException();
		}

		public byte offsetReadByte(uint offset) {
			seek(offset);
			return reader.ReadByte();
		}

		public ushort offsetReadUInt16(uint offset) {
			seek(offset);
			return reader.ReadUInt16();
		}

		public uint offsetReadUInt32(uint offset) {
			seek(offset);
			return reader.ReadUInt32();
		}

		public void offsetWrite(uint offset, uint data, int size) {
			if (size == 2)
				offsetWriteUInt16(offset, (ushort)data);
			else if (size == 4)
				offsetWriteUInt32(offset, data);
			else
				throw new NotImplementedException();
		}

		public void offsetWriteUInt16(uint offset, ushort data) {
			seek(offset);
			writer.Write(data);
		}

		public void offsetWriteUInt32(uint offset, uint data) {
			seek(offset);
			writer.Write(data);
		}
	}
}
