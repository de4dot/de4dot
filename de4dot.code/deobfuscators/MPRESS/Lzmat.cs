/*
***************************************************************************
** LZMAT ANSI-C decoder 1.01
** Copyright (C) 2007,2008 Vitaly Evseenko. All Rights Reserved.
** lzmat_dec.c
**
** This file is part of the LZMAT real-time data compression library.
**
** The LZMAT library is free software; you can redistribute it and/or
** modify it under the terms of the GNU General Public License as
** published by the Free Software Foundation; either version 2 of
** the License, or (at your option) any later version.
**
** The LZMAT library is distributed WITHOUT ANY WARRANTY;
** without even the implied warranty of MERCHANTABILITY or
** FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public
** License for more details.
**
** You should have received a copy of the GNU General Public License
** along with the LZMAT library; see the file GPL.TXT.
** If not, write to the Free Software Foundation, Inc.,
** 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301, USA.
**
** Vitaly Evseenko
** <ve@matcode.com>
** http://www.matcode.com/lzmat.htm
***************************************************************************
*/

using System;

namespace de4dot.code.deobfuscators.MPRESS {
	enum LzmatStatus {
		OK = 0,
		ERROR = -1,
		INTEGRITY_FAILURE = 0x100,
		BUFFER_TOO_SMALL = 0x110,
	}

	static class Lzmat {
		const int LZMAT_DEFAULT_CNT = 0x12;
		const int LZMAT_1BYTE_CNT = (0xFF + LZMAT_DEFAULT_CNT);
		const int LZMAT_2BYTE_CNT = (0xFFFF + LZMAT_1BYTE_CNT);
		const int LZMAT_MAX_2BYTE_CNT = (LZMAT_2BYTE_CNT - 1);

		const int MAX_LZMAT_SHORT_DIST0 = 0x80;
		const int MAX_LZMAT_SHORT_DIST1 = (0x800 | MAX_LZMAT_SHORT_DIST0);
		const int MAX_LZMAT_LONG_DIST0 = 0x40;
		const int MAX_LZMAT_LONG_DIST1 = (0x400 | MAX_LZMAT_LONG_DIST0);
		const int MAX_LZMAT_LONG_DIST2 = (0x4000 | MAX_LZMAT_LONG_DIST1);
		const int MAX_LZMAT_LONG_DIST3 = (0x40000 | MAX_LZMAT_LONG_DIST2);
		const int MAX_LZMAT_GAMMA_DIST = (MAX_LZMAT_LONG_DIST3 - 1);

		const int LZMAT_DIST_MSK0 = 0x3F;
		const int LZMAT_DIST_MSK1 = 0x3FF;

		static uint LZMAT_GET_U4(byte[] _p_, ref uint _i_, ref byte _n_) => (_n_ ^= 1) != 0 ? (uint)(_p_[_i_] & 0xF) : (uint)(_p_[_i_++] >> 4);
		static byte LZMAT_GET_U8(byte[] _p_, uint _i_, byte _n_) => (byte)(((_n_) != 0 ? ((_p_[_i_] >> 4) | (_p_[_i_ + 1] << 4)) : _p_[_i_]));
		static ushort LZMAT_GET_LE16(byte[] _p_, uint _i_, byte _n_) => (ushort)((_n_) != 0 ? ((_p_[_i_] >> 4) | ((ushort)(GET_LE16(_p_, _i_ + 1)) << 4)) : GET_LE16(_p_, _i_));
		static ushort GET_LE16(byte[] _p_, uint _i_) => BitConverter.ToUInt16(_p_, (int)_i_);

