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

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class CompositeHandlerDetector {
		readonly List<OpCodeHandler> handlers;

		public CompositeHandlerDetector(IList<OpCodeHandler> handlers) {
			this.handlers = new List<OpCodeHandler>(handlers.Count);
			OpCodeHandler nop = null;
			foreach (var handler in handlers) {
				if (nop == null && handler.OpCodeHandlerInfo.TypeCode == HandlerTypeCode.Nop)
					nop = handler;
				else
					this.handlers.Add(handler);
			}
			if (nop != null)
				this.handlers.Add(nop);
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
			public readonly HandlerMethod HandlerMethod;
			public readonly IList<Block> Blocks;
			public readonly int BlockIndex;
			public int InstructionIndex;

			public HandlerState(HandlerMethod handlerMethod, int blockIndex, int instructionIndex) {
				this.HandlerMethod = handlerMethod;
				this.Blocks = handlerMethod.Blocks.MethodBlocks.GetAllBlocks();
				this.BlockIndex = blockIndex;
				this.InstructionIndex = instructionIndex;
			}

			public HandlerState(HandlerMethod handlerMethod, IList<Block> blocks, int blockIndex, int instructionIndex) {
				this.HandlerMethod = handlerMethod;
				this.Blocks = blocks;
				this.BlockIndex = blockIndex;
				this.InstructionIndex = instructionIndex;
			}

			public HandlerState Clone() {
				return new HandlerState(HandlerMethod, Blocks, BlockIndex, InstructionIndex);
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
			composite.OpCodeHandlerInfos.Clear();
			var compositeExecState = new FindHandlerState(new HandlerState(composite.ExecMethod, 0, 0));
			while (!compositeExecState.Done) {
				var handler = FindHandlerMethod(ref compositeExecState);
				if (handler == null)
					return false;

				composite.OpCodeHandlerInfos.Add(handler.OpCodeHandlerInfo);
			}
			return composite.OpCodeHandlerInfos.Count != 0;
		}

		OpCodeHandler FindHandlerMethod(ref FindHandlerState findExecState) {
			foreach (var handler in handlers) {
				FindHandlerState findExecStateNew = findExecState.Clone();
				if (!Matches(handler.ExecMethod, ref findExecStateNew))
					continue;

				findExecState = findExecStateNew;
				return handler;
			}
			return null;
		}

		Stack<MatchState> stack = new Stack<MatchState>();
		bool Matches(HandlerMethod handler, ref FindHandlerState findState) {
			HandlerState? nextState = null;
			stack.Clear();
			stack.Push(new MatchState(new HandlerState(handler, 0, 0), findState.CompositeState));
			while (stack.Count > 0) {
				var matchState = stack.Pop();

				if (matchState.CompositeState.InstructionIndex == 0) {
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

				var hblock = matchState.OtherState.Blocks[matchState.OtherState.BlockIndex];
				var hinstrs = hblock.Instructions;
				int hi = matchState.OtherState.InstructionIndex;
				var cblock = matchState.CompositeState.Blocks[matchState.CompositeState.BlockIndex];
				var cinstrs = cblock.Instructions;
				int ci = matchState.CompositeState.InstructionIndex;
				if (hi < hinstrs.Count)
					return false;

				if (ci < cinstrs.Count) {
					if (hblock.CountTargets() != 0)
						return false;
					if (hblock.LastInstr.OpCode.Code == Code.Ret) {
						if (nextState != null)
							return false;
						nextState = matchState.CompositeState;
					}
				}
				else {
					if (cblock.CountTargets() != hblock.CountTargets())
						return false;
					if (cblock.FallThrough != null || hblock.FallThrough != null) {
						if (cblock.FallThrough == null || hblock.FallThrough == null)
							return false;

						var hs = CreateHandlerState(handler, matchState.OtherState.Blocks, hblock.FallThrough);
						var cs = CreateHandlerState(findState.CompositeState.HandlerMethod, findState.CompositeState.Blocks, cblock.FallThrough);
						stack.Push(new MatchState(hs, cs));
					}
					if (cblock.Targets != null || hblock.Targets != null) {
						if (cblock.Targets == null || hblock.Targets == null ||
							cblock.Targets.Count != hblock.Targets.Count)
							return false;

						for (int i = 0; i < cblock.Targets.Count; i++) {
							var hs = CreateHandlerState(handler, matchState.OtherState.Blocks, hblock.Targets[i]);
							var cs = CreateHandlerState(findState.CompositeState.HandlerMethod, findState.CompositeState.Blocks, cblock.Targets[i]);
							stack.Push(new MatchState(hs, cs));
						}
					}
				}
			}

			if (nextState == null) {
				findState.Done = true;
				return true;
			}
			else {
				if (findState.CompositeState.BlockIndex == nextState.Value.BlockIndex &&
					findState.CompositeState.InstructionIndex == nextState.Value.InstructionIndex)
					return false;
				findState.CompositeState = nextState.Value;
				return true;
			}
		}

		static HandlerState CreateHandlerState(HandlerMethod handler, IList<Block> blocks, Block target) {
			return new HandlerState(handler, blocks.IndexOf(target), 0);
		}

		static bool Compare(ref HandlerState handler, ref HandlerState composite) {
			var hinstrs = handler.Blocks[handler.BlockIndex].Instructions;
			int hi = handler.InstructionIndex;
			var cinstrs = composite.Blocks[composite.BlockIndex].Instructions;
			int ci = composite.InstructionIndex;

			while (true) {
				if (hi >= hinstrs.Count && ci >= cinstrs.Count)
					break;
				if (hi >= hinstrs.Count || ci >= cinstrs.Count)
					return false;

				var hinstr = hinstrs[hi++];
				var cinstr = cinstrs[ci++];
				if (hinstr.OpCode.Code == Code.Nop ||
					cinstr.OpCode.Code == Code.Nop) {
					if (hinstr.OpCode.Code != Code.Nop)
						hi--;
					if (cinstr.OpCode.Code != Code.Nop)
						ci--;
					continue;
				}

				if (hi == hinstrs.Count && hinstr.OpCode.Code == Code.Ret) {
					if (cinstr.OpCode.Code != Code.Br && cinstr.OpCode.Code != Code.Ret)
						ci--;
					break;
				}

				if (hinstr.OpCode.Code != cinstr.OpCode.Code)
					return false;

				if (hinstr.OpCode.Code == Code.Ldfld &&
					hi + 1 < hinstrs.Count && ci + 1 < cinstrs.Count) {
					var hfield = hinstr.Operand as FieldDef;
					var cfield = cinstr.Operand as FieldDef;
					if (hfield != null && cfield != null &&
						!hfield.IsStatic && !cfield.IsStatic &&
						hfield.DeclaringType == handler.HandlerMethod.Method.DeclaringType &&
						cfield.DeclaringType == composite.HandlerMethod.Method.DeclaringType &&
						SignatureEqualityComparer.Instance.Equals(hfield.Signature, cfield.Signature)) {
						cinstr = cinstrs[ci++];
						hinstr = hinstrs[hi++];
						if (cinstr.OpCode.Code != Code.Ldc_I4 ||
							hinstr.OpCode.Code != Code.Ldc_I4)
							return false;
						continue;
					}
				}

				if (!CompareOperand(hinstr.OpCode.OperandType, cinstr.Operand, hinstr.Operand))
					return false;
			}

			handler.InstructionIndex = hi;
			composite.InstructionIndex = ci;
			return true;
		}

		static bool CompareOperand(OperandType opType, object a, object b) {
			switch (opType) {
			case OperandType.ShortInlineI:
				return (a is byte && b is byte && (byte)a == (byte)b) ||
					(a is sbyte && b is sbyte && (sbyte)a == (sbyte)b);

			case OperandType.InlineI:
				return a is int && b is int && (int)a == (int)b;

			case OperandType.InlineI8:
				return a is long && b is long && (long)a == (long)b;

			case OperandType.ShortInlineR:
				return a is float && b is float && (float)a == (float)b;

			case OperandType.InlineR:
				return a is double && b is double && (double)a == (double)b;

			case OperandType.InlineField:
				return FieldEqualityComparer.CompareDeclaringTypes.Equals(a as IField, b as IField);

			case OperandType.InlineMethod:
				return MethodEqualityComparer.CompareDeclaringTypes.Equals(a as IMethod, b as IMethod);

			case OperandType.InlineSig:
				return SignatureEqualityComparer.Instance.Equals(a as MethodSig, b as MethodSig);

			case OperandType.InlineString:
				return string.Equals(a as string, b as string);

			case OperandType.InlineSwitch:
				var al = a as IList<Instruction>;
				var bl = b as IList<Instruction>;
				return al != null && bl != null && al.Count == bl.Count;

			case OperandType.InlineTok:
				var fa = a as IField;
				var fb = b as IField;
				if (fa != null && fb != null)
					return FieldEqualityComparer.CompareDeclaringTypes.Equals(fa, fb);
				var ma = a as IMethod;
				var mb = b as IMethod;
				if (ma != null && mb != null)
					return MethodEqualityComparer.CompareDeclaringTypes.Equals(ma, mb);
				return TypeEqualityComparer.Instance.Equals(a as ITypeDefOrRef, b as ITypeDefOrRef);

			case OperandType.InlineType:
				return TypeEqualityComparer.Instance.Equals(a as ITypeDefOrRef, b as ITypeDefOrRef);

			case OperandType.InlineVar:
			case OperandType.ShortInlineVar:
				var la = a as Local;
				var lb = b as Local;
				if (la != null && lb != null)
					return true;
				var pa = a as Parameter;
				var pb = b as Parameter;
				return pa != null && pb != null && pa.Index == pb.Index;

			case OperandType.InlineBrTarget:
			case OperandType.ShortInlineBrTarget:
			case OperandType.InlineNone:
			case OperandType.InlinePhi:
				return true;

			default:
				return false;
			}
		}
	}
}
