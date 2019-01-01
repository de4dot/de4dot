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

namespace de4dot.blocks {
	class BlocksSorter {
		ScopeBlock scopeBlock;

		class BlockInfo {
			public int dfsNumber = -1;
			public int low;
			public BaseBlock baseBlock;
			public bool onStack;

			public BlockInfo(BaseBlock baseBlock) => this.baseBlock = baseBlock;
			public bool Visited() => dfsNumber >= 0;
			public override string ToString() => $"L:{low}, D:{dfsNumber}, S:{onStack}";
		}

		// It uses Tarjan's strongly connected components algorithm to find all SCCs.
		// See http://www.ics.uci.edu/~eppstein/161/960220.html or wikipedia for a good explanation.
		// The non-Tarjan code is still pretty simple and can (should) be improved.
		class Sorter {
			ScopeBlock scopeBlock;
			IList<BaseBlock> validBlocks;
			Dictionary<BaseBlock, BlockInfo> blockToInfo = new Dictionary<BaseBlock, BlockInfo>();
			Stack<BlockInfo> stack = new Stack<BlockInfo>();
			List<BaseBlock> sorted;
			int dfsNumber = 0;
			bool skipFirstBlock;
			BaseBlock firstBlock;

			public Sorter(ScopeBlock scopeBlock, IList<BaseBlock> validBlocks, bool skipFirstBlock) {
				this.scopeBlock = scopeBlock;
				this.validBlocks = validBlocks;
				this.skipFirstBlock = skipFirstBlock;
			}

			public List<BaseBlock> Sort() {
				if (validBlocks.Count == 0)
					return new List<BaseBlock>();
				if (skipFirstBlock)
					firstBlock = validBlocks[0];

				foreach (var block in validBlocks) {
					if (block != firstBlock)
						blockToInfo[block] = new BlockInfo(block);
				}

				sorted = new List<BaseBlock>(validBlocks.Count);
				var finalList = new List<BaseBlock>(validBlocks.Count);

				if (firstBlock is Block) {
					foreach (var target in GetTargets(firstBlock)) {
						Visit(target);
						finalList.AddRange(sorted);
						sorted.Clear();
					}
				}
				foreach (var bb in validBlocks) {
					Visit(bb);
					finalList.AddRange(sorted);
					sorted.Clear();
				}

				if (stack.Count > 0)
					throw new ApplicationException("Stack isn't empty");

				if (firstBlock != null)
					finalList.Insert(0, firstBlock);
				else if (validBlocks[0] != finalList[0]) {
					// Make sure the original first block is first
					int index = finalList.IndexOf(validBlocks[0]);
					finalList.RemoveAt(index);
					finalList.Insert(0, validBlocks[0]);
				}
				return finalList;
			}

			void Visit(BaseBlock bb) {
				var info = GetInfo(bb);
				if (info == null)
					return;
				if (info.baseBlock == firstBlock)
					return;
				if (info.Visited())
					return;
				Visit(info);
			}

			BlockInfo GetInfo(BaseBlock baseBlock) {
				baseBlock = scopeBlock.ToChild(baseBlock);
				if (baseBlock == null)
					return null;
				blockToInfo.TryGetValue(baseBlock, out var info);
				return info;
			}

			List<BaseBlock> GetTargets(BaseBlock baseBlock) {
				var list = new List<BaseBlock>();

				if (baseBlock is Block block)
					AddTargets(list, block.GetTargets());
				else if (baseBlock is TryBlock)
					AddTargets(list, (TryBlock)baseBlock);
				else if (baseBlock is TryHandlerBlock)
					AddTargets(list, (TryHandlerBlock)baseBlock);
				else
					AddTargets(list, (ScopeBlock)baseBlock);

				return list;
			}

			void AddTargets(List<BaseBlock> dest, TryBlock tryBlock) {
				AddTargets(dest, (ScopeBlock)tryBlock);
				foreach (var tryHandlerBlock in tryBlock.TryHandlerBlocks) {
					dest.Add(tryHandlerBlock);
					AddTargets(dest, tryHandlerBlock);
				}
			}

			void AddTargets(List<BaseBlock> dest, TryHandlerBlock tryHandlerBlock) {
				AddTargets(dest, (ScopeBlock)tryHandlerBlock);

				dest.Add(tryHandlerBlock.FilterHandlerBlock);
				AddTargets(dest, tryHandlerBlock.FilterHandlerBlock);

				dest.Add(tryHandlerBlock.HandlerBlock);
				AddTargets(dest, tryHandlerBlock.HandlerBlock);
			}

