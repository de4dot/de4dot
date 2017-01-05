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
	class CryptDecrypter {
		byte[] key;

		static readonly byte[] sbox = new byte[8 * 8 * 8] {
			14,  4, 13,  1,  2, 15, 11,  8,
			 3, 10,  6, 12,  5,  9,  0,  7,
			 0, 15,  7,  4, 14,  2, 13,  1,
			10,  6, 12, 11,  9,  5,  3,  8,
			 4,  1, 14,  8, 13,  6,  2, 11,
			15, 12,  9,  7,  3, 10,  5,  0,
			15, 12,  8,  2,  4,  9,  1,  7,
			 5, 11,  3, 14, 10,  0,  6, 13,
			15,  1,  8, 14,  6, 11,  3,  4,
			 9,  7,  2, 13, 12,  0,  5, 10,
			 3, 13,  4,  7, 15,  2,  8, 14,
			12,  0,  1, 10,  6,  9, 11,  5,
			 0, 14,  7, 11, 10,  4, 13,  1,
			 5,  8, 12,  6,  9,  3,  2, 15,
			13,  8, 10,  1,  3, 15,  4,  2,
			11,  6,  7, 12,  0,  5, 14,  9,
			10,  0,  9, 14,  6,  3, 15,  5,
			 1, 13, 12,  7, 11,  4,  2,  8,
			13,  7,  0,  9,  3,  4,  6, 10,
			 2,  8,  5, 14, 12, 11, 15,  1,
			13,  6,  4,  9,  8, 15,  3,  0,
			11,  1,  2, 12,  5, 10, 14,  7,
			 1, 10, 13,  0,  6,  9,  8,  7,
			 4, 15, 14,  3, 11,  5,  2, 12,
			 7, 13, 14,  3,  0,  6,  9, 10,
			 1,  2,  8,  5, 11, 12,  4, 15,
			13,  8, 11,  5,  6, 15,  0,  3,
			 4,  7,  2, 12,  1, 10, 14,  9,
			10,  6,  9,  0, 12, 11,  7, 13,
			15,  1,  3, 14,  5,  2,  8,  4,
			 3, 15,  0,  6, 10,  1, 13,  8,
			 9,  4,  5, 11, 12,  7,  2, 14,
			 2, 12,  4,  1,  7, 10, 11,  6,
			 8,  5,  3, 15, 13,  0, 14,  9,
			14, 11,  2, 12,  4,  7, 13,  1,
			 5,  0, 15, 10,  3,  9,  8,  6,
			 4,  2,  1, 11, 10, 13,  7,  8,
			15,  9, 12,  5,  6,  3,  0, 14,
			11,  8, 12,  7,  1, 14,  2, 13,
			 6, 15,  0,  9, 10,  4,  5,  3,
			12,  1, 10, 15,  9,  2,  6,  8,
			 0, 13,  3,  4, 14,  7,  5, 11,
			10, 15,  4,  2,  7, 12,  9,  5,
			 6,  1, 13, 14,  0, 11,  3,  8,
			 9, 14, 15,  5,  2,  8, 12,  3,
			 7,  0,  4, 10,  1, 13, 11,  6,
			 4,  3,  2, 12,  9,  5, 15, 10,
			11, 14,  1,  7,  6,  0,  8, 13,
			 4, 11,  2, 14, 15,  0,  8, 13,
			 3, 12,  9,  7,  5, 10,  6,  1,
			13,  0, 11,  7,  4,  9,  1, 10,
			14,  3,  5, 12,  2, 15,  8,  6,
			 1,  4, 11, 13, 12,  3,  7, 14,
			10, 15,  6,  8,  0,  5,  9,  2,
			 6, 11, 13,  8,  1,  4, 10,  7,
			 9,  5,  0, 15, 14,  2,  3, 12,
			13,  2,  8,  4,  6, 15, 11,  1,
			10,  9,  3, 14,  5,  0, 12,  7,
			 1, 15, 13,  8, 10,  3,  7,  4,
			12,  5,  6, 11,  0, 14,  9,  2,
			 7, 11,  4,  1,  9, 12, 14,  2,
			 0,  6, 10, 13, 15,  3,  5,  8,
			 2,  1, 14,  7,  4, 10,  8, 13,
			15, 12,  9,  0,  3,  5,  6, 11,
		};
		static readonly byte[] perm = new byte[32] {
			16,  7, 20, 21, 29, 12, 28, 17,
			 1, 15, 23, 26,  5, 18, 31, 10,
			 2,  8, 24, 14, 32, 27,  3,  9,
			19, 13, 30,  6, 22, 11,  4, 25,
		};
		static readonly byte[] esel = new byte[48] {
			32,  1,  2,  3,  4,  5,  4,  5,
			 6,  7,  8,  9,  8,  9, 10, 11,
			12, 13, 12, 13, 14, 15, 16, 17,
			16, 17, 18, 19, 20, 21, 20, 21,
			22, 23, 24, 25, 24, 25, 26, 27,
			28, 29, 28, 29, 30, 31, 32,  1,
		};
		static readonly byte[] ip = new byte[64] {
			58, 50, 42, 34, 26, 18, 10,  2,
			60, 52, 44, 36, 28, 20, 12,  4,
			62, 54, 46, 38, 30, 22, 14,  6,
			64, 56, 48, 40, 32, 24, 16,  8,
			57, 49, 41, 33, 25, 17,  9,  1,
			59, 51, 43, 35, 27, 19, 11,  3,
			61, 53, 45, 37, 29, 21, 13,  5,
			63, 55, 47, 39, 31, 23, 15,  7,
		};
		static readonly byte[] final = new byte[64] {
			40,  8, 48, 16, 56, 24, 64, 32,
			39,  7, 47, 15, 55, 23, 63, 31,
			38,  6, 46, 14, 54, 22, 62, 30,
			37,  5, 45, 13, 53, 21, 61, 29,
			36,  4, 44, 12, 52, 20, 60, 28,
			35,  3, 43, 11, 51, 19, 59, 27,
			34,  2, 42, 10, 50, 18, 58, 26,
			33,  1, 41,  9, 49, 17, 57, 25,
		};
		static readonly byte[] pc1 = new byte[56] {
			57, 49, 41, 33, 25, 17,  9,  1,
			58, 50, 42, 34, 26, 18, 10,  2,
			59, 51, 43, 35, 27, 19, 11,  3,
			60, 52, 44, 36, 63, 55, 47, 39,
			31, 23, 15,  7, 62, 54, 46, 38,
			30, 22, 14,  6, 61, 53, 45, 37,
			29, 21, 13,  5, 28, 20, 12,  4,
		};
		static readonly byte[] pc2 = new byte[48] {
			14, 17, 11, 24,  1,  5,  3, 28,
			15,  6, 21, 10, 23, 19, 12,  4,
			26,  8, 16,  7, 27, 20, 13,  2,
			41, 52, 31, 37, 47, 55, 30, 40,
			51, 45, 33, 48, 44, 49, 39, 56,
			34, 53, 46, 42, 50, 36, 29, 32,
		};
		static readonly byte[] rots = new byte[16] {
			1, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1,
		};

		struct Bits {
			readonly byte[] byteBits;

			public static Bits FromBytes(byte[] bytes) {
				return FromBytes(bytes, 0, bytes.Length * 8);
			}

			public static Bits FromBytes(byte[] bytes, int index, int numBits) {
				return new Bits(bytes, index, numBits);
			}

			public static Bits FromByteBits(byte[] byteBits1, byte[] byteBits2) {
				return new Bits(byteBits1, byteBits2);
			}

			public static Bits FromByteBits(byte[] byteBits) {
				return FromByteBits(byteBits, 0, byteBits.Length);
			}

			public static Bits FromByteBits(byte[] byteBits, int index, int numBits) {
				var bits = new Bits(numBits);
				for (int i = 0; i < numBits; i++)
					bits.byteBits[i] = byteBits[index + i];
				return bits;
			}

			public byte this[int index] {
				get { return byteBits[index]; }
			}

			public byte[] ByteBits {
				get { return byteBits; }
			}

			Bits(int numBits) {
				this.byteBits = new byte[numBits];
			}

			Bits(byte[] bytes1, byte[] bytes2) {
				this.byteBits = Concat(bytes1, bytes2);
			}

			Bits(byte[] bytes, int index, int numBits) {
				this.byteBits = ToByteBits(bytes, index, numBits);
			}

			static byte[] ToByteBits(byte[] bytes, int index, int numBits) {
				var byteBits = new byte[numBits];
				for (int i = 0; i < numBits; i++) {
					int j = i / 8;
					int k = i & 7;
					byteBits[i] = (byte)(((bytes[index + j] >> k) & 1) != 0 ? 1 : 0);
				}
				return byteBits;
			}

			static byte[] Concat(byte[] bytes1, byte[] bytes2) {
				var bytes = new byte[bytes1.Length + bytes2.Length];
				Array.Copy(bytes1, 0, bytes, 0, bytes1.Length);
				Array.Copy(bytes2, 0, bytes, bytes1.Length, bytes2.Length);
				return bytes;
			}

			public Bits Transpose(byte[] bits) {
				var result = new Bits(bits.Length);
				for (int i = 0; i < bits.Length; i++)
					result.byteBits[i] = byteBits[bits[i] - 1];
				return result;
			}

			public void Rol() {
				if (byteBits.Length == 0)
					return;
				var first = byteBits[0];
				for (int i = 1; i < byteBits.Length; i++)
					byteBits[i - 1] = byteBits[i];
				byteBits[byteBits.Length - 1] = first;
			}

			public void Rol(int num) {
				for (int i = 0; i < num; i++)
					Rol();
			}

			public Bits Extract(int index, int numBits) {
				return FromByteBits(byteBits, index, numBits);
			}

			public void ToBits(byte[] dest, int index) {
				var bits = ToBits();
				Array.Copy(bits, 0, dest, index, bits.Length);
			}

			public byte[] ToBits() {
				var bits = new byte[(byteBits.Length + 7) / 8];
				for (int i = 0; i < bits.Length; i++) {
					byte val = 0;
					for (int j = i * 8, k = 1; j < byteBits.Length; j++, k <<= 1) {
						if (byteBits[j] != 0)
							val |= (byte)k;
					}
					bits[i] = val;
				}
				return bits;
			}

			public Bits Clone() {
				return FromByteBits(byteBits, 0, byteBits.Length);
			}

			public void Set(int destIndex, Bits other) {
				for (int i = 0; i < other.byteBits.Length; i++)
					byteBits[destIndex + i] = other.byteBits[i];
			}

			public void Xor(Bits other) {
				if (byteBits.Length != other.byteBits.Length)
					throw new ArgumentException("other");
				for (int i = 0; i < byteBits.Length; i++)
					byteBits[i] ^= other.byteBits[i];
			}

			public void CopyTo(byte[] dest, int index) {
				for (int i = 0; i < byteBits.Length; i++)
					dest[index + i] = byteBits[i];
			}
		}

		public CryptDecrypter(byte[] key) {
			if (key.Length <= 8)
				throw new ArgumentException("Invalid size", "key");
			this.key = key;
		}

		public static byte[] Decrypt(byte[] key, byte[] encrypted) {
			return new CryptDecrypter(key).Decrypt(encrypted);
		}

		byte[] Decrypt(byte[] encrypted) {
			if (encrypted.Length % 8 != 0)
				throw new ArgumentException("encrypted");
			var key1 = CreateKey(key, 0);
			var key2 = CreateKey(key, 8);

			var decrypted = new byte[encrypted.Length];
			int count = encrypted.Length / 8;
			for (int i = 0; i < count; i++) {
				var buf = new byte[8];
				Array.Copy(encrypted, i * 8, buf, 0, buf.Length);
				buf = Decrypt(buf, key1, true);
				buf = Decrypt(buf, key2, false);
				buf = Decrypt(buf, key1, true);
				Array.Copy(buf, 0, decrypted, i * 8, buf.Length);
			}

			return decrypted;
		}

		byte[] Decrypt(byte[] data, Bits key, bool flag) {
			var bits = Bits.FromBytes(data).Transpose(ip);

			if (flag) {
				for (int i = 0, ki = key.ByteBits.Length - 48; i < 16; i++, ki -= 48) {
					var oldBits = bits.Extract(0, 32);
					var tmp = Decrypt(oldBits.Clone(), key.Extract(ki, 48));
					tmp.Xor(bits.Extract(32, 32));
					bits.Set(32, oldBits);
					bits.Set(0, tmp);
				}
			}
			else {
				for (int i = 0, ki = 0; i < 16; i++, ki += 48) {
					var oldBits = bits.Extract(32, 32);
					var tmp = Decrypt(oldBits.Clone(), key.Extract(ki, 48));
					tmp.Xor(bits.Extract(0, 32));
					bits.Set(0, oldBits);
					bits.Set(32, tmp);
				}
			}

			bits = bits.Transpose(final);
			return bits.ToBits();
		}

		Bits Decrypt(Bits data, Bits key) {
			var newData = data.Clone().Transpose(esel);
			newData.Xor(key);
			return Bits.FromByteBits(GetSbox(newData)).Transpose(perm);
		}

		byte[] GetSbox(Bits data) {
			var sboxByteBits = new byte[32];

			for (int i = 0; i < 8; i++) {
				int di = i * 6;
				int index = (data[di + 0] << 5) + (data[di + 5] << 4) + (data[di + 1] << 3) +
							(data[di + 2] << 2) + (data[di + 3] << 1) + data[di + 4] + i * 64;
				Bits.FromBytes(sbox, index, 4).CopyTo(sboxByteBits, i * 4);
			}

			return sboxByteBits;
		}

		static Bits CreateKey(byte[] data, int index) {
			Bits key1, key2;
			CreateKeys(data, index, out key1, out key2);
			byte[] newKey = new byte[16 * 6];
			//byte[] tmpData = new byte[28 * 2];
			for (int i = 0; i < 16; i++) {
				int rolCount = rots[i];
				key1.Rol(rolCount);
				key2.Rol(rolCount);
				Bits.FromByteBits(key1.ByteBits, key2.ByteBits).Transpose(pc2).ToBits(newKey, i * 6);
			}
			return Bits.FromBytes(newKey);
		}

		static void CreateKeys(byte[] data, int index, out Bits key1, out Bits key2) {
			var tmpKey = new byte[8];
			int len = Math.Min(tmpKey.Length, data.Length - index);
			if (len == 0)
				throw new ArgumentException("data");
			Array.Copy(data, index, tmpKey, 0, len);
			var bits = Bits.FromBytes(tmpKey).Transpose(pc1);
			key1 = Bits.FromByteBits(bits.ByteBits, 0, 28);
			key2 = Bits.FromByteBits(bits.ByteBits, 28, 28);
		}
	}
}
