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

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class NativeFileDecrypter {
		byte[] key;
		byte kb = 0;
		byte[,] transform = new byte[256, 256];

		public NativeFileDecrypter(byte[] keyData) {
			var keyInit = new byte[] {
				0x78, 0x61, 0x32, keyData[0], keyData[2],
				0x62, keyData[3], keyData[0], keyData[1], keyData[1],
				0x66, keyData[1], keyData[5], 0x33, keyData[2],
				keyData[4], 0x74, 0x32, keyData[3], keyData[2],
			};
			key = new byte[32];
			for (int i = 0; i < 32; i++) {
				key[i] = (byte)(i + keyInit[i % keyInit.Length] * keyInit[((i + 0x0B) | 0x1F) % keyInit.Length]);
				kb += key[i];
			}

			var transformTemp = new ushort[256, 256];
			for (int i = 0; i < 256; i++)
				for (int j = 0; j < 256; j++)
					transformTemp[i, j] = 0x400;
			int counter = 0x0B;
			byte newByte = 0;
			int ki = 0;
			for (int i = 0; i < 256; i++) {
				while (true) {
					for (int j = key.Length - 1; j >= ki; j--)
						newByte += (byte)(key[j] + counter);
					bool done = true;
					ki = (ki + 1) % key.Length;
					for (int k = 0; k <= i; k++) {
						if (newByte == transformTemp[k, 0]) {
							done = false;
							break;
						}
					}
					if (done)
						break;
					counter++;
				}
				transformTemp[i, 0] = newByte;
			}

			counter = ki = 0;
			for (int i = 1; i < 256; i++) {
				ki++;
				int i1;
				do {
					counter++;
					i1 = 1 + (key[(i + 37 + counter) % key.Length] + counter + kb) % 255;
				} while (transformTemp[0, i1] != 0x400);
				for (int i0 = 0; i0 < 256; i0++)
					transformTemp[i0, i1] = transformTemp[(i0 + ki) % 256, 0];
			}

			for (int i = 0; i < 256; i++) {
				for (int j = 0; j < 256; j++)
					transform[(byte)transformTemp[i, j], j] = (byte)i;
			}
		}

		public void Decrypt(byte[] data, int offset, int count) {
			for (int i = 0; i < count; i += 1024, offset += 1024) {
				int blockLen = Math.Min(1024, count - i);

				if (blockLen == 1) {
					data[offset] = transform[data[offset], kb];
					continue;
				}

				for (int j = 0; j < blockLen - 1; j++)
					data[offset + j] = transform[data[offset + j], data[offset + j + 1]];
				data[offset + blockLen - 1] = transform[data[offset + blockLen - 1], kb ^ 0x55];

				for (int j = blockLen - 1; j > 0; j--)
					data[offset + j] = transform[data[offset + j], data[offset + j - 1]];
				data[offset] = transform[data[offset], kb];
			}
		}
	}
}
