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
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	class PushedArgs {
		List<Instruction> args;
		int nextIndex;

		public bool CanAddMore {
			get { return nextIndex >= 0; }
		}

		public int NumValidArgs {
			get { return args.Count - (nextIndex + 1); }
		}

		public PushedArgs(int numArgs) {
			nextIndex = numArgs - 1;
			args = new List<Instruction>(numArgs);
			for (int i = 0; i < numArgs; i++)
				args.Add(null);
		}

		public void add(Instruction instr) {
			args[nextIndex--] = instr;
		}

		public void set(int i, Instruction instr) {
			args[i] = instr;
		}

		public Instruction get(int i) {
			if (0 <= i && i < args.Count)
				return args[i];
			return null;
		}

		public Instruction getEnd(int i) {
			return get(args.Count - 1 - i);
		}

		public void fixDups() {
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

	static class MethodStack {
		// May not return all args. The args are returned in reverse order.
		public static PushedArgs getPushedArgInstructions(IList<Instruction> instructions, int index) {
			try {
				int pushes, pops;
				DotNetUtils.calculateStackUsage(instructions[index], false, out pushes, out pops);
				if (pops != -1)
					return getPushedArgInstructions(instructions, index, pops);
			}
			catch (System.NullReferenceException) {
				// Here if eg. invalid metadata token in a call instruction (operand is null)
			}
			return new PushedArgs(0);
		}

		// May not return all args. The args are returned in reverse order.
		static PushedArgs getPushedArgInstructions(IList<Instruction> instructions, int index, int numArgs) {
			var pushedArgs = new PushedArgs(numArgs);

			Instruction instr;
			int skipPushes = 0;
			while (index >= 0 && pushedArgs.CanAddMore) {
				instr = getPreviousInstruction(instructions, ref index);
				if (instr == null)
					break;

				int pushes, pops;
				DotNetUtils.calculateStackUsage(instr, false, out pushes, out pops);
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
						pushedArgs.add(instr);
					skipPushes += pops;
				}
			}
			instr = pushedArgs.get(0);
			if (instr != null && instr.OpCode.Code == Code.Dup) {
				instr = getPreviousInstruction(instructions, ref index);
				if (instr != null) {
					int pushes, pops;
					DotNetUtils.calculateStackUsage(instr, false, out pushes, out pops);
					if (pushes == 1 && pops == 0)
						pushedArgs.set(0, instr);
				}
			}
			pushedArgs.fixDups();

			return pushedArgs;
		}

		public static TypeReference getLoadedType(MethodDefinition method, IList<Instruction> instructions, int instrIndex) {
			bool wasNewobj;
			return getLoadedType(method, instructions, instrIndex, 0, out wasNewobj);
		}

		public static TypeReference getLoadedType(MethodDefinition method, IList<Instruction> instructions, int instrIndex, int argIndexFromEnd) {
			bool wasNewobj;
			return getLoadedType(method, instructions, instrIndex, argIndexFromEnd, out wasNewobj);
		}

		public static TypeReference getLoadedType(MethodDefinition method, IList<Instruction> instructions, int instrIndex, out bool wasNewobj) {
			return getLoadedType(method, instructions, instrIndex, 0, out wasNewobj);
		}

		public static TypeReference getLoadedType(MethodDefinition method, IList<Instruction> instructions, int instrIndex, int argIndexFromEnd, out bool wasNewobj) {
			wasNewobj = false;
			var pushedArgs = MethodStack.getPushedArgInstructions(instructions, instrIndex);
			var pushInstr = pushedArgs.getEnd(argIndexFromEnd);
			if (pushInstr == null)
				return null;

			TypeReference fieldType;
			VariableDefinition local;
			switch (pushInstr.OpCode.Code) {
			case Code.Ldstr:
				fieldType = method.Module.TypeSystem.String;
				break;

			case Code.Call:
			case Code.Calli:
			case Code.Callvirt:
				var calledMethod = pushInstr.Operand as MethodReference;
				if (calledMethod == null)
					return null;
				fieldType = calledMethod.MethodReturnType.ReturnType;
				break;

			case Code.Newarr:
				fieldType = pushInstr.Operand as TypeReference;
				if (fieldType == null)
					return null;
				fieldType = new ArrayType(fieldType);
				wasNewobj = true;
				break;

			case Code.Newobj:
				var ctor = pushInstr.Operand as MethodReference;
				if (ctor == null)
					return null;
				fieldType = ctor.DeclaringType;
				wasNewobj = true;
				break;

			case Code.Castclass:
			case Code.Isinst:
			case Code.Unbox_Any:
				fieldType = pushInstr.Operand as TypeReference;
				break;

			case Code.Ldarg:
			case Code.Ldarg_S:
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
				fieldType = DotNetUtils.getArgType(method, pushInstr);
				break;

			case Code.Ldloc:
			case Code.Ldloc_S:
			case Code.Ldloc_0:
			case Code.Ldloc_1:
			case Code.Ldloc_2:
			case Code.Ldloc_3:
				local = DotNetUtils.getLocalVar(method.Body.Variables, pushInstr);
				if (local == null)
					return null;
				fieldType = local.VariableType;
				break;

			case Code.Ldloca:
			case Code.Ldloca_S:
				local = pushInstr.Operand as VariableDefinition;
				if (local == null)
					return null;
				fieldType = createByReferenceType(local.VariableType);
				break;

			case Code.Ldarga:
			case Code.Ldarga_S:
				fieldType = createByReferenceType(DotNetUtils.getArgType(method, pushInstr));
				break;

			case Code.Ldfld:
			case Code.Ldsfld:
				var field2 = pushInstr.Operand as FieldReference;
				if (field2 == null)
					return null;
				fieldType = field2.FieldType;
				break;

			case Code.Ldelema:
				fieldType = createByReferenceType(pushInstr.Operand as TypeReference);
				break;

			case Code.Ldobj:
				fieldType = pushInstr.Operand as TypeReference;
				break;

			default:
				return null;
			}

			return fieldType;
		}

		static ByReferenceType createByReferenceType(TypeReference elementType) {
			if (elementType == null)
				return null;
			return new ByReferenceType(elementType);
		}

		static Instruction getPreviousInstruction(IList<Instruction> instructions, ref int instrIndex) {
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
