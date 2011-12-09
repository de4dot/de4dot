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

namespace de4dot.code.deobfuscators.dotNET_Reactor {
	static class QuickLZ {
		static uint read32(byte[] data, int index) {
			return BitConverter.ToUInt32(data, index);
		}

		// Can't use Array.Copy() when data overlaps so here's one that works
		static void copy(byte[] src, int srcIndex, byte[] dst, int dstIndex, int size) {
			for (int i = 0; i < size; i++)
				dst[dstIndex++] = src[srcIndex++];
		}

		static int[] indexInc = new int[] { 4, 0, 1, 0, 2, 0, 1, 0, 3, 0, 1, 0, 2, 0, 1, 0 };
		public static byte[] decompress(byte[] inData) {
			const int sig = 0x5A4C4351;	// "QCLZ"

			int mode = BitConverter.ToInt32(inData, 4);
			int compressedLength = BitConverter.ToInt32(inData, 8);
			int decompressedLength = BitConverter.ToInt32(inData, 12);
			bool isCompressed = BitConverter.ToInt32(inData, 16) == 1;
			int headerLength = 32;
			if (BitConverter.ToInt32(inData, 0) != sig || BitConverter.ToInt32(inData, compressedLength - 4) != sig)
				throw new ApplicationException("No QCLZ sig");

			byte[] outData = new byte[decompressedLength];

			if (!isCompressed) {
				copy(inData, headerLength, outData, 0, decompressedLength);
				return outData;
			}

			int inIndex = headerLength;
			int outIndex = 0;
			uint val1 = 1;
			uint count;
			int size;

			while (true) {
				if (val1 == 1) {
					val1 = read32(inData, inIndex);
					inIndex += 4;
				}
				uint val2 = read32(inData, inIndex);
				if ((val1 & 1) == 1) {
					val1 >>= 1;
					if ((val2 & 3) == 0) {
						count = (val2 & 0xFF) >> 2;
						copy(outData, (int)(outIndex - count), outData, outIndex, 3);
						outIndex += 3;
						inIndex++;
					}
					else if ((val2 & 2) == 0) {
						count = (val2 & 0xFFFF) >> 2;
						copy(outData, (int)(outIndex - count), outData, outIndex, 3);
						outIndex += 3;
						inIndex += 2;
					}
					else if ((val2 & 1) == 0) {
						size = (int)((val2 >> 2) & 0x0F) + 3;
						count = (val2 & 0xFFFF) >> 6;
						copy(outData, (int)(outIndex - count), outData, outIndex, size);
						outIndex += size;
						inIndex += 2;
					}
					else if ((val2 & 4) == 0) {
						size = (int)((val2 >> 3) & 0x1F) + 3;
						count = (val2 & 0xFFFFFF) >> 8;
						copy(outData, (int)(outIndex - count), outData, outIndex, size);
						outIndex += size;
						inIndex += 3;
					}
					else if ((val2 & 8) == 0) {
						size = (int)((val2 >> 4) & 0x07FF) + 3;
						count = val2 >> 15;
						copy(outData, (int)(outIndex - count), outData, outIndex, size);
						outIndex += size;
						inIndex += 4;
					}
					else {
						byte b = (byte)(val2 >> 16);
						size = (int)(val2 >> 4) & 0x0FFF;
						for (int i = 0; i < size; i++)
							outData[outIndex++] = b;
						inIndex += 3;
					}
				}
				else {
					copy(inData, inIndex, outData, outIndex, 4);
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

			return outData;
		}
	}
}
