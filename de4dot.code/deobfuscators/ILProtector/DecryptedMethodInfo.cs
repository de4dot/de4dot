/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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

namespace de4dot.code.deobfuscators.ILProtector {
	[Serializable]
	class DecryptedMethodInfo {
		public int id;
		public byte[] data;

		public DecryptedMethodInfo(int id, int size) {
			this.id = id;
			this.data = new byte[size];
		}

		public DecryptedMethodInfo(int id, byte[] data) {
			this.id = id;
			this.data = data;
		}

		public override string ToString() {
			return string.Format("ID: {0}, Size: 0x{1:X}", id, data.Length);
		}
	}
}
