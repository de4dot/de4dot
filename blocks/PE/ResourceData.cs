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

namespace de4dot.PE {
	public class ResourceData : ResourceDirectoryEntry {
		uint rva;
		uint size;

		public uint RVA {
			get { return rva; }
		}

		public uint Size {
			get { return size; }
		}

		public ResourceData(int id, uint rva, uint size)
			: base(id) {
			this.rva = rva;
			this.size = size;
		}

		public ResourceData(string name, uint dataOffset, uint dataSize)
			: base(name) {
			this.rva = dataOffset;
			this.size = dataSize;
		}

		public override string ToString() {
			return string.Format("RVA: {0:X8} SIZE: {1:X8}, NAME: {2}", rva, size, getName());
		}
	}
}
