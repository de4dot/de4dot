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
using System.Text;

namespace de4dot.PE {
	public class Resources {
		BinaryReader reader;
		uint startOffset;
		uint totalSize;
		ResourceDirectory root;

		public BinaryReader Reader {
			get { return reader; }
		}

		public Resources(BinaryReader reader, uint startOffset, uint totalSize) {
			this.reader = reader;
			this.startOffset = startOffset;
			this.totalSize = totalSize;
		}

		public ResourceDirectory getRoot() {
			if (root != null)
				return root;
			return root = new ResourceDirectory("root", this, startOffset == 0 ? -1 : 0);
		}

		public bool isSizeAvailable(int offset, int size) {
			if (offset < 0 || offset + size < offset)
				return false;
			return (uint)(offset + size) <= totalSize;
		}

		public bool isSizeAvailable(int size) {
			return isSizeAvailable((int)(reader.BaseStream.Position - startOffset), size);
		}

		public bool seek(int offset) {
			if (!isSizeAvailable(offset, 0))
				return false;
			reader.BaseStream.Position = startOffset + offset;
			return true;
		}

		public ushort readUInt16() {
			return reader.ReadUInt16();
		}

		public uint readUInt32() {
			return reader.ReadUInt32();
		}

		public byte[] readBytes(int size) {
			return reader.ReadBytes(size);
		}

		public string readString(int offset) {
			if (!seek(offset))
				return null;
			if (!isSizeAvailable(2))
				return null;
			int size = readUInt16();
			int sizeInBytes = size * 2;
			if (!isSizeAvailable(sizeInBytes))
				return null;
			var stringData = readBytes(sizeInBytes);
			try {
				return Encoding.Unicode.GetString(stringData);
			}
			catch {
				return null;
			}
		}
	}
}
