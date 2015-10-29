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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class CompositeHandlerDetector {
		readonly List<MethodSigInfo> handlers;

		public CompositeHandlerDetector(IList<MethodSigInfo> handlers) {
			this.handlers = new List<MethodSigInfo>(handlers);

			this.handlers.Sort((a, b) => {
				int r = b.BlockSigInfos.Count.CompareTo(a.BlockSigInfos.Count);
				if (r != 0)
					return r;
				return b.BlockSigInfos[0].Hashes.Count.CompareTo(a.BlockSigInfos[0].Hashes.Count);
			});
		}

		struct MatchState {
			public HandlerState OtherState;
			public HandlerState CompositeState;

			public MatchState(HandlerState OtherState, HandlerState CompositeState) {
				this.OtherState = OtherState;
				this.CompositeState = CompositeState;
			}
		}

		struct HandlerState {
			public readonly List<BlockSigInfo> BlockSigInfos;
			public readonly int BlockIndex;
			public int HashIndex;

			public HandlerState(List<BlockSigInfo> blockSigInfos, int blockIndex, int hashIndex) {
				this.BlockSigInfos = blockSigInfos;
				this.BlockIndex = blockIndex;
				this.HashIndex = hashIndex;
			}

			public HandlerState Clone() {
				return new HandlerState(BlockSigInfos, BlockIndex, HashIndex);
			}
		}

		struct FindHandlerState {
			public HandlerState CompositeState;
			public readonly Dictionary<int, bool> VisitedCompositeBlocks;
			public bool Done;

			public FindHandlerState(HandlerState compositeState) {
				this.CompositeState = compositeState;
				this.VisitedCompositeBlocks = new Dictionary<int, bool>();
				this.Done = false;
			}

			public FindHandlerState(HandlerState compositeState, Dictionary<int, bool> visitedCompositeBlocks, bool done) {
				this.CompositeState = compositeState;
				this.VisitedCompositeBlocks = new Dictionary<int, bool>(visitedCompositeBlocks);
				this.Done = done;
			}

			public FindHandlerState Clone() {
				return new FindHandlerState(CompositeState.Clone(), VisitedCompositeBlocks, Done);
			}
		}

		public bool FindHandlers(CompositeOpCodeHandler composite) {
			composite.TypeCodes.Clear();
			var compositeExecState = new FindHandlerState(new HandlerState(composite.BlockSigInfos, 0, 0));
			while (!compositeExecState.Done) {
				var handler = FindHandlerMethod(ref compositeExecState);
				if (handler == null)
					return false;

				composite.TypeCodes.Add(handler.TypeCode);
			}
			return composite.TypeCodes.Count != 0;
		}

		MethodSigInfo FindHandlerMethod(ref FindHandlerState findExecState) {
			foreach (var handler in handlers) {
				FindHandlerState findExecStateNew = findExecState.Clone();
				if (!Matches(handler.BlockSigInfos, ref findExecStateNew))
					continue;

				findExecState = findExecStateNew;
				return handler;
			}
			return null;
		}

		Stack<MatchState> stack = new Stack<MatchState>();
		bool Matches(List<BlockSigInfo> handler, ref FindHandlerState findState) {
			HandlerState? nextState = null;
			stack.Clear();
			stack.Push(new MatchState(new HandlerState(handler, 0, 0), findState.CompositeState));
			while (stack.Count > 0) {
				var matchState = stack.Pop();

				if (matchState.CompositeState.HashIndex == 0) {
					if (findState.VisitedCompositeBlocks.ContainsKey(matchState.CompositeState.BlockIndex))
						continue;
					findState.VisitedCompositeBlocks[matchState.CompositeState.BlockIndex] = true;
				}
				else {
					if (!findState.VisitedCompositeBlocks.ContainsKey(matchState.CompositeState.BlockIndex))
						throw new ApplicationException("Block hasn't been visited");
				}

				if (!Compare(ref matchState.OtherState, ref matchState.CompositeState))
					return false;

				var hblock = matchState.OtherState.BlockSigInfos[matchState.OtherState.BlockIndex];
				var hinstrs = hblock.Hashes;
				int hi = matchState.OtherState.HashIndex;
				var cblock = matchState.CompositeState.BlockSigInfos[matchState.CompositeState.BlockIndex];
				var cinstrs = cblock.Hashes;
				int ci = matchState.CompositeState.HashIndex;
				if (hi < hinstrs.Count)
					return false;

				if (ci < cinstrs.Count) {
					if (hblock.Targets.Count != 0)
						return false;
					if (hblock.EndsInRet) {
						if (nextState != null)
							return false;
						nextState = matchState.CompositeState;
					}
				}
				else {
					if (cblock.Targets.Count != hblock.Targets.Count)
						return false;
					if (cblock.HasFallThrough != hblock.HasFallThrough)
						return false;

					for (int i = 0; i < cblock.Targets.Count; i++) {
						var hs = new HandlerState(handler, hblock.Targets[i], 0);
						var cs = new HandlerState(findState.CompositeState.BlockSigInfos, cblock.Targets[i], 0);
						stack.Push(new MatchState(hs, cs));
					}
				}
			}

			if (nextState == null && findState.VisitedCompositeBlocks.Count != findState.CompositeState.BlockSigInfos.Count)
				nextState = GetNextHandlerState(ref findState);
			if (nextState == null) {
				if (findState.VisitedCompositeBlocks.Count != findState.CompositeState.BlockSigInfos.Count)
					return false;
				findState.Done = true;
				return true;
			}
			else {
				if (findState.CompositeState.BlockIndex == nextState.Value.BlockIndex &&
					findState.CompositeState.HashIndex == nextState.Value.HashIndex)
					return false;
				findState.CompositeState = nextState.Value;
				if (findState.CompositeState.HashIndex == 0)
					findState.VisitedCompositeBlocks.Remove(findState.CompositeState.BlockIndex);
				return true;
			}
		}

		static HandlerState? GetNextHandlerState(ref FindHandlerState findState) {
			for (int i = 0; i < findState.CompositeState.BlockSigInfos.Count; i++) {
				if (findState.VisitedCompositeBlocks.ContainsKey(i))
					continue;
				return new HandlerState(findState.CompositeState.BlockSigInfos, i, 0);
			}

			return null;
		}

		static bool Compare(ref HandlerState handler, ref HandlerState composite) {
			var hhashes = handler.BlockSigInfos[handler.BlockIndex].Hashes;
			int hi = handler.HashIndex;
			var chashes = composite.BlockSigInfos[composite.BlockIndex].Hashes;
			int ci = composite.HashIndex;

			while (true) {
				if (hi >= hhashes.Count && ci >= chashes.Count)
					break;

				if (hi >= hhashes.Count) {
					if (handler.BlockSigInfos[handler.BlockIndex].EndsInRet)
						break;
				}

				if (hi >= hhashes.Count || ci >= chashes.Count)
					return false;

				var hhash = hhashes[hi++];
				var chash = chashes[ci++];

				if (chash != hhash)
					return false;
			}

			handler.HashIndex = hi;
			composite.HashIndex = ci;
			return true;
		}
	}
}
