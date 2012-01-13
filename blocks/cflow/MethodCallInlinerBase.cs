/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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

namespace de4dot.blocks.cflow {
	public abstract class MethodCallInlinerBase : IMethodCallInliner {
		// We can't catch all infinite loops, so inline methods at most this many times
		const int MAX_ITERATIONS = 10;

		protected Blocks blocks;
		protected Block block;
		int iteration;

		public void init(Blocks blocks, Block block) {
			this.blocks = blocks;
			this.block = block;
			this.iteration = 0;
		}

		public bool deobfuscate() {
			if (iteration++ >= MAX_ITERATIONS)
				return false;

			return deobfuscateInternal();
		}

		protected abstract bool deobfuscateInternal();
	}
}
