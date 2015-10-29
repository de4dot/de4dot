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
using System.IO;
using dnlib.IO;

namespace de4dot.code.deobfuscators {
	[Serializable]
	public class InvalidMethodBody : Exception {
		public InvalidMethodBody() {
		}

		public InvalidMethodBody(string msg)
			: base(msg) {
		}
	}

	public class MethodBodyHeader {
		public ushort flags;
		public ushort maxStack;
		public uint codeSize;
		public uint localVarSigTok;
	}

	public static class MethodBodyParser {
		public static MethodBodyHeader ParseMethodBody(IBinaryReader reader, out byte[] code, out byte[] extraSections) {
			try {
				return ParseMethodBody2(reader, out code, out extraSections);
			}
			catch (IOException) {
				throw new InvalidMethodBody();
			}
		}

		public static bool Verify(byte[] data) {
			return Verify(MemoryImageStream.Create(data));
		}

		public static bool Verify(IBinaryReader reader) {
			try {
				byte[] code, extraSections;
				ParseMethodBody(reader, out code, out extraSections);
				return true;
			}
			catch (InvalidMethodBody) {
				return false;
			}
		}

		static MethodBodyHeader ParseMethodBody2(IBinaryReader reader, out byte[] code, out byte[] extraSections) {
			var mbHeader = new MethodBodyHeader();

			uint codeOffset;
			byte b = Peek(reader);
			if ((b & 3) == 2) {
				mbHeader.flags = 2;
				mbHeader.maxStack = 8;
				mbHeader.codeSize = (uint)(reader.ReadByte() >> 2);
				mbHeader.localVarSigTok = 0;
				codeOffset = 1;
			}
			else if ((b & 7) == 3) {
				mbHeader.flags = reader.ReadUInt16();
				codeOffset = (uint)(4 * (mbHeader.flags >> 12));
				if (codeOffset != 12)
					throw new InvalidMethodBody();
				mbHeader.maxStack = reader.ReadUInt16();
				mbHeader.codeSize = reader.ReadUInt32();
				if (mbHeader.codeSize > int.MaxValue)
					throw new InvalidMethodBody();
				mbHeader.localVarSigTok = reader.ReadUInt32();
				if (mbHeader.localVarSigTok != 0 && (mbHeader.localVarSigTok >> 24) != 0x11)
					throw new InvalidMethodBody();
			}
			else
				throw new InvalidMethodBody();

			if (mbHeader.codeSize + codeOffset > reader.Length)
				throw new InvalidMethodBody();
			code = reader.ReadBytes((int)mbHeader.codeSize);

			if ((mbHeader.flags & 8) != 0)
				extraSections = ReadExtraSections2(reader);
			else
				extraSections = null;

			return mbHeader;
		}

		static void Align(IBinaryReader reader, int alignment) {
			reader.Position = (reader.Position + alignment - 1) & ~(alignment - 1);
		}

		public static byte[] ReadExtraSections(IBinaryReader reader) {
			try {
				return ReadExtraSections2(reader);
			}
			catch (IOException) {
				throw new InvalidMethodBody();
			}
		}

		static byte[] ReadExtraSections2(IBinaryReader reader) {
			Align(reader, 4);
			int startPos = (int)reader.Position;
			ParseSection(reader);
			int size = (int)reader.Position - startPos;
			reader.Position = startPos;
			return reader.ReadBytes(size);
		}

		static void ParseSection(IBinaryReader reader) {
			byte flags;
			do {
				Align(reader, 4);

				flags = reader.ReadByte();
				if ((flags & 1) == 0)
					throw new InvalidMethodBody("Not an exception section");
				if ((flags & 0x3E) != 0)
					throw new InvalidMethodBody("Invalid bits set");

				if ((flags & 0x40) != 0) {
					reader.Position--;
					int num = (int)(reader.ReadUInt32() >> 8) / 24;
					reader.Position += num * 24;
				}
				else {
					int num = reader.ReadByte() / 12;
					reader.Position += 2 + num * 12;
				}
			} while ((flags & 0x80) != 0);
		}

		static byte Peek(IBinaryReader reader) {
			byte b = reader.ReadByte();
			reader.Position--;
			return b;
		}
	}
}
