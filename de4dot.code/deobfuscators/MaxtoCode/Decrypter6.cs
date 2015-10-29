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

namespace de4dot.code.deobfuscators.MaxtoCode {
	class Decrypter6 {
		readonly uint[] key;
		readonly byte[] gen1 = new byte[0x100];
		readonly byte[] gen2 = new byte[0x100];
		readonly byte[] gen3 = new byte[0x100];
		readonly byte[] gen4 = new byte[0x100];
		static readonly byte[] d1h = new byte[16] { 14, 4, 13, 21, 2, 15, 11, 8, 3, 10, 6, 12, 5, 9, 0, 7 };
		static readonly byte[] d1l = new byte[16] { 15, 1, 8, 14, 6, 11, 3, 4, 30, 7, 2, 13, 12, 0, 5, 10 };
		static readonly byte[] d2h = new byte[16] { 10, 0, 9, 14, 6, 3, 15, 5, 23, 13, 12, 7, 11, 4, 2, 8 };
		static readonly byte[] d2l = new byte[16] { 7, 13, 14, 3, 0, 6, 9, 10, 1, 2, 8, 5, 11, 12, 4, 15 };
		static readonly byte[] d3h = new byte[16] { 2, 12, 4, 1, 7, 10, 11, 6, 8, 5, 3, 15, 13, 0, 14, 9 };
		static readonly byte[] d3l = new byte[16] { 12, 1, 10, 15, 9, 2, 6, 8, 2, 13, 3, 4, 14, 7, 5, 11 };
		static readonly byte[] d4h = new byte[16] { 4, 11, 12, 14, 15, 0, 8, 13, 3, 12, 9, 7, 5, 10, 6, 1 };
		static readonly byte[] d4l = new byte[16] { 13, 2, 8, 14, 6, 7, 11, 1, 10, 9, 3, 14, 5, 0, 12, 7 };

		public static byte[] Decrypt(byte[] key, byte[] encrypted) {
			return new Decrypter6(key).Decrypt(encrypted);
		}

		Decrypter6(byte[] key) {
			if (key.Length != 32)
				throw new ArgumentException("Invalid key size", "key");
			this.key = new uint[8];
			Buffer.BlockCopy(key, 0, this.key, 0, key.Length);
			Initialize();
		}

		byte[] Decrypt(byte[] encrypted) {
			if ((encrypted.Length & 7) != 0)
				throw new ArgumentException("Invalid data length", "encrypted");
			var decrypted = new byte[encrypted.Length];

			int count = decrypted.Length / 8;
			for (int i = 0; i < count; i++) {
				uint x, y;
				Decrypt(BitConverter.ToUInt32(encrypted, i * 8), BitConverter.ToUInt32(encrypted, i * 8 + 4), out x, out y);
				for (int j = 1; j < 100; j++)
					Decrypt(x, y, out x, out y);
				WriteUInt32(decrypted, i * 8, x);
				WriteUInt32(decrypted, i * 8 + 4, y);
			}

			return decrypted;
		}

		static void WriteUInt32(byte[] data, int index, uint value) {
			data[index] = (byte)value;
			data[index + 1] = (byte)(value >> 8);
			data[index + 2] = (byte)(value >> 16);
			data[index + 3] = (byte)(value >> 24);
		}

		void Initialize() {
			for (int i = 0; i < 0x100; i++) {
				gen1[i] = (byte)((d1h[i / 16] << 4) | d1l[i & 0x0F]);
				gen2[i] = (byte)((d2h[i / 16] << 4) | d2l[i & 0x0F]);
				gen3[i] = (byte)((d3h[i / 16] << 4) | d3l[i & 0x0F]);
				gen4[i] = (byte)((d4h[i / 16] << 4) | d4l[i & 0x0F]);
			}
		}

		void Decrypt(uint i0, uint i1, out uint o0, out uint o1) {
			uint x = i0;
			uint y = Decrypt(x + key[0]);
			y ^= i1;
			x ^= Decrypt(y + key[1]);
			y ^= Decrypt(x + key[2]);
			x ^= Decrypt(y + key[3]);
			y ^= Decrypt(x + key[4]);
			x ^= Decrypt(y + key[5]);
			y ^= Decrypt(x + key[6]);
			x ^= Decrypt(y + key[7]);

			for (int i = 0; i < 3; i++) {
				y ^= Decrypt(x + key[7]);
				x ^= Decrypt(y + key[6]);
				y ^= Decrypt(x + key[5]);
				x ^= Decrypt(y + key[4]);
				y ^= Decrypt(x + key[3]);
				x ^= Decrypt(y + key[2]);
				y ^= Decrypt(x + key[1]);
				x ^= Decrypt(y + key[0]);
			}

			o0 = y;
			o1 = x;
		}

		uint Decrypt(uint val) {
			uint x = (uint)((gen1[(byte)(val >> 24)] << 24) |
				(gen2[(byte)(val >> 16)] << 16) |
				(gen3[(byte)(val >> 8)] << 8) |
				gen4[(byte)val]);
			return Ror(x, 21);
		}

		static uint Ror(uint val, int n) {
			return (val << (32 - n)) + (val >> n);
		}
	}
}
