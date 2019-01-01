/**************************   mersenne.cpp   **********************************
* Author:        Agner Fog
* Date created:  2001
* Last modified: 2008-11-16
* Project:       randomc.h
* Platform:      Any C++
* Description:
* Random Number generator of type 'Mersenne Twister'
*
* This random number generator is described in the article by
* M. Matsumoto & T. Nishimura, in:
* ACM Transactions on Modeling and Computer Simulation,
* vol. 8, no. 1, 1998, pp. 3-30.
* Details on the initialization scheme can be found at
* http://www.math.sci.hiroshima-u.ac.jp/~m-mat/MT/emt.html
*
* Further documentation:
* The file ran-instructions.pdf contains further documentation and 
* instructions.
*
* Copyright 2001-2008 by Agner Fog. 
* GNU General Public License http://www.gnu.org/licenses/gpl.html
*******************************************************************************/

// Only the methods I need have been ported to C#...

namespace de4dot.code.deobfuscators.CodeWall.randomc {
	class CRandomMersenne {
		const int MERS_N = 624;
		const int MERS_M = 397;
		const int MERS_R = 31;
		const int MERS_U = 11;
		const int MERS_S = 7;
		const int MERS_T = 15;
		const int MERS_L = 18;
		const uint MERS_A = 0x9908B0DF;
		const uint MERS_B = 0x9D2C5680;
		const uint MERS_C = 0xEFC60000;

		uint[] mt = new uint[MERS_N];	// State vector
		int mti;						// Index into mt

		public CRandomMersenne() {
		}

		public CRandomMersenne(int seed) => RandomInit(seed);

		void Init0(int seed) {
			// Seed generator
			const uint factor = 1812433253;
			mt[0] = (uint)seed;
			for (mti = 1; mti < MERS_N; mti++) {
				mt[mti] = (factor * (mt[mti - 1] ^ (mt[mti - 1] >> 30)) + (uint)mti);
			}
		}

		public void RandomInit(int seed) {
			// Initialize and seed
			Init0(seed);

			// Randomize some more
			for (int i = 0; i < 37; i++) BRandom();
		}

		static uint[] mag01 = new uint[2] { 0, MERS_A };
		public uint BRandom() {
			// Generate 32 random bits
			uint y;

			if (mti >= MERS_N) {
				// Generate MERS_N words at one time
				const uint LOWER_MASK = (1U << MERS_R) - 1;		// Lower MERS_R bits
				const uint UPPER_MASK = 0xFFFFFFFF << MERS_R;	// Upper (32 - MERS_R) bits

				int kk;
				for (kk = 0; kk < MERS_N - MERS_M; kk++) {
					y = (mt[kk] & UPPER_MASK) | (mt[kk + 1] & LOWER_MASK);
					mt[kk] = mt[kk + MERS_M] ^ (y >> 1) ^ mag01[y & 1];
				}

				for (; kk < MERS_N - 1; kk++) {
					y = (mt[kk] & UPPER_MASK) | (mt[kk + 1] & LOWER_MASK);
					mt[kk] = mt[kk + (MERS_M - MERS_N)] ^ (y >> 1) ^ mag01[y & 1];
				}

				y = (mt[MERS_N - 1] & UPPER_MASK) | (mt[0] & LOWER_MASK);
				mt[MERS_N - 1] = mt[MERS_M - 1] ^ (y >> 1) ^ mag01[y & 1];
				mti = 0;
			}
			y = mt[mti++];

			// Tempering (May be omitted):
			y ^= y >> MERS_U;
			y ^= (y << MERS_S) & MERS_B;
			y ^= (y << MERS_T) & MERS_C;
			y ^= y >> MERS_L;

			return y;
		}
	}
}
