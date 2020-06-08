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
using de4dot.blocks;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators {
	public class PushedArgs {
		List<Instruction> args;
		int nextIndex;

		public bool CanAddMore => nextIndex >= 0;
		public int NumValidArgs => args.Count - (nextIndex + 1);

		public PushedArgs(int numArgs) {
			nextIndex = numArgs - 1;
			args = new List<Instruction>(numArgs);
			for (int i = 0; i < numArgs; i++)
				args.Add(null);
		}

		public void Add(Instruction instr) => args[nextIndex--] = instr;
		public void Set(int i, Instruction instr) => args[i] = instr;
		internal void Pop() => args[++nextIndex] = null;

		public Instruction Get(int i) {
			if (0 <= i && i < args.Count)
				return args[i];
			return null;
		}

		public Instruction GetEnd(int i) => Get(args.Count - 1 - i);
	}

	public static class MethodStack {
		// May not return all args. The args are returned in reverse order.
		public static PushedArgs GetPushedArgInstructions(IList<Instruction> instructions, int index) {
			try {
				instructions[index].CalculateStackUsage(false, out int pushes, out int pops);
				if (pops != -1)
					return GetPushedArgInstructions(instructions, index, pops);
			}
			catch (System.NullReferenceException) {
				// Here if eg. invalid metadata token in a call instruction (operand is null)
			}
			return new PushedArgs(0);
		}

		// May not return all args. The args are returned in reverse order.
		static PushedArgs GetPushedArgInstructions(IList<Instruction> instructions, int index, int numArgs) {
			var pushedArgs = new PushedArgs(numArgs);
			if (!pushedArgs.CanAddMore) return pushedArgs;

			Dictionary<int, Branch> branches = null;
			var states = new Stack<State>();
			var state = new State(index, null, 0, 0, 1, new HashSet<int>());
			var isBacktrack = false;
			states.Push(state.Clone());
			while (true) {
				while (state.index >= 0) {
					if (branches != null && branches.TryGetValue(state.index, out var branch) && state.visited.Add(state.index)) {
						branch.current = 0;
						var brState = state.Clone();
						brState.branch = branch;
						states.Push(brState);
					}
					if (!isBacktrack)
						state.index--;
					isBacktrack = false;
					var update = UpdateState(instructions, state, pushedArgs);
					if (update == Update.Finish)
						return pushedArgs;
					if (update == Update.Fail)
						break;
				}

				if (states.Count == 0)
					return pushedArgs;

				var prevValidArgs = state.validArgs;
				state = states.Pop();
				if (state.validArgs < prevValidArgs)
					for (int i = state.validArgs + 1; i <= prevValidArgs; i++)
						pushedArgs.Pop();

				if (branches == null)
					branches = GetBranches(instructions);
				else {
					isBacktrack = true;
					state.index = state.branch.Variants[state.branch.current++];
					if (state.branch.current < state.branch.Variants.Count)
						states.Push(state.Clone());
					else
						state.branch = null;
				}
			}

		}

		class Branch {
			public int current;
			public List<int> Variants { get; }
			public Branch() => Variants = new List<int>();
		}

		class State {
			public int index;
			public Branch branch;
			public int validArgs;
			public int skipPushes;
			public int addPushes;
			public HashSet<int> visited;

			public State(int index, Branch branch, int validArgs, int skipPushes, int addPushes, HashSet<int> visited) {
				this.index = index;
				this.branch = branch;
				this.validArgs = validArgs;
				this.skipPushes = skipPushes;
				this.addPushes = addPushes;
				this.visited = visited;
			}

			public State Clone() => new State(index, branch, validArgs, skipPushes, addPushes, new HashSet<int>(visited));
		}

		enum Update { Ok, Fail, Finish };

		private static Update UpdateState(IList<Instruction> instructions, State state, PushedArgs pushedArgs) {
			if (state.index < 0 || state.index >= instructions.Count)
				return Update.Fail;
			var instr = instructions[state.index];
			if (!Instr.IsFallThrough(instr.OpCode))
				return Update.Fail;
			instr.CalculateStackUsage(false, out int pushes, out int pops);
			if (pops == -1)
				return Update.Fail;
			var isDup = instr.OpCode.Code == Code.Dup;
			if (isDup) {
				pushes = 1;
				pops = 0;
			}
			if (pushes > 1)
				return Update.Fail;

			if (state.skipPushes > 0) {
				state.skipPushes -= pushes;
				if (state.skipPushes < 0)
					return Update.Fail;
				state.skipPushes += pops;
			}
			else {
				if (pushes == 1) {
					if (isDup)
						state.addPushes++;
					else {
						for (; state.addPushes > 0; state.addPushes--) {
							pushedArgs.Add(instr);
							state.validArgs++;
							if (!pushedArgs.CanAddMore)
								return Update.Finish;
						}
						state.addPushes = 1;
					}
				}
				state.skipPushes += pops;
			}
			return Update.Ok;
		}

		private static IList<Instruction> CacheInstructions = null;
		private static Dictionary<int, Branch> CacheBranches = null;

		// cache last branches based on instructions object
		private static Dictionary<int, Branch> GetBranches(IList<Instruction> instructions) {
			if (CacheInstructions == instructions) return CacheBranches;
			CacheInstructions = instructions;
			CacheBranches = new Dictionary<int, Branch>();
			for (int b = 0; b < instructions.Count; b++) {
				var br = instructions[b];
				if (br.Operand is Instruction target) {
					var t = instructions.IndexOf(target);
					if (!CacheBranches.TryGetValue(t, out var branch)) {
						branch = new Branch();
						CacheBranches.Add(t, branch);
					}
					branch.Variants.Add(b);
				}
			}
			return CacheBranches;
		}

		public static TypeSig GetLoadedType(MethodDef method, IList<Instruction> instructions, int instrIndex) =>
			GetLoadedType(method, instructions, instrIndex, 0, out bool wasNewobj);
		public static TypeSig GetLoadedType(MethodDef method, IList<Instruction> instructions, int instrIndex, int argIndexFromEnd) =>
			GetLoadedType(method, instructions, instrIndex, argIndexFromEnd, out bool wasNewobj);
		public static TypeSig GetLoadedType(MethodDef method, IList<Instruction> instructions, int instrIndex, out bool wasNewobj) =>
			GetLoadedType(method, instructions, instrIndex, 0, out wasNewobj);

		public static TypeSig GetLoadedType(MethodDef method, IList<Instruction> instructions, int instrIndex, int argIndexFromEnd, out bool wasNewobj) {
			wasNewobj = false;
			var pushedArgs = MethodStack.GetPushedArgInstructions(instructions, instrIndex);
			var pushInstr = pushedArgs.GetEnd(argIndexFromEnd);
			if (pushInstr == null)
				return null;

			TypeSig type;
			Local local;
			var corLibTypes = method.DeclaringType.Module.CorLibTypes;
			switch (pushInstr.OpCode.Code) {
			case Code.Ldstr:
				type = corLibTypes.String;
				break;

			case Code.Conv_I:
			case Code.Conv_Ovf_I:
			case Code.Conv_Ovf_I_Un:
				type = corLibTypes.IntPtr;
				break;

			case Code.Conv_U:
			case Code.Conv_Ovf_U:
			case Code.Conv_Ovf_U_Un:
				type = corLibTypes.UIntPtr;
				break;

			case Code.Conv_I8:
			case Code.Conv_Ovf_I8:
			case Code.Conv_Ovf_I8_Un:
				type = corLibTypes.Int64;
				break;

			case Code.Conv_U8:
			case Code.Conv_Ovf_U8:
			case Code.Conv_Ovf_U8_Un:
				type = corLibTypes.UInt64;
				break;

			case Code.Conv_R8:
			case Code.Ldc_R8:
			case Code.Ldelem_R8:
			case Code.Ldind_R8:
				type = corLibTypes.Double;
				break;

			case Code.Call:
			case Code.Calli:
			case Code.Callvirt:
				var calledMethod = pushInstr.Operand as IMethod;
				if (calledMethod == null)
					return null;
				type = calledMethod.MethodSig.GetRetType();
				break;

			case Code.Newarr:
				var type2 = pushInstr.Operand as ITypeDefOrRef;
				if (type2 == null)
					return null;
				type = new SZArraySig(type2.ToTypeSig());
				wasNewobj = true;
				break;

			case Code.Newobj:
				var ctor = pushInstr.Operand as IMethod;
				if (ctor == null)
					return null;
				type = ctor.DeclaringType.ToTypeSig();
				wasNewobj = true;
				break;

			case Code.Castclass:
			case Code.Isinst:
			case Code.Unbox_Any:
			case Code.Ldelem:
			case Code.Ldobj:
				type = (pushInstr.Operand as ITypeDefOrRef).ToTypeSig();
				break;

			case Code.Ldarg:
			case Code.Ldarg_S:
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
				type = pushInstr.GetArgumentType(method.MethodSig, method.DeclaringType);
				break;

			case Code.Ldloc:
			case Code.Ldloc_S:
			case Code.Ldloc_0:
			case Code.Ldloc_1:
			case Code.Ldloc_2:
			case Code.Ldloc_3:
				local = pushInstr.GetLocal(method.Body.Variables);
				if (local == null)
					return null;
				type = local.Type.RemovePinned();
				break;

			case Code.Ldloca:
			case Code.Ldloca_S:
				local = pushInstr.Operand as Local;
				if (local == null)
					return null;
				type = CreateByRefType(local.Type.RemovePinned());
				break;

			case Code.Ldarga:
			case Code.Ldarga_S:
				type = CreateByRefType(pushInstr.GetArgumentType(method.MethodSig, method.DeclaringType));
				break;

			case Code.Ldfld:
			case Code.Ldsfld:
				var field = pushInstr.Operand as IField;
				if (field == null || field.FieldSig == null)
					return null;
				type = field.FieldSig.GetFieldType();
				break;

			case Code.Ldflda:
			case Code.Ldsflda:
				var field2 = pushInstr.Operand as IField;
				if (field2 == null || field2.FieldSig == null)
					return null;
				type = CreateByRefType(field2.FieldSig.GetFieldType());
				break;

			case Code.Ldelema:
			case Code.Unbox:
				type = CreateByRefType(pushInstr.Operand as ITypeDefOrRef);
				break;

			default:
				return null;
			}

			return type;
		}

		static ByRefSig CreateByRefType(ITypeDefOrRef elementType) {
			if (elementType == null)
				return null;
			return new ByRefSig(elementType.ToTypeSig());
		}

		static ByRefSig CreateByRefType(TypeSig elementType) {
			if (elementType == null)
				return null;
			return new ByRefSig(elementType);
		}

	}
}
