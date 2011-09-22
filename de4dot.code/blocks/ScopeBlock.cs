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

using System;
using System.Collections.Generic;
using Mono.Cecil.Cil;

namespace de4dot.blocks {
	// A normal branch may not transfer out of a protected block (try block), filter handler,
	// an exception handler block, or a method.
	abstract class ScopeBlock : BaseBlock {
		protected List<BaseBlock> baseBlocks;

		public List<BaseBlock> BaseBlocks {
			get { return baseBlocks; }
			set { baseBlocks = value; }
		}

		public IEnumerable<BaseBlock> getBaseBlocks() {
			if (baseBlocks != null) {
				foreach (var bb in baseBlocks)
					yield return bb;
			}
		}

		public IList<BaseBlock> getAllBaseBlocks() {
			return getAllBlocks(new List<BaseBlock>());
		}

		public IList<Block> getAllBlocks() {
			return getAllBlocks(new List<Block>());
		}

		public IList<ScopeBlock> getAllScopeBlocks() {
			return getAllBlocks(new List<ScopeBlock>());
		}

		IList<T> getAllBlocks<T>(IList<T> list) where T : BaseBlock {
			addBlocks(list, this);
			return list;
		}

		void addBlocks<T>(IList<T> list, ScopeBlock scopeBlock) where T : BaseBlock {
			foreach (var bb in scopeBlock.getBaseBlocks()) {
				T t = bb as T;
				if (t != null)
					list.Add(t);
				if (bb is ScopeBlock)
					addBlocks(list, (ScopeBlock)bb);
			}
		}

		List<Block> findBlocks(Func<Block, bool> blockChecker = null) {
			var blocks = new List<Block>();
			foreach (var bb in getBaseBlocks()) {
				Block block = bb as Block;
				if (block != null && (blockChecker == null || blockChecker(block)))
					blocks.Add(block);
			}
			return blocks;
		}

		public void deobfuscateLeaveObfuscation() {
			foreach (var block in findBlocks())
				deobfuscateLeaveObfuscation(block);
		}

		void deobfuscateLeaveObfuscation(Block block) {
			var instrs = block.Instructions;
			int index = instrs.Count - 1;
			if (index <= 0)
				return;
			var lastInstr = instrs[index];
			if (!lastInstr.isLeave() && lastInstr.OpCode != OpCodes.Endfinally && lastInstr.OpCode != OpCodes.Rethrow)
				return;

			int end = index;
			int start = end;
			if (start <= 0)
				return;
			while (start > 0) {
				var instr = instrs[--start];

				bool valid = false;
				switch (instr.OpCode.Code) {
				case Code.Dup:
				case Code.Ldarg:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
				case Code.Ldarga:
				case Code.Ldarga_S:
				case Code.Ldarg_S:
				case Code.Ldc_I4:
				case Code.Ldc_I4_0:
				case Code.Ldc_I4_1:
				case Code.Ldc_I4_2:
				case Code.Ldc_I4_3:
				case Code.Ldc_I4_4:
				case Code.Ldc_I4_5:
				case Code.Ldc_I4_6:
				case Code.Ldc_I4_7:
				case Code.Ldc_I4_8:
				case Code.Ldc_I4_M1:
				case Code.Ldc_I4_S:
				case Code.Ldc_I8:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
				case Code.Ldftn:
				case Code.Ldloc:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
				case Code.Ldloca:
				case Code.Ldloca_S:
				case Code.Ldloc_S:
				case Code.Ldnull:
				case Code.Ldsfld:
				case Code.Ldsflda:
				case Code.Ldstr:
				case Code.Ldtoken:
				case Code.Nop:
					valid = true;
					break;
				}
				if (!valid) {
					start++;
					break;
				}
			}

			int num = end - start;
			if (num > 0)
				block.remove(start, num);
		}

		public void deobfuscate(Blocks blocks) {
			while (true) {
				mergeBlocks();

				bool removed = false;
				removed |= removeNops();
				removed |= deobfuscateConditionalBranches();
				if (!removed)
					break;
			}

			var switchDeobfuscator = new SwitchControlFlowDeobfuscator(blocks);
			switchDeobfuscator.deobfuscate(this);
		}

