/**************************   mother.cpp   ************************************
* Author:        Agner Fog
* Date created:  1999
* Last modified: 2008-11-16
* Project:       randomc.h
* Platform:      This implementation uses 64-bit integers for intermediate calculations.
*                Works only on compilers that support 64-bit integers.
* Description:
* Random Number generator of type 'Mother-Of-All generator'.
*
* This is a multiply-with-carry type of random number generator
* invented by George Marsaglia.  The algorithm is:             
* S = 2111111111*X[n-4] + 1492*X[n-3] + 1776*X[n-2] + 5115*X[n-1] + C
* X[n] = S modulo 2^32
* C = floor(S / 2^32)
*
* Further documentation:
* The file ran-instructions.pdf contains further documentation and 
* instructions.
*
* Copyright 1999-2008 by Agner Fog. 
* GNU General Public License http://www.gnu.org/licenses/gpl.html
******************************************************************************/

// Only the methods I need have been ported to C#...

namespace de4dot.code.deobfuscators.CodeWall.randomc {
	class CRandomMother {
		uint[] x = new uint[5];             // History buffer

		public CRandomMother(int seed) => RandomInit(seed);

		// this function initializes the random number generator:
		public void RandomInit(int seed) {
			int i;
			uint s = (uint)seed;
			// make random numbers and put them into the buffer
			for (i = 0; i < 5; i++) {
				s = s * 29943829 - 1;
				x[i] = s;
			}
			// randomize some more
			for (i = 0; i < 19; i++) BRandom();
		}

		// Output random bits
		public uint BRandom() {
			ulong sum;
			sum = (ulong)2111111111UL * (ulong)x[3] +
			   (ulong)1492 * (ulong)(x[2]) +
			   (ulong)1776 * (ulong)(x[1]) +
			   (ulong)5115 * (ulong)(x[0]) +
			   (ulong)x[4];
			x[3] = x[2]; x[2] = x[1]; x[1] = x[0];
			x[4] = (uint)(sum >> 32);			// Carry
			x[0] = (uint)sum;					// Low 32 bits of sum
			return x[0];
		}

		// returns a random number between 0 and 1:
		public double Random() => (double)BRandom() * (1.0 / (65536.0 * 65536.0));
	}
}
