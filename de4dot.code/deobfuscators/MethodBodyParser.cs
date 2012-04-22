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
	static class MethodBodyParser {
		public static void parseMethodBody(BinaryReader reader, out byte[] code, out byte[] extraSections) {
			long methodBodyOffset = reader.BaseStream.Position;

			uint codeOffset;
			int codeSize;
			ushort flags;
			switch (peek(reader) & 3) {
			case 2:
				flags = 2;
				codeOffset = 1;
				codeSize = reader.ReadByte() >> 2;
				break;

			case 3:
				flags = reader.ReadUInt16();
				codeOffset = (uint)(4 * (flags >> 12));
				reader.ReadUInt16();	// maxStack
				codeSize = reader.ReadInt32();
				reader.ReadUInt32();	// LocalVarSigTok
				break;

			default:
				throw new ApplicationException("Invalid method body header");
			}

			code = reader.ReadBytes(codeSize);

			if ((flags & 8) != 0)
				extraSections = readExtraSections(reader);
			else
				extraSections = null;
		}

		static void align(BinaryReader reader, int alignment) {
			reader.BaseStream.Position = (reader.BaseStream.Position + alignment - 1) & ~(alignment - 1);
		}

		public static byte[] readExtraSections(BinaryReader reader) {
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
					throw new ApplicationException("Not an exception section");
				if ((flags & 0x3E) != 0)
					throw new ApplicationException("Invalid bits set");

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
