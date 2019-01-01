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

		public Instruction Get(int i) {
			if (0 <= i && i < args.Count)
				return args[i];
			return null;
		}

		public Instruction GetEnd(int i) => Get(args.Count - 1 - i);

		public void FixDups() {
			Instruction prev = null, instr;
			for (int i = 0; i < NumValidArgs; i++, prev = instr) {
				instr = args[i];
				if (instr == null || prev == null)
					continue;
				if (instr.OpCode.Code != Code.Dup)
					continue;
				args[i] = prev;
				instr = prev;
			}
		}
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

			Instruction instr;
			int skipPushes = 0;
			while (index >= 0 && pushedArgs.CanAddMore) {
				instr = GetPreviousInstruction(instructions, ref index);
				if (instr == null)
					break;

				instr.CalculateStackUsage(false, out int pushes, out int pops);
				if (pops == -1)
					break;
				if (instr.OpCode.Code == Code.Dup) {
					pushes = 1;
					pops = 0;
				}
				if (pushes > 1)
					break;

				if (skipPushes > 0) {
					skipPushes -= pushes;
					if (skipPushes < 0)
						break;
					skipPushes += pops;
				}
				else {
					if (pushes == 1)
						pushedArgs.Add(instr);
					skipPushes += pops;
				}
			}
			instr = pushedArgs.Get(0);
			if (instr != null && instr.OpCode.Code == Code.Dup) {
				instr = GetPreviousInstruction(instructions, ref index);
				if (instr != null) {
					instr.CalculateStackUsage(false, out int pushes, out int pops);
					if (pushes == 1 && pops == 0)
						pushedArgs.Set(0, instr);
				}
			}
			pushedArgs.FixDups();

			return pushedArgs;
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

		static Instruction GetPreviousInstruction(IList<Instruction> instructions, ref int instrIndex) {
			while (true) {
				instrIndex--;
				if (instrIndex < 0)
					return null;
				var instr = instructions[instrIndex];
				if (instr.OpCode.Code == Code.Nop)
					continue;
				if (instr.OpCode.OpCodeType == OpCodeType.Prefix)
					continue;
				switch (instr.OpCode.FlowControl) {
				case FlowControl.Next:
				case FlowControl.Call:
					return instr;
				default:
					return null;
				}
			}
		}
	}
}