		public static LzmatStatus Decompress(byte[] pbOut, out uint pcbOut, byte[] pbIn) {
			pcbOut = 0;
			uint cbIn = (uint)pbIn.Length;

	uint  inPos, outPos;
	uint  cbOutBuf = (uint)pbOut.Length;
	byte  cur_nib;
	pbOut[0] = pbIn[0];
	for(inPos=1, outPos=1, cur_nib=0; inPos<(cbIn-cur_nib);)
	{
		int bc;
		byte tag;
		tag = LZMAT_GET_U8(pbIn,inPos,cur_nib);
		inPos++;
		for(bc=0; bc<8 && inPos<(cbIn-cur_nib) && outPos<cbOutBuf; bc++, tag<<=1)
		{
			if((tag&0x80)!=0) // gamma
			{
				uint r_pos, r_cnt, dist;
//#define cflag	r_cnt
				r_cnt = LZMAT_GET_LE16(pbIn,inPos,cur_nib);
				inPos++;
				if(outPos>MAX_LZMAT_SHORT_DIST1)
				{
					dist = r_cnt>>2;
					switch(r_cnt&3)
					{
					case 0:
						dist=(dist&LZMAT_DIST_MSK0)+1;
						break;
					case 1:
						inPos+=cur_nib;
						dist = (dist&LZMAT_DIST_MSK1)+0x41;
						cur_nib^=1;
						break;
					case 2:
						inPos++;
						dist += 0x441;
						break;
					case 3:
						if((inPos+2+cur_nib)>cbIn)
							return LzmatStatus.INTEGRITY_FAILURE+1;
						inPos++;
						dist = (dist + 
							((uint)LZMAT_GET_U4(pbIn,ref inPos,ref cur_nib)<<14))
							+0x4441;
						break;
					}
				}
				else
				{
					dist = r_cnt>>1;
					if((r_cnt&1)!=0)
					{
						inPos+=cur_nib;
						dist = (dist&0x7FF)+0x81;
						cur_nib^=1;
					}
					else
						dist = (dist&0x7F)+1;
				}
//#undef cflag
				r_cnt = LZMAT_GET_U4(pbIn,ref inPos,ref cur_nib);
				if(r_cnt!=0xF)
				{
					r_cnt += 3;
				}
				else
				{
					if((inPos+1+cur_nib)>cbIn)
						return LzmatStatus.INTEGRITY_FAILURE+2;
					r_cnt = LZMAT_GET_U8(pbIn,inPos,cur_nib);
					inPos++;
					if(r_cnt!=0xFF)
					{
						r_cnt += LZMAT_DEFAULT_CNT;
					}
					else
					{
						if((inPos+2+cur_nib)>cbIn)
							return LzmatStatus.INTEGRITY_FAILURE+3;
						r_cnt = (uint)(LZMAT_GET_LE16(pbIn,inPos,cur_nib)+LZMAT_1BYTE_CNT);
						inPos+=2;
						if(r_cnt==LZMAT_2BYTE_CNT)
						{
							// copy chunk
							if(cur_nib!=0)
							{
								r_cnt = ((uint)pbIn[inPos-4]&0xFC)<<5;
								inPos++;
								cur_nib = 0;
							}
							else
							{
								r_cnt = (uint)((GET_LE16(pbIn,inPos-5)&0xFC0)<<1);
							}
							r_cnt+=(uint)((tag&0x7F)+4);
							r_cnt<<=1;
							if((outPos+(r_cnt<<2))>cbOutBuf)
								return LzmatStatus.BUFFER_TOO_SMALL;
							while(r_cnt--!=0 && outPos<cbOutBuf)
							{
								pbOut[outPos] = pbIn[inPos];
								pbOut[outPos + 1] = pbIn[inPos + 1];
								pbOut[outPos + 2] = pbIn[inPos + 2];
								pbOut[outPos + 3] = pbIn[inPos + 3];
								inPos+=4;
								outPos+=4;
							}
							break;
						}
					}
				}
				if(outPos<dist)
					return LzmatStatus.INTEGRITY_FAILURE+4;
				if((outPos+r_cnt)>cbOutBuf)
					return LzmatStatus.BUFFER_TOO_SMALL+1;
				r_pos = outPos-dist;
				while(r_cnt--!=0 && outPos<cbOutBuf)
					pbOut[outPos++]=pbOut[r_pos++];
			}
			else
			{
				pbOut[outPos++]=LZMAT_GET_U8(pbIn,inPos,cur_nib);
				inPos++;
			}
		}
	}
	pcbOut = outPos;
	return LzmatStatus.OK;
		}

		public static byte[] DecompressOld(byte[] compressed) {
			int srcIndex = 3;
			int dstIndex = 0;
			int decompressedLen = compressed[0] + (compressed[1] << 8) + (compressed[2] << 16);
			byte[] decompressed = new byte[decompressedLen];
			while (dstIndex < decompressedLen) {
				int partLen = compressed[srcIndex++] + (compressed[srcIndex++] << 8) + (compressed[srcIndex++] << 16);
				if (partLen < 0x800000) {
					Array.Copy(compressed, srcIndex, decompressed, dstIndex, partLen);
					srcIndex += partLen;
					dstIndex += partLen;
				}
				else {
					partLen &= 0x7FFFFF;
					int decompressedLen2 = Lzmat_old(decompressed, dstIndex, decompressedLen - dstIndex, compressed, srcIndex, partLen);
					if (decompressedLen2 == 0)
						return null;
					dstIndex += decompressedLen2;
					srcIndex += partLen;
				}
			}
			return decompressed;
		}

		static int Lzmat_old(byte[] outBuf, int outIndex, int outLen, byte[] inBuf, int inIndex, int inLen) {
			int inPos = 0;
			int outPos = 0;
			while (inPos < inLen) {
				byte tag = inBuf[inIndex + inPos++];
				for (int bc = 0; bc < 8 && inPos < inLen && outPos < outLen; bc++, tag <<= 1) {
					if ((tag & 0x80) != 0) {
						ushort outPosDispl = (ushort)((((inBuf[inIndex + inPos + 1]) & 0xF) << 8) + inBuf[inIndex + inPos]);
						inPos++;
						int r_cnt = (inBuf[inIndex + inPos++] >> 4) + 3;
						if (outPosDispl == 0)
							outPosDispl = 0x1000;
						if (outPosDispl > outPos)
							return 0;
						if (r_cnt == 18) {
							if (inPos >= inLen)
								return 0;
							r_cnt = inBuf[inIndex + inPos++] + 18;
						}
						if (r_cnt == 0x111) {
							if (inPos + 2 > inLen)
								return 0;
							r_cnt = (inBuf[inIndex + inPos + 1] << 8) + inBuf[inIndex + inPos] + 0x111;
							inPos += 2;
						}
						int outPos2 = outPos - outPosDispl;
						while (r_cnt-- > 0 && outPos < outLen)
							outBuf[outIndex + outPos++] = outBuf[outIndex + outPos2++];
					}
					else
						outBuf[outIndex + outPos++] = inBuf[inIndex + inPos++];
				}
			}
			if (inPos < inLen)
				return 0;
			return outPos;
		}
	}
}
