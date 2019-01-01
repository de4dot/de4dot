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

using System.Collections.Generic;
using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	public class BlocksCflowDeobfuscator {
		Blocks blocks;
		List<Block> allBlocks = new List<Block>();
		List<IBlocksDeobfuscator> userBlocksDeobfuscators = new List<IBlocksDeobfuscator>();
		List<IBlocksDeobfuscator> ourBlocksDeobfuscators = new List<IBlocksDeobfuscator>();

		public BlocksCflowDeobfuscator()
			: this(false) {
		}

		public BlocksCflowDeobfuscator(bool disableNewCFCode) => Initialize(disableNewCFCode);

		public BlocksCflowDeobfuscator(IEnumerable<IBlocksDeobfuscator> blocksDeobfuscator)
			: this(blocksDeobfuscator, false) {
		}

		public BlocksCflowDeobfuscator(IEnumerable<IBlocksDeobfuscator> blocksDeobfuscator, bool disableNewCFCode) {
			Initialize(disableNewCFCode);
			Add(blocksDeobfuscator);
		}

		void Initialize(bool disableNewCFCode) {
			ourBlocksDeobfuscators.Add(new BlockCflowDeobfuscator { ExecuteIfNotModified = false });
			ourBlocksDeobfuscators.Add(new SwitchCflowDeobfuscator { ExecuteIfNotModified = false });
			ourBlocksDeobfuscators.Add(new DeadStoreRemover { ExecuteIfNotModified = false });
			ourBlocksDeobfuscators.Add(new DeadCodeRemover { ExecuteIfNotModified = false });
			ourBlocksDeobfuscators.Add(new ConstantsFolder { ExecuteIfNotModified = true, DisableNewCode = disableNewCFCode });
			ourBlocksDeobfuscators.Add(new StLdlocFixer { ExecuteIfNotModified = true });
			ourBlocksDeobfuscators.Add(new DupBlockCflowDeobfuscator { ExecuteIfNotModified = true });
		}

		public void Add(IEnumerable<IBlocksDeobfuscator> blocksDeobfuscators) {
			foreach (var bd in blocksDeobfuscators)
				Add(bd);
		}

		public void Add(IBlocksDeobfuscator blocksDeobfuscator) {
			if (blocksDeobfuscator != null)
				userBlocksDeobfuscators.Add(blocksDeobfuscator);
		}

		public void Initialize(Blocks blocks) => this.blocks = blocks;

		public void Deobfuscate() {
			bool modified;
			int iterations = -1;

			DeobfuscateBegin(userBlocksDeobfuscators);
			DeobfuscateBegin(ourBlocksDeobfuscators);

			do {
				iterations++;
				modified = false;
				RemoveDeadBlocks();
				MergeBlocks();

				blocks.MethodBlocks.GetAllBlocks(allBlocks);

				if (iterations == 0)
					modified |= FixDotfuscatorLoop();

				modified |= Deobfuscate(userBlocksDeobfuscators, allBlocks);
				modified |= Deobfuscate(ourBlocksDeobfuscators, allBlocks);
				modified |= DeobfuscateIfNotModified(modified, userBlocksDeobfuscators, allBlocks);
				modified |= DeobfuscateIfNotModified(modified, ourBlocksDeobfuscators, allBlocks);
			} while (modified);
		}

		void DeobfuscateBegin(IEnumerable<IBlocksDeobfuscator> bds) {
			foreach (var bd in bds)
				bd.DeobfuscateBegin(blocks);
		}

		bool Deobfuscate(IEnumerable<IBlocksDeobfuscator> bds, List<Block> allBlocks) {
			bool modified = false;
			foreach (var bd in bds) {
				if (bd.ExecuteIfNotModified)
					continue;
				modified |= bd.Deobfuscate(allBlocks);
			}
			return modified;
		}

		bool DeobfuscateIfNotModified(bool modified, IEnumerable<IBlocksDeobfuscator> bds, List<Block> allBlocks) {
			foreach (var bd in bds) {
				if (modified)
					break;
				if (!bd.ExecuteIfNotModified)
					continue;
				modified |= bd.Deobfuscate(allBlocks);
			}
			return modified;
		}

		// Hack for old Dotfuscator
		bool FixDotfuscatorLoop() {
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
			bool modified = false;
			foreach (var block in allBlocks) {
				if (block.Instructions.Count != 5)
					continue;
				var instructions = block.Instructions;
				if (instructions[0].OpCode.Code != Code.Dup)
					continue;
				if (instructions[1].OpCode.Code != Code.Dup)
					continue;
				if (!instructions[2].IsLdcI4())
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
				if (prev == null || !prev.LastInstr.IsLdcI4())
					continue;
				var next = block.FallThrough;
				if (next.FirstInstr.OpCode.Code != Code.Pop)
					continue;
				block.ReplaceLastInstrsWithBranch(5, next);
				modified = true;
			}
			return modified;
		}

		bool RemoveDeadBlocks() => new DeadBlocksRemover(blocks.MethodBlocks).Remove() > 0;

		bool MergeBlocks() {
			bool modified = false;
			foreach (var scopeBlock in GetAllScopeBlocks(blocks.MethodBlocks))
				modified |= scopeBlock.MergeBlocks() > 0;
			return modified;
		}

		IEnumerable<ScopeBlock> GetAllScopeBlocks(ScopeBlock scopeBlock) {
			var list = new List<ScopeBlock>();
			list.Add(scopeBlock);
			list.AddRange(scopeBlock.GetAllScopeBlocks());
			return list;
		}
	}
}
