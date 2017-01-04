// QuickLZ data compression library
// Copyright (C) 2006-2011 Lasse Mikkel Reinhold
// lar@quicklz.com
//
// QuickLZ can be used for free under the GPL 1, 2 or 3 license (where anything 
// released into public must be open source) or under a commercial license if such 
// has been acquired (see http://www.quicklz.com/order.html). The commercial license 
// does not cover derived or ported versions created by third parties under GPL.

// Port of QuickLZ to C# by de4dot@gmail.com. This code is most likely not working now.

using System;

namespace de4dot.code.deobfuscators {
	public class QuickLZBase {
		protected static uint Read32(byte[] data, int index) {
			return BitConverter.ToUInt32(data, index);
		}

		// Can't use Array.Copy() when data overlaps so here's one that works
		protected static void Copy(byte[] src, int srcIndex, byte[] dst, int dstIndex, int size) {
			for (int i = 0; i < size; i++)
				dst[dstIndex++] = src[srcIndex++];
		}

		static int[] indexInc = new int[] { 4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0 };
		public static void Decompress(byte[] inData, int inIndex, byte[] outData) {
			int decompressedLength = outData.Length;
			int outIndex = 0;
			uint val1 = 1;
			uint count;
			int size;

			while (true) {
				if (val1 == 1) {
					val1 = Read32(inData, inIndex);
					inIndex += 4;
				}
				uint val2 = Read32(inData, inIndex);
				if ((val1 & 1) == 1) {
					val1 >>= 1;
					if ((val2 & 3) == 0) {
						count = (val2 & 0xFF) >> 2;
						Copy(outData, (int)(outIndex - count), outData, outIndex, 3);
						outIndex += 3;
						inIndex++;
					}
					else if ((val2 & 2) == 0) {
						count = (val2 & 0xFFFF) >> 2;
						Copy(outData, (int)(outIndex - count), outData, outIndex, 3);
						outIndex += 3;
						inIndex += 2;
					}
					else if ((val2 & 1) == 0) {
						size = (int)((val2 >> 2) & 0x0F) + 3;
						count = (val2 & 0xFFFF) >> 6;
						Copy(outData, (int)(outIndex - count), outData, outIndex, size);
						outIndex += size;
						inIndex += 2;
					}
					else if ((val2 & 4) == 0) {
						size = (int)((val2 >> 3) & 0x1F) + 3;
						count = (val2 & 0xFFFFFF) >> 8;
						Copy(outData, (int)(outIndex - count), outData, outIndex, size);
						outIndex += size;
						inIndex += 3;
					}
					else if ((val2 & 8) == 0) {
						count = val2 >> 15;
						if (count != 0) {
							size = (int)((val2 >> 4) & 0x07FF) + 3;
							inIndex += 4;
						}
						else {
							size = (int)Read32(inData, inIndex + 4);
							count = Read32(inData, inIndex + 8);
							inIndex += 12;
						}
						Copy(outData, (int)(outIndex - count), outData, outIndex, size);
						outIndex += size;
					}
					else {
						byte b = (byte)(val2 >> 16);
						size = (int)(val2 >> 4) & 0x0FFF;
						if (size == 0) {
							size = (int)Read32(inData, inIndex + 3);
							inIndex += 7;
						}
						else
							inIndex += 3;
						for (int i = 0; i < size; i++)
							outData[outIndex++] = b;
					}
				}
				else {
					Copy(inData, inIndex, outData, outIndex, 4);
					int index = (int)(val1 & 0x0F);
					outIndex += indexInc[index];
					inIndex += indexInc[index];
					val1 >>= indexInc[index];
					if (outIndex >= decompressedLength - 4)
						break;
				}
			}
			while (outIndex < decompressedLength) {
				if (val1 == 1) {
					inIndex += 4;
					val1 = 0x80000000;
				}
				outData[outIndex++] = inData[inIndex++];
				val1 >>= 1;
			}
		}
	}

	public class QuickLZ : QuickLZBase {
		static int DEFAULT_QCLZ_SIG = 0x5A4C4351;	// "QCLZ"

		public static bool IsCompressed(byte[] data) {
			if (data.Length < 4)
				return false;
			return BitConverter.ToInt32(data, 0) == DEFAULT_QCLZ_SIG;
		}

		public static byte[] Decompress(byte[] inData) {
			return Decompress(inData, DEFAULT_QCLZ_SIG);
		}

		public static byte[] Decompress(byte[] inData, int sig) {
			/*int mode =*/ BitConverter.ToInt32(inData, 4);
			int compressedLength = BitConverter.ToInt32(inData, 8);
			int decompressedLength = BitConverter.ToInt32(inData, 12);
			bool isDataCompressed = BitConverter.ToInt32(inData, 16) == 1;
			int headerLength = 32;
			if (BitConverter.ToInt32(inData, 0) != sig || BitConverter.ToInt32(inData, compressedLength - 4) != sig)
				throw new ApplicationException("No QCLZ sig");

			byte[] outData = new byte[decompressedLength];

			if (!isDataCompressed) {
				Copy(inData, headerLength, outData, 0, decompressedLength);
				return outData;
			}

			Decompress(inData, headerLength, outData);
			return outData;
		}
	}
}
