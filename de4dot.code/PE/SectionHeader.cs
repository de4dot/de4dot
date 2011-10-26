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
using System.Text;

namespace de4dot.PE {
	class SectionHeader {
		public byte[] name;
		public uint virtualSize;
		public uint virtualAddress;
		public uint sizeOfRawData;
		public uint pointerToRawData;
		public uint pointerToRelocations;
		public uint pointerToLinenumbers;
		public ushort numberOfRelocations;
		public ushort numberOfLinenumbers;
		public uint characteristics;
		public string displayName;

		public SectionHeader(BinaryReader reader) {
			name = reader.ReadBytes(8);
			virtualSize = reader.ReadUInt32();
			virtualAddress = reader.ReadUInt32();
			sizeOfRawData = reader.ReadUInt32();
			pointerToRawData = reader.ReadUInt32();
			pointerToRelocations = reader.ReadUInt32();
			pointerToLinenumbers = reader.ReadUInt32();
			numberOfRelocations = reader.ReadUInt16();
			numberOfLinenumbers = reader.ReadUInt16();
			characteristics = reader.ReadUInt32();

			var sb = new StringBuilder(name.Length);
			foreach (var c in name) {
				if (c == 0)
					break;
				sb.Append((char)c);
			}
			displayName = sb.ToString();
		}

		public override string ToString() {
			return string.Format("{0:X8} {1:X8} {2:X8} - {3}", virtualAddress, virtualSize, sizeOfRawData, displayName);
		}
	}
}
