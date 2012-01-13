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
		BlockCflowDeobfuscator blockCflowDeobfuscator = new BlockCflowDeobfuscator();
		Blocks blocks;
		List<Block> allBlocks = new List<Block>();
		SwitchCflowDeobfuscator switchCflowDeobfuscator = new SwitchCflowDeobfuscator();
		DeadCodeRemover deadCodeRemover = new DeadCodeRemover();
		DeadStoreRemover deadStoreRemover = new DeadStoreRemover();
		StLdlocFixer stLdlocFixer = new StLdlocFixer();
		ConstantsFolder constantsFolder = new ConstantsFolder();

		public IMethodCallInliner MethodCallInliner { get; set; }

		public void init(Blocks blocks) {
			this.blocks = blocks;
		}

		public void deobfuscate() {
			bool changed;
			int iterations = -1;
			do {
				iterations++;
				changed = false;
				removeDeadBlocks();
				mergeBlocks();

				blocks.MethodBlocks.getAllBlocks(allBlocks);

				if (iterations == 0)
					changed |= fixDotfuscatorLoop();

				foreach (var block in allBlocks) {
					MethodCallInliner.init(blocks, block);
					changed |= MethodCallInliner.deobfuscate();
				}

				foreach (var block in allBlocks) {
					var lastInstr = block.LastInstr;
					if (!DotNetUtils.isConditionalBranch(lastInstr.OpCode.Code) && lastInstr.OpCode.Code != Code.Switch)
						continue;
					blockCflowDeobfuscator.init(blocks, block);
					changed |= blockCflowDeobfuscator.deobfuscate();
				}

				switchCflowDeobfuscator.init(blocks, allBlocks);
				changed |= switchCflowDeobfuscator.deobfuscate();

				deadStoreRemover.init(blocks, allBlocks);
				changed |= deadStoreRemover.remove();

				deadCodeRemover.init(allBlocks);
				changed |= deadCodeRemover.remove();

				if (!changed) {
					constantsFolder.init(blocks, allBlocks);
					changed |= constantsFolder.deobfuscate();
				}

				if (!changed) {
					stLdlocFixer.init(allBlocks, blocks.Locals);
					changed |= stLdlocFixer.fix();
				}
			} while (changed);
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
