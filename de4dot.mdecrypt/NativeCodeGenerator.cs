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
using System.Collections.Generic;
using System.IO;

namespace de4dot.mdecrypt {
	class NativeCodeGenerator {
		MemoryStream memStream;
		BinaryWriter writer;
		Dictionary<int, IntPtr> offsetToBranchAddr = new Dictionary<int, IntPtr>();

		public int Size => (int)memStream.Length;

		public NativeCodeGenerator() {
			memStream = new MemoryStream(0x200);
			writer = new BinaryWriter(memStream);
		}

		public void WriteByte(byte b) => writer.Write(b);

		public void WriteBytes(byte b1, byte b2) {
			WriteByte(b1);
			WriteByte(b2);
		}

		public void WriteBytes(byte b, ushort us) {
			WriteByte(b);
			WriteWord(us);
		}

		public void WriteWord(ushort w) => writer.Write(w);
		public void WriteDword(uint d) => writer.Write(d);
		public void WriteBytes(byte[] bytes) => writer.Write(bytes);

		public void WriteCall(IntPtr addr) {
			WriteByte(0xE8);
			WriteBranchAddr(addr);
		}

		public void WriteBranchAddr(IntPtr addr) {
			offsetToBranchAddr.Add((int)memStream.Position, addr);
			writer.Write(0);
		}

		public byte[] GetCode(IntPtr addr) {
			FixOffsets(addr);
			memStream.Position = memStream.Length;
			return memStream.ToArray();
		}

		unsafe void FixOffsets(IntPtr destAddr) {
			foreach (var kv in offsetToBranchAddr) {
				memStream.Position = kv.Key;
				// kv.Value (func/label) = destAddr + kv.Key + 4 + displ
				var displ = (ulong)((byte*)kv.Value - (byte*)destAddr - kv.Key - 4);
				uint high = (uint)(displ >> 32);
				if (high != 0 && high != 0xFFFFFFFF)
					throw new ApplicationException("Invalid displ");
				writer.Write((uint)displ);
			}
		}
	}
}
