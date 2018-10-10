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

namespace de4dot.code.deobfuscators.Spices_Net {
	class QclzDecompressor : QuickLZBase {
		static int SPICES_QCLZ_SIG = 0x3952534E;	// "9RSN"

		public static byte[] Decompress(byte[] data) {
			if (Read32(data, 0) == SPICES_QCLZ_SIG)
				return QuickLZ.Decompress(data, SPICES_QCLZ_SIG);

			int headerLength, decompressedLength/*, compressedLength*/;
			if ((data[0] & 2) != 0) {
				headerLength = 9;
				/*compressedLength = (int)*/Read32(data, 1);
				decompressedLength = (int)Read32(data, 5);
			}
			else {
				headerLength = 3;
				//compressedLength = data[1];
				decompressedLength = data[2];
			}

			bool isCompressed = (data[0] & 1) != 0;
			byte[] decompressed = new byte[decompressedLength];
			if (isCompressed)
				Decompress(data, headerLength, decompressed);
			else
				Copy(data, headerLength, decompressed, 0, decompressed.Length);

			return decompressed;
		}
	}
}