		bool removeNops() {
			bool removed = false;

			foreach (var block in findBlocks()) 
				removed |= block.removeNops();

			return removed;
		}

		internal bool getLdcValue(Instr instr, out int value) {
			if (Code.Ldc_I4_0 <= instr.OpCode.Code && instr.OpCode.Code <= Code.Ldc_I4_8)
				value = instr.OpCode.Code - Code.Ldc_I4_0;
			else if (instr.OpCode.Code == Code.Ldc_I4)
				value = (int)instr.Operand;
			else if (instr.OpCode.Code == Code.Ldc_I4_S)
				value = (sbyte)instr.Operand;
			else if (instr.OpCode.Code == Code.Ldc_I4_M1)
				value = -1;
			else {
				value = 0;
				return false;
			}
			return true;
		}

		bool deobfuscateConditionalBranches() {
			bool removed = false;
			removed |= new BrFalseDeobfuscator(this, findBlocks((block) => block.LastInstr.isBrfalse())).deobfuscate();
			removed |= new BrTrueDeobfuscator(this, findBlocks((block) => block.LastInstr.isBrtrue())).deobfuscate();
			return removed;
		}

		// Remove the block if it's a dead block. If it has refs to other dead blocks, those
		// are also removed.
		public void removeDeadBlock(Block block) {
			removeDeadBlocks(new List<Block> { block });
		}

		// Remove all dead blocks we can find
		public void removeDeadBlocks() {
			removeDeadBlocks(findBlocks());
		}

		// Remove the blocks if they're dead blocks. If they have refs to other dead blocks,
		// those are also removed.
		public void removeDeadBlocks(List<Block> blocks) {
			while (blocks.Count != 0) {
				var block = blocks[blocks.Count - 1];
				blocks.RemoveAt(blocks.Count - 1);
				if (block.Sources.Count != 0)
					continue;	// Not dead
				if (block == baseBlocks[0])
					continue;	// It's the start of this block fence so must be present
				if (!isOurBlockBase(block))
					continue;	// Some other ScopeBlock owns it, eg. first instr of an exception handler

				// It's a dead block we can delete!

				if (block.FallThrough != null)
					blocks.Add(block.FallThrough);
				if (block.Targets != null)
					blocks.AddRange(block.Targets);
				block.removeDeadBlock();
				if (!baseBlocks.Remove(block))
					throw new ApplicationException("Could not remove dead block from baseBlocks");
			}
		}

		public bool isOurBlockBase(BaseBlock bb) {
			return bb != null && bb.Parent == this;
		}

		// For each block, if it has only one target, and the target has only one source, then
		// merge them into one block.
		public void mergeBlocks() {
			var blocks = findBlocks();
			for (int i = 0; i < blocks.Count; i++) {
				var block = blocks[i];
				var target = block.getOnlyTarget();
				if (!isOurBlockBase(target))
					continue;	// Only merge blocks we own!
				if (!block.canMerge(target))
					continue;	// Can't merge them!
				if (target == baseBlocks[0])
					continue;	// The first one has an implicit source (eg. start of method or exception handler)

				var targetIndex = blocks.IndexOf(target);
				if (targetIndex < 0)
					throw new ApplicationException("Could not remove target block from blocks");
				blocks.RemoveAt(targetIndex);
				block.merge(target);
				if (!baseBlocks.Remove(target))
					throw new ApplicationException("Could not remove merged block from baseBlocks");
				if (targetIndex < i)
					i--;
				i--;				// Redo since there may be more blocks we can merge
			}
		}

		// If bb is in baseBlocks (a direct child), return bb. If bb is a BaseBlock in a
		// ScopeBlock that is a direct child, then return that ScopeBlock. Else return null.
		public BaseBlock toChild(BaseBlock bb) {
			if (isOurBlockBase(bb))
				return bb;

			for (bb = bb.Parent; bb != null; bb = bb.Parent) {
				var sb = bb as ScopeBlock;
				if (sb == null)
					throw new ApplicationException("Parent is not a ScopeBlock");
				if (isOurBlockBase(sb))
					return sb;
			}

			return null;
		}

