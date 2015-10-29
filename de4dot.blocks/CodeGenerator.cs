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

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.blocks {
	class CodeGenerator {
		MethodBlocks methodBlocks;
		List<Block> blocks = new List<Block>();
		Stack<BlockState> stateStack = new Stack<BlockState>();
		List<ExceptionInfo> exceptions = new List<ExceptionInfo>();
		Dictionary<BaseBlock, bool> visited = new Dictionary<BaseBlock, bool>();
		List<BaseBlock> notProcessedYet = new List<BaseBlock>();

		class BlockState {
			public ScopeBlock scopeBlock;

			public BlockState(ScopeBlock scopeBlock) {
				this.scopeBlock = scopeBlock;
			}
		}

		class ExceptionInfo {
			public int tryStart;
			public int tryEnd;
			public int filterStart;
			public int handlerStart;
			public int handlerEnd;
			public ITypeDefOrRef catchType;
			public ExceptionHandlerType handlerType;
			public ExceptionInfo(int tryStart, int tryEnd, int filterStart,
				int handlerStart, int handlerEnd, ITypeDefOrRef catchType,
				ExceptionHandlerType handlerType) {
				if (tryStart > tryEnd || filterStart > handlerStart ||
					tryStart < 0 || tryEnd < 0 || filterStart < 0 || handlerStart < 0 || handlerEnd < 0)
					throw new ApplicationException("Invalid start/end/filter/handler indexes");
				this.tryStart = tryStart;
				this.tryEnd = tryEnd;
				this.filterStart = filterStart == handlerStart ? -1 : filterStart;
				this.handlerStart = handlerStart;
				this.handlerEnd = handlerEnd;
				this.catchType = catchType;
				this.handlerType = handlerType;
			}
		}

		public CodeGenerator(MethodBlocks methodBlocks) {
			this.methodBlocks = methodBlocks;
		}

		public void GetCode(out IList<Instruction> allInstructions, out IList<ExceptionHandler> allExceptionHandlers) {
			FixEmptyBlocks();
			LayOutBlocks();
			SortExceptions();
			LayOutInstructions(out allInstructions, out allExceptionHandlers);

			allInstructions.SimplifyBranches();
			allInstructions.OptimizeBranches();
			allInstructions.UpdateInstructionOffsets();
		}

		class BlockInfo {
			public int start;
			public int end;
			public BlockInfo(int start, int end) {
				this.start = start;
				this.end = end;
			}
		}

		void LayOutInstructions(out IList<Instruction> allInstructions, out IList<ExceptionHandler> allExceptionHandlers) {
			allInstructions = new List<Instruction>();
			allExceptionHandlers = new List<ExceptionHandler>();

			var blockInfos = new List<BlockInfo>();
			for (int i = 0; i < blocks.Count; i++) {
				var block = blocks[i];

				int startIndex = allInstructions.Count;
				for (int j = 0; j < block.Instructions.Count - 1; j++)
					allInstructions.Add(block.Instructions[j].Instruction);

				if (block.Targets != null) {
					var targets = new List<Instr>();
					foreach (var target in block.Targets)
						targets.Add(target.FirstInstr);
					block.LastInstr.UpdateTargets(targets);
				}
				allInstructions.Add(block.LastInstr.Instruction);

				var next = i + 1 < blocks.Count ? blocks[i + 1] : null;

				// If eg. ble next, then change it to bgt XYZ and fall through to next.
				if (block.Targets != null && block.CanFlipConditionalBranch() && block.Targets[0] == next) {
					block.FlipConditionalBranch();
					block.LastInstr.UpdateTargets(new List<Instr> { block.Targets[0].FirstInstr });
				}
				else if (block.FallThrough != null && block.FallThrough != next) {
					var instr = new Instr(OpCodes.Br.ToInstruction(block.FallThrough.FirstInstr.Instruction));
					instr.UpdateTargets(new List<Instr> { block.FallThrough.FirstInstr });
					allInstructions.Add(instr.Instruction);
				}

				int endIndex = allInstructions.Count - 1;

				blockInfos.Add(new BlockInfo(startIndex, endIndex));
			}

			foreach (var ex in exceptions) {
				var tryStart = GetBlockInfo(blockInfos, ex.tryStart).start;
				var tryEnd = GetBlockInfo(blockInfos, ex.tryEnd).end;
				var filterStart = ex.filterStart == -1 ? -1 : GetBlockInfo(blockInfos, ex.filterStart).start;
				var handlerStart = GetBlockInfo(blockInfos, ex.handlerStart).start;
				var handlerEnd = GetBlockInfo(blockInfos, ex.handlerEnd).end;

				var eh = new ExceptionHandler(ex.handlerType);
				eh.CatchType = ex.catchType;
				eh.TryStart = GetInstruction(allInstructions, tryStart);
				eh.TryEnd = GetInstruction(allInstructions, tryEnd + 1);
				eh.FilterStart = filterStart == -1 ? null : GetInstruction(allInstructions, filterStart);
				eh.HandlerStart = GetInstruction(allInstructions, handlerStart);
				eh.HandlerEnd = GetInstruction(allInstructions, handlerEnd + 1);

				allExceptionHandlers.Add(eh);
			}
		}

		static BlockInfo GetBlockInfo(List<BlockInfo> blockInfos, int index) {
			if (index >= blockInfos.Count)
				index = blockInfos.Count - 1;
			if (index < 0)
				index = 0;
			return blockInfos[index];
		}

		static Instruction GetInstruction(IList<Instruction> allInstructions, int i) {
			if (i < allInstructions.Count)
				return allInstructions[i];
			return null;
		}

		void SortExceptions() {
			exceptions.Sort((a, b) => {
				// Make sure nested try blocks are sorted before the outer try block.
				if (a.tryStart > b.tryStart) return -1;	// a could be nested, but b is not
				if (a.tryStart < b.tryStart) return 1;	// b could be nested, but a is not
				// same tryStart
				if (a.tryEnd < b.tryEnd) return -1;		// a is nested
				if (a.tryEnd > b.tryEnd) return 1;		// b is nested
				// same tryEnd (they share try block)

				int ai = a.filterStart == -1 ? a.handlerStart : a.filterStart;
				int bi = b.filterStart == -1 ? b.handlerStart : b.filterStart;
				if (ai < bi) return -1;
				if (ai > bi) return 1;
				// same start

				// if we're here, they should be identical since handlers can't overlap
				// when they share the try block!
				if (a.handlerEnd < b.handlerEnd) return -1;
				if (a.handlerEnd > b.handlerEnd) return 1;
				// same handler end

				return 0;
			});
		}

		void FixEmptyBlocks() {
			foreach (var block in methodBlocks.GetAllBlocks()) {
				if (block.Instructions.Count == 0) {
					block.Instructions.Add(new Instr(OpCodes.Nop.ToInstruction()));
				}
			}
		}

		// Write all blocks to the blocks list
		void LayOutBlocks() {
			if (methodBlocks.BaseBlocks.Count == 0)
				return;

			stateStack.Push(new BlockState(methodBlocks));
			ProcessBaseBlocks(methodBlocks.BaseBlocks, (block) => {
				return block.LastInstr.OpCode == OpCodes.Ret;
			});

			stateStack.Pop();

			foreach (var bb in notProcessedYet) {
				bool wasVisited;
				visited.TryGetValue(bb, out wasVisited);
				if (!wasVisited)
					throw new ApplicationException("A block wasn't processed");
			}
		}

		void ProcessBaseBlocks(List<BaseBlock> lb, Func<Block, bool> placeLast) {
			var bbs = new List<BaseBlock>();
			int lastIndex = -1;
			for (int i = 0; i < lb.Count; i++) {
				var bb = lb[i];
				var block = bb as Block;
				if (block != null && placeLast(block))
					lastIndex = i;
				bbs.Add(bb);
			}
			if (lastIndex != -1) {
				var block = (Block)bbs[lastIndex];
				bbs.RemoveAt(lastIndex);
				bbs.Add(block);
			}
			foreach (var bb in bbs)
				DoBaseBlock(bb);
		}

		// Returns the BaseBlock's ScopeBlock. The return value is either current ScopeBlock,
		// the ScopeBlock one step below current (current one's child), or null.
		ScopeBlock GetScopeBlock(BaseBlock bb) {
			BlockState current = stateStack.Peek();

			if (current.scopeBlock.IsOurBaseBlock(bb))
				return current.scopeBlock;
			return (ScopeBlock)current.scopeBlock.ToChild(bb);
		}

		void DoBaseBlock(BaseBlock bb) {
			BlockState current = stateStack.Peek();
			ScopeBlock newOne = GetScopeBlock(bb);
			if (newOne == null)
				return;		// Not a BaseBlock somewhere inside this ScopeBlock
			if (newOne != current.scopeBlock)
				bb = newOne;

			bool hasVisited;
			if (!visited.TryGetValue(bb, out hasVisited))
				visited[bb] = hasVisited = false;
			if (hasVisited)
				return;
			visited[bb] = true;

			if (bb is Block)
				DoBlock(bb as Block);
			else if (bb is TryBlock)
				DoTryBlock(bb as TryBlock);
			else if (bb is FilterHandlerBlock)
				DoFilterHandlerBlock(bb as FilterHandlerBlock);
			else if (bb is HandlerBlock)
				DoHandlerBlock(bb as HandlerBlock);
			else if (bb is TryHandlerBlock) {
				// The try handler block is usually after the try block, but sometimes it isn't...
				// Handle that case here.
				visited.Remove(bb);
				notProcessedYet.Add(bb);
			}
			else
				throw new ApplicationException("Invalid block found");
		}

		void DoBlock(Block block) {
			blocks.Add(block);
		}

		void DoTryBlock(TryBlock tryBlock) {
			var tryStart = blocks.Count;
			stateStack.Push(new BlockState(tryBlock));
			ProcessBaseBlocks(tryBlock.BaseBlocks, (block) => {
				return block.LastInstr.OpCode == OpCodes.Leave ||
						block.LastInstr.OpCode == OpCodes.Leave_S;
			});
			stateStack.Pop();
			var tryEnd = blocks.Count - 1;

			if (tryBlock.TryHandlerBlocks.Count == 0)
				throw new ApplicationException("No handler blocks");

			foreach (var handlerBlock in tryBlock.TryHandlerBlocks) {
				visited[handlerBlock] = true;

				stateStack.Push(new BlockState(handlerBlock));

				var filterStart = blocks.Count;
				if (handlerBlock.FilterHandlerBlock.BaseBlocks != null)
					DoBaseBlock(handlerBlock.FilterHandlerBlock);

				var handlerStart = blocks.Count;
				DoBaseBlock(handlerBlock.HandlerBlock);
				var handlerEnd = blocks.Count - 1;

				exceptions.Add(new ExceptionInfo(tryStart, tryEnd, filterStart, handlerStart, handlerEnd, handlerBlock.CatchType, handlerBlock.HandlerType));

				stateStack.Pop();
			}
		}

		void DoFilterHandlerBlock(FilterHandlerBlock filterHandlerBlock) {
			stateStack.Push(new BlockState(filterHandlerBlock));
			ProcessBaseBlocks(filterHandlerBlock.BaseBlocks, (block) => {
				return block.LastInstr.OpCode == OpCodes.Endfilter;	// MUST end with endfilter!
			});
			stateStack.Pop();
		}

		void DoHandlerBlock(HandlerBlock handlerBlock) {
			stateStack.Push(new BlockState(handlerBlock));
			ProcessBaseBlocks(handlerBlock.BaseBlocks, (block) => {
				return block.LastInstr.OpCode == OpCodes.Endfinally ||
						block.LastInstr.OpCode == OpCodes.Leave ||
						block.LastInstr.OpCode == OpCodes.Leave_S;
			});
			stateStack.Pop();
		}
	}
}
