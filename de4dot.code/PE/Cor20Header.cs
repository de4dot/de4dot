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
	class Cor20Header : IFileLocation {
		public uint cb;
		public ushort majorRuntimeVersion;
		public ushort minorRuntimeVersion;
		public DataDirectory metaData;
		public uint flags;
		public uint entryPointToken;
		public DataDirectory resources;
		public DataDirectory strongNameSignature;
		public DataDirectory codeManagerTable;
		public DataDirectory vtableFixups;
		public DataDirectory exportAddressTableJumps;
		public DataDirectory managedNativeHeader;

		uint mdOffset, mdHeaderLength;
		public uint MetadataOffset {
			get { return mdOffset; }
		}
		public uint MetadataHeaderLength {
			get { return mdHeaderLength; }
		}

		uint offset;
		public uint Offset {
			get { return offset; }
		}

		public uint Length {
			get { return 18 * 4; }
		}

		public Cor20Header(BinaryReader reader) {
			offset = (uint)reader.BaseStream.Position;
			cb = reader.ReadUInt32();
			majorRuntimeVersion = reader.ReadUInt16();
			minorRuntimeVersion = reader.ReadUInt16();
			metaData.read(reader);
			flags = reader.ReadUInt32();
			entryPointToken = reader.ReadUInt32();
			resources.read(reader);
			strongNameSignature.read(reader);
			codeManagerTable.read(reader);
			vtableFixups.read(reader);
			exportAddressTableJumps.read(reader);
			managedNativeHeader.read(reader);
		}

		public void initMetadataTable(BinaryReader reader) {
			if (reader.ReadUInt32() != 0x424A5342)
				return;
			mdOffset = (uint)reader.BaseStream.Position - 4;
			reader.ReadUInt16();	// major version
			reader.ReadUInt16();	// minor version
			reader.ReadUInt32();	// reserved
			int slen = reader.ReadInt32();
			reader.BaseStream.Position += slen;
			reader.ReadUInt16();	// flags
			int streams = reader.ReadUInt16();
			for (int i = 0; i < streams; i++) {
				uint offset = reader.ReadUInt32();
				uint size = reader.ReadUInt32();
				skipString(reader);
			}
			mdHeaderLength = (uint)reader.BaseStream.Position - mdOffset;
		}

		void skipString(BinaryReader reader) {
			while (reader.ReadByte() != 0) {
				// nothing
			}
			reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;
		}
	}
}
