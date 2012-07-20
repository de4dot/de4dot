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

namespace de4dot.code.deobfuscators {
	[Serializable]
	class InvalidMethodBody : Exception {
		public InvalidMethodBody() {
		}

		public InvalidMethodBody(string msg)
			: base(msg) {
		}
	}

	class MethodBodyHeader {
		public ushort flags;
		public ushort maxStack;
		public uint codeSize;
		public uint localVarSigTok;
	}

	static class MethodBodyParser {
		public static MethodBodyHeader parseMethodBody(BinaryReader reader, out byte[] code, out byte[] extraSections) {
			try {
				return parseMethodBody2(reader, out code, out extraSections);
			}
			catch (IOException) {
				throw new InvalidMethodBody();
			}
		}

		public static bool verify(byte[] data) {
			return verify(new BinaryReader(new MemoryStream(data)));
		}

		public static bool verify(Stream data) {
			return verify(new BinaryReader(data));
		}

		public static bool verify(BinaryReader reader) {
			try {
				byte[] code, extraSections;
				parseMethodBody(reader, out code, out extraSections);
				return true;
			}
			catch (InvalidMethodBody) {
				return false;
			}
		}

		static MethodBodyHeader parseMethodBody2(BinaryReader reader, out byte[] code, out byte[] extraSections) {
			var mbHeader = new MethodBodyHeader();

			uint codeOffset;
			switch (peek(reader) & 3) {
			case 2:
				mbHeader.flags = 2;
				mbHeader.maxStack = 8;
				mbHeader.codeSize = (uint)(reader.ReadByte() >> 2);
				mbHeader.localVarSigTok = 0;
				codeOffset = 1;
				break;

			case 3:
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
				break;

			default:
				throw new InvalidMethodBody();
			}

			if (mbHeader.codeSize + codeOffset > reader.BaseStream.Length)
				throw new InvalidMethodBody();
			code = reader.ReadBytes((int)mbHeader.codeSize);

			if ((mbHeader.flags & 8) != 0)
				extraSections = readExtraSections2(reader);
			else
				extraSections = null;

			return mbHeader;
		}

		static void align(BinaryReader reader, int alignment) {
			reader.BaseStream.Position = (reader.BaseStream.Position + alignment - 1) & ~(alignment - 1);
		}

		public static byte[] readExtraSections(BinaryReader reader) {
			try {
				return readExtraSections2(reader);
			}
			catch (IOException) {
				throw new InvalidMethodBody();
			}
		}

		static byte[] readExtraSections2(BinaryReader reader) {
			align(reader, 4);
			int startPos = (int)reader.BaseStream.Position;
			parseSection(reader);
			int size = (int)reader.BaseStream.Position - startPos;
			reader.BaseStream.Position = startPos;
			return reader.ReadBytes(size);
		}

		static void parseSection(BinaryReader reader) {
			byte flags;
			do {
				align(reader, 4);

				flags = reader.ReadByte();
				if ((flags & 1) == 0)
					throw new InvalidMethodBody("Not an exception section");
				if ((flags & 0x3E) != 0)
					throw new InvalidMethodBody("Invalid bits set");

				if ((flags & 0x40) != 0) {
					reader.BaseStream.Position--;
					int num = (int)(reader.ReadUInt32() >> 8) / 24;
					reader.BaseStream.Position += num * 24;
				}
				else {
					int num = reader.ReadByte() / 12;
					reader.BaseStream.Position += 2 + num * 12;
				}
			} while ((flags & 0x80) != 0);
		}

		static byte peek(BinaryReader reader) {
			byte b = reader.ReadByte();
			reader.BaseStream.Position--;
			return b;
		}
	}
}
