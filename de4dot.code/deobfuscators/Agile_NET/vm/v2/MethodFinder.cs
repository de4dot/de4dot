/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class MethodFinder {
		readonly IList<OpCodeHandlerInfo> handlerInfos;
		readonly PrimitiveHandlerMethod handlerMethod;

		class SigState {
			public readonly MethodSigInfo SigInfo;

			public SigState(PrimitiveHandlerMethod handlerMethod) {
				this.SigInfo = handlerMethod.Sig;
			}
		}

		public MethodFinder(IList<OpCodeHandlerInfo> handlerInfos, PrimitiveHandlerMethod handlerMethod) {
			this.handlerInfos = handlerInfos;
			this.handlerMethod = handlerMethod;
		}

		public OpCodeHandler FindHandler() {
			var handler = FindHandler(new SigState(handlerMethod));
			if (handler == null)
				return null;

			return new OpCodeHandler(handler, handlerMethod.Method.DeclaringType, handlerMethod);
		}

		OpCodeHandlerInfo FindHandler(SigState execSigState) {
			foreach (var handler in handlerInfos) {
				if (Matches(handler.ExecSig, execSigState))
					return handler;
			}
			return null;
		}

		struct MatchInfo {
			public int HandlerIndex;
			public int SigIndex;

			public MatchInfo(int handlerIndex, int sigIndex) {
				this.HandlerIndex = handlerIndex;
				this.SigIndex = sigIndex;
			}
		}

		Dictionary<int, int> sigIndexToHandlerIndex = new Dictionary<int, int>();
		Dictionary<int, int> handlerIndexToSigIndex = new Dictionary<int, int>();
		Stack<MatchInfo> stack = new Stack<MatchInfo>();
		bool Matches(MethodSigInfo handlerSig, SigState sigState) {
			stack.Clear();
			sigIndexToHandlerIndex.Clear();
			handlerIndexToSigIndex.Clear();
			var handlerInfos = handlerSig.BlockInfos;
			var sigInfos = sigState.SigInfo.BlockInfos;

			stack.Push(new MatchInfo(0, 0));
			while (stack.Count > 0) {
				var info = stack.Pop();

				int handlerIndex, sigIndex;
				bool hasVisitedHandler = handlerIndexToSigIndex.TryGetValue(info.HandlerIndex, out sigIndex);
				bool hasVisitedSig = sigIndexToHandlerIndex.TryGetValue(info.SigIndex, out handlerIndex);
				if (hasVisitedHandler != hasVisitedSig)
					return false;
				if (hasVisitedHandler) {
					if (handlerIndex != info.HandlerIndex || sigIndex != info.SigIndex)
						return false;
					continue;
				}
				handlerIndexToSigIndex[info.HandlerIndex] = info.SigIndex;
				sigIndexToHandlerIndex[info.SigIndex] = info.HandlerIndex;

				var handlerBlock = handlerInfos[info.HandlerIndex];
				var sigBlock = sigInfos[info.SigIndex];

				if (!handlerBlock.Equals(sigBlock))
					return false;

				for (int i = 0; i < handlerBlock.Targets.Count; i++) {
					int handlerTargetIndex = handlerBlock.Targets[i];
					int sigTargetIndex = sigBlock.Targets[i];
					stack.Push(new MatchInfo(handlerTargetIndex, sigTargetIndex));
				}
			}

			return true;
		}
	}
}
