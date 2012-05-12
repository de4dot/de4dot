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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.blocks.cflow {
	public class BlocksCflowDeobfuscator {
		Blocks blocks;
		List<Block> allBlocks = new List<Block>();
		List<IBlocksDeobfuscator> userBlocksDeobfuscators = new List<IBlocksDeobfuscator>();
		List<IBlocksDeobfuscator> ourBlocksDeobfuscators = new List<IBlocksDeobfuscator>();

		public BlocksCflowDeobfuscator() {
			init();
		}

		public BlocksCflowDeobfuscator(IEnumerable<IBlocksDeobfuscator> blocksDeobfuscator) {
			init();
			add(blocksDeobfuscator);
		}

		void init() {
			ourBlocksDeobfuscators.Add(new BlockCflowDeobfuscator { ExecuteOnNoChange = false });
			ourBlocksDeobfuscators.Add(new SwitchCflowDeobfuscator { ExecuteOnNoChange = false });
			ourBlocksDeobfuscators.Add(new DeadStoreRemover { ExecuteOnNoChange = false });
			ourBlocksDeobfuscators.Add(new DeadCodeRemover { ExecuteOnNoChange = false });
			ourBlocksDeobfuscators.Add(new ConstantsFolder { ExecuteOnNoChange = true });
			ourBlocksDeobfuscators.Add(new StLdlocFixer { ExecuteOnNoChange = true });
		}

		public void add(IEnumerable<IBlocksDeobfuscator> blocksDeobfuscators) {
			foreach (var bd in blocksDeobfuscators)
				add(bd);
		}

		public void add(IBlocksDeobfuscator blocksDeobfuscator) {
			if (blocksDeobfuscator != null)
				userBlocksDeobfuscators.Add(blocksDeobfuscator);
		}

		public void init(Blocks blocks) {
			this.blocks = blocks;
		}

		public void deobfuscate() {
			bool changed;
			int iterations = -1;

			deobfuscateBegin(userBlocksDeobfuscators);
			deobfuscateBegin(ourBlocksDeobfuscators);

			do {
				iterations++;
				changed = false;
				removeDeadBlocks();
				mergeBlocks();

				blocks.MethodBlocks.getAllBlocks(allBlocks);

				if (iterations == 0)
					changed |= fixDotfuscatorLoop();

				changed |= deobfuscate(userBlocksDeobfuscators, allBlocks);
				changed |= deobfuscate(ourBlocksDeobfuscators, allBlocks);
				changed |= deobfuscateNoChange(changed, userBlocksDeobfuscators, allBlocks);
				changed |= deobfuscateNoChange(changed, ourBlocksDeobfuscators, allBlocks);
			} while (changed);
		}

		void deobfuscateBegin(IEnumerable<IBlocksDeobfuscator> bds) {
			foreach (var bd in bds)
				bd.deobfuscateBegin(blocks);
		}

		bool deobfuscate(IEnumerable<IBlocksDeobfuscator> bds, List<Block> allBlocks) {
			bool changed = false;
			foreach (var bd in bds) {
				if (bd.ExecuteOnNoChange)
					continue;
				changed |= bd.deobfuscate(allBlocks);
			}
			return changed;
		}

		bool deobfuscateNoChange(bool changed, IEnumerable<IBlocksDeobfuscator> bds, List<Block> allBlocks) {
			foreach (var bd in bds) {
				if (changed)
					break;
				if (!bd.ExecuteOnNoChange)
					continue;
				changed |= bd.deobfuscate(allBlocks);
			}
			return changed;
		}

		// Hack for old Dotfuscator
		bool fixDotfuscatorLoop() {
			/*
			blk1:
				...
				ldc.i4.x
			blk2:
				dup
				dup
				ldc.i4.y
				some_op
				bcc blk2
			blk3:
				pop
				...
			*/
			bool changed = false;
			foreach (var block in allBlocks) {
				if (block.Instructions.Count != 5)
					continue;
				var instructions = block.Instructions;
				if (instructions[0].OpCode.Code != Code.Dup)
					continue;
				if (instructions[1].OpCode.Code != Code.Dup)
					continue;
				if (!instructions[2].isLdcI4())
					continue;
				if (instructions[3].OpCode.Code != Code.Sub && instructions[3].OpCode.Code != Code.Add)
					continue;
				if (instructions[4].OpCode.Code != Code.Blt && instructions[4].OpCode.Code != Code.Blt_S &&
					instructions[4].OpCode.Code != Code.Bgt && instructions[4].OpCode.Code != Code.Bgt_S)
					continue;
				if (block.Sources.Count != 2)
					continue;
				var prev = block.Sources[0];
				if (prev == block)
					prev = block.Sources[1];
				if (prev == null || !prev.LastInstr.isLdcI4())
					continue;
				var next = block.FallThrough;
				if (next.FirstInstr.OpCode.Code != Code.Pop)
					continue;
				block.replaceLastInstrsWithBranch(5, next);
				changed = true;
			}
			return changed;
		}

		bool removeDeadBlocks() {
			return new DeadBlocksRemover(blocks.MethodBlocks).remove() > 0;
		}

		bool mergeBlocks() {
			bool changed = false;
			foreach (var scopeBlock in getAllScopeBlocks(blocks.MethodBlocks))
				changed |= scopeBlock.mergeBlocks() > 0;
			return changed;
		}

		IEnumerable<ScopeBlock> getAllScopeBlocks(ScopeBlock scopeBlock) {
			var list = new List<ScopeBlock>();
			list.Add(scopeBlock);
			list.AddRange(scopeBlock.getAllScopeBlocks());
			return list;
		}
	}
}