			void AddTargets(List<BaseBlock> dest, ScopeBlock scopeBlock) {
				foreach (var block in scopeBlock.GetAllBlocks())
					AddTargets(dest, block.GetTargets());
			}

			void AddTargets(List<BaseBlock> dest, IEnumerable<Block> source) {
				var list = new List<Block>(source);
				list.Reverse();
				foreach (var block in list)
					dest.Add(block);
			}

			struct VisitState {
				public BlockInfo Info;
				public List<BaseBlock> Targets;
				public int TargetIndex;
				public BlockInfo TargetInfo;
				public VisitState(BlockInfo info) {
					Info = info;
					Targets = null;
					TargetIndex = 0;
					TargetInfo = null;
				}
			}
			Stack<VisitState> visitStateStack = new Stack<VisitState>();
			void Visit(BlockInfo info) {
				// This method used to be recursive but to prevent stack overflows,
				// it's not recursive anymore.

				var state = new VisitState(info);
recursive_call:
				if (state.Info.baseBlock == firstBlock)
					throw new ApplicationException("Can't visit firstBlock");
				stack.Push(state.Info);
				state.Info.onStack = true;
				state.Info.dfsNumber = dfsNumber;
				state.Info.low = dfsNumber;
				dfsNumber++;

				state.Targets = GetTargets(state.Info.baseBlock);
				state.TargetIndex = 0;
return_to_caller:
				for (; state.TargetIndex < state.Targets.Count; state.TargetIndex++) {
					state.TargetInfo = GetInfo(state.Targets[state.TargetIndex]);
					if (state.TargetInfo == null)
						continue;
					if (state.TargetInfo.baseBlock == firstBlock)
						continue;

					if (!state.TargetInfo.Visited()) {
						visitStateStack.Push(state);
						state = new VisitState(state.TargetInfo);
						goto recursive_call;
					}
					else if (state.TargetInfo.onStack)
						state.Info.low = Math.Min(state.Info.low, state.TargetInfo.dfsNumber);
				}

				if (state.Info.low != state.Info.dfsNumber)
					goto return_from_method;
				var sccBlocks = new List<BaseBlock>();
				while (true) {
					var poppedInfo = stack.Pop();
					poppedInfo.onStack = false;
					sccBlocks.Add(poppedInfo.baseBlock);
					if (ReferenceEquals(state.Info, poppedInfo))
						break;
				}
				if (sccBlocks.Count > 1) {
					sccBlocks.Reverse();
					var result = new Sorter(scopeBlock, sccBlocks, true).Sort();
					SortLoopBlock(result);
					sorted.InsertRange(0, result);
				}
				else {
					sorted.Insert(0, sccBlocks[0]);
				}

return_from_method:
				if (visitStateStack.Count == 0)
					return;
				state = visitStateStack.Pop();
				state.Info.low = Math.Min(state.Info.low, state.TargetInfo.low);
				state.TargetIndex++;
				goto return_to_caller;
			}

			void SortLoopBlock(List<BaseBlock> list) {
				// Some popular decompilers sometimes produce bad output unless the loop condition
				// checker block is at the end of the loop. Eg., they may use a while loop when
				// it's really a for/foreach loop.

				var loopStart = GetLoopStartBlock(list);
				if (loopStart == null)
					return;

				if (!list.Remove(loopStart))
					throw new ApplicationException("Could not remove block");
				list.Add(loopStart);
			}

			Block GetLoopStartBlock(List<BaseBlock> list) {
				var loopBlocks = new Dictionary<Block, bool>(list.Count);
				foreach (var bb in list) {
					if (bb is Block block)
						loopBlocks[block] = true;
				}

				var targetBlocks = new Dictionary<Block, int>();
				foreach (var bb in list) {
					var block = bb as Block;
					if (block == null)
						continue;
					foreach (var source in block.Sources) {
						if (loopBlocks.ContainsKey(source))
							continue;
						targetBlocks.TryGetValue(block, out int count);
						targetBlocks[block] = count + 1;
					}
				}

				int max = -1;
				Block loopStart = null;
				foreach (var kv in targetBlocks) {
					if (kv.Value <= max)
						continue;
					max = kv.Value;
					loopStart = kv.Key;
				}

				return loopStart;
			}
		}

		public BlocksSorter(ScopeBlock scopeBlock) => this.scopeBlock = scopeBlock;

		public List<BaseBlock> Sort() {
			var sorted = new Sorter(scopeBlock, scopeBlock.BaseBlocks, false).Sort();
			return new ForwardScanOrder(scopeBlock, sorted).Fix();
		}
	}
}
