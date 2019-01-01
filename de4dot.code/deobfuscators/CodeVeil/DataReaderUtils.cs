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

using System.Text;
using dnlib.IO;

namespace de4dot.code.deobfuscators.CodeVeil {
	static class DataReaderUtils {
		public static char[] ReadChars(ref DataReader reader, int length) {
			var chars = new char[length];
			for (int i = 0; i < length; i++)
				chars[i] = ReadChar(ref reader);
			return chars;
		}

		public static char ReadChar(ref DataReader reader) => ReadChar(ref reader, Encoding.UTF8);

		static char ReadChar(ref DataReader reader, Encoding encoding) {
			var decoder = encoding.GetDecoder();
			bool twoBytes = encoding is UnicodeEncoding;
			byte[] bytes = new byte[2];
			char[] chars = new char[1];
			while (true) {
				bytes[0] = reader.ReadByte();
				if (twoBytes)
					bytes[1] = reader.ReadByte();
				int x = decoder.GetChars(bytes, 0, twoBytes ? 2 : 1, chars, 0);
				if (x != 0)
					break;
			}
			return chars[0];
		}
	}
}