		internal void repartitionBlocks() {
			var newBaseBlocks = new BlocksSorter(this).sort();

			const bool insane = true;
			if (insane) {
				if (newBaseBlocks.Count != baseBlocks.Count)
					throw new ApplicationException("BlocksSorter included too many/few BaseBlocks");
				foreach (var bb in baseBlocks) {
					if (!newBaseBlocks.Contains(bb))
						throw new ApplicationException("BlocksSorter forgot a child");
				}
			}

			baseBlocks = newBaseBlocks;
		}

		// Removes the TryBlock and all its TryHandlerBlocks. The code inside the try block
		// is not removed.
		public void removeTryBlock(TryBlock tryBlock) {
			int tryBlockIndex = baseBlocks.IndexOf(tryBlock);
			if (tryBlockIndex < 0)
				throw new ApplicationException("Can't remove the TryBlock since it's not this ScopeBlock's TryBlock");

			foreach (var bb in tryBlock.BaseBlocks)
				bb.Parent = this;
			baseBlocks.RemoveAt(tryBlockIndex);
			baseBlocks.InsertRange(tryBlockIndex, tryBlock.BaseBlocks);
			tryBlock.BaseBlocks.Clear();

			// Get removed blocks and make sure they're not referenced by remaining code
			var removedBlocks = new List<Block>();
			foreach (var handler in tryBlock.TryHandlerBlocks)
				handler.getAllBlocks(removedBlocks);
			if (!verifyNoExternalRefs(removedBlocks))
				throw new ApplicationException("Removed blocks are referenced by remaining code");

			removeAllDeadBlocks(Utils.convert<TryHandlerBlock, BaseBlock>(tryBlock.TryHandlerBlocks));
		}

		// Returns true if no external blocks references the blocks
		static bool verifyNoExternalRefs(IList<Block> removedBlocks) {
			var removedDict = new Dictionary<Block, bool>();
			foreach (var removedBlock in removedBlocks)
				removedDict[removedBlock] = true;
			foreach (var removedBlock in removedBlocks) {
				foreach (var source in removedBlock.Sources) {
					bool val;
					if (!removedDict.TryGetValue(source, out val))
						return false;	// external code references a removed block
				}
			}
			return true;
		}

		// Remove all blocks in deadBlocks. They're guaranteed to be dead.
		void removeAllDeadBlocks(IEnumerable<BaseBlock> deadBlocks) {
			removeAllDeadBlocks(deadBlocks, null);
		}

		// Remove all blocks in deadBlocks. They're guaranteed to be dead. deadBlocksDict is
		// a dictionary of all dead blocks (even those not in this ScopeBlock).
		internal void removeAllDeadBlocks(IEnumerable<BaseBlock> deadBlocks, Dictionary<BaseBlock, bool> deadBlocksDict) {

			// Verify that all the blocks really are dead. If all their source blocks are
			// dead, then they are dead.

			var allDeadBlocks = new List<Block>();
			foreach (var bb in deadBlocks) {
				if (bb is Block)
					allDeadBlocks.Add(bb as Block);
				else if (bb is ScopeBlock) {
					var sb = (ScopeBlock)bb;
					allDeadBlocks.AddRange(sb.getAllBlocks());
				}
				else
					throw new ApplicationException(string.Format("Unknown BaseBlock type {0}", bb.GetType()));
			}

			if (deadBlocksDict != null) {
				foreach (var block in allDeadBlocks) {
					if (block.Sources == null)
						continue;
					foreach (var source in block.Sources) {
						if (!deadBlocksDict.ContainsKey(source))
							throw new ApplicationException("Trying to remove a block that is not dead!");
					}
				}
			}

			foreach (var block in allDeadBlocks)
				block.removeGuaranteedDeadBlock();
			foreach (var bb in deadBlocks) {
				if (!baseBlocks.Remove(bb))
					throw new ApplicationException("Could not remove dead base block from baseBlocks");
			}
		}
	}
}
