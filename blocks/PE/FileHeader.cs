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

using System.IO;

namespace de4dot.PE {
	public enum Machine : ushort {
		i386 = 0x14C,
		ia64 = 0x200,
		amd64 = 0x8664,
	}

	public class FileHeader : IFileLocation {
		public Machine machine;
		public ushort numberOfSections;
		public uint timeDateStamp;
		public uint pointerToSymbolTable;
		public uint numberOfSymbols;
		public ushort sizeOfOptionalHeader;
		public ushort characteristics;

		uint offset;
		public uint Offset {
			get { return offset; }
		}

		public uint Length {
			get { return 5 * 4; }
		}

		public FileHeader(BinaryReader reader) {
			offset = (uint)reader.BaseStream.Position;
			machine = (Machine)reader.ReadUInt16();
			numberOfSections = reader.ReadUInt16();
			timeDateStamp = reader.ReadUInt32();
			pointerToSymbolTable = reader.ReadUInt32();
			numberOfSymbols = reader.ReadUInt32();
			sizeOfOptionalHeader = reader.ReadUInt16();
			characteristics = reader.ReadUInt16();
		}
	}
}
