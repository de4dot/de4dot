/*
    Copyright (C) 2011 de4dot@gmail.com

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

using System.Collections.Generic;

namespace de4dot.blocks {
	class BlocksSorter {
		ScopeBlock scopeBlock;
		Dictionary<BaseBlock, bool> visited;
		List<BaseBlock> sorted;

		public BlocksSorter(ScopeBlock scopeBlock) {
			this.scopeBlock = scopeBlock;
		}

		bool hasVisited(BaseBlock bb) {
			bool hasVisited;
			if (visited.TryGetValue(bb, out hasVisited))
				return hasVisited;
			visited[bb] = false;
			return false;
		}

		public List<BaseBlock> sort() {
			visited = new Dictionary<BaseBlock, bool>();
			sorted = new List<BaseBlock>(scopeBlock.BaseBlocks.Count);

			if (scopeBlock.BaseBlocks.Count > 0)
				search(scopeBlock.BaseBlocks[0]);
			sorted.Reverse();	// It's in reverse order

			// Just in case there's dead code or unreferenced exception blocks
			foreach (var bb in scopeBlock.BaseBlocks) {
				if (hasVisited(bb))
					continue;
				sorted.Add(bb);
			}

			sorted = new ForwardScanOrder(scopeBlock, sorted).fix();

			return sorted;
		}

		// Depth-first order
		void search(BaseBlock bb) {
			if (hasVisited(bb))
				return;

			visited[bb] = true;
			var block = bb as Block;	// Block or ScopeBlock
			if (block != null) {
				// Since the sorted array will be in reverse order, and we want the
				// conditional branches to fall through to their fall-through target, make
				// sure the FallThrough target is added last! Some conditional instructions
				// aren't reversible (eg. beq and bne.un) since they don't take the same
				// types of arguments. This will also make sure .NET Reflector doesn't
				// crash (sometimes).
				var targets = new List<Block>(block.getTargets());
				targets.Reverse();

				foreach (var target in targets) {
					var child = scopeBlock.toChild(target);
					if (child != null)
						search(child);
				}
			}
			sorted.Add(bb);
		}
	}
}
