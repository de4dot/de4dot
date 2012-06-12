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
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Babel_NET {
	class BabelMethodCallInliner : MethodCallInlinerBase, IBranchHandler {
		InstructionEmulator emulator;
		BranchEmulator branchEmulator;
		int emulateIndex;
		IList<Instruction> instructions;

		public BabelMethodCallInliner() {
			emulator = new InstructionEmulator();
			branchEmulator = new BranchEmulator(emulator, this);
		}

		public static List<MethodDefinition> find(ModuleDefinition module, IEnumerable<MethodDefinition> notInlinedMethods) {
			var notInlinedMethodsDict = new Dictionary<MethodDefinition, bool>();
			foreach (var method in notInlinedMethods)
				notInlinedMethodsDict[method] = true;

			var inlinedMethods = new List<MethodDefinition>();

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					if (!notInlinedMethodsDict.ContainsKey(method) && canInline(method))
						inlinedMethods.Add(method);
				}
			}

			return inlinedMethods;
		}

		void IBranchHandler.handleNormal(int stackArgs, bool isTaken) {
			if (!isTaken)
				emulateIndex++;
			else
				emulateIndex = instructions.IndexOf((Instruction)instructions[emulateIndex].Operand);
		}

		bool IBranchHandler.handleSwitch(Int32Value switchIndex) {
			if (!switchIndex.allBitsValid())
				return false;
			var instr = instructions[emulateIndex];
			var targets = (Instruction[])instr.Operand;
			if (switchIndex.value >= 0 && switchIndex.value < targets.Length)
				emulateIndex = instructions.IndexOf(targets[switchIndex.value]);
			else
				emulateIndex++;
			return true;
		}

		protected override bool deobfuscateInternal() {
			bool changed = false;
			var instructions = block.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instr = instructions[i].Instruction;
				if (instr.OpCode.Code == Code.Call)
					changed |= inlineMethod(instr, i);
			}
			instructions = null;
			return changed;
		}

		static bool canInline(MethodDefinition method) {
			if (!DotNetUtils.isMethod(method, "System.Int32", "(System.Int32)"))
				return false;
			if (!method.IsAssembly)
				return false;
			if (method.GenericParameters.Count > 0)
				return false;

			return method.IsStatic;
		}

		bool canInline2(MethodDefinition method) {
			return canInline(method) && method != blocks.Method;
		}

		bool inlineMethod(Instruction callInstr, int instrIndex) {
			var methodToInline = callInstr.Operand as MethodDefinition;
			if (methodToInline == null)
				return false;

			if (!canInline2(methodToInline))
				return false;
			var body = methodToInline.Body;
			if (body == null)
				return false;

			if (instrIndex == 0)
				return false;

			var ldci4 = block.Instructions[instrIndex - 1];
			if (!ldci4.isLdcI4())
				return false;
			int newValue;
			if (!getNewValue(methodToInline, ldci4.getLdcI4Value(), out newValue))
				return false;

			block.Instructions[instrIndex - 1] = new Instr(Instruction.Create(OpCodes.Nop));
			block.Instructions[instrIndex] = new Instr(DotNetUtils.createLdci4(newValue));
			return true;
		}

		bool getNewValue(MethodDefinition method, int arg, out int newValue) {
			newValue = 0;
			emulator.init(method);
			emulator.setArg(method.Parameters[0], new Int32Value(arg));

			Instruction instr;
			emulateIndex = 0;
			instructions = method.Body.Instructions;
			int counter = 0;
			while (true) {
				if (counter++ >= 50)
					return false;
				if (emulateIndex < 0 || emulateIndex >= instructions.Count)
					return false;
				instr = instructions[emulateIndex];
				switch (instr.OpCode.Code) {
				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
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
				case Code.Add:
				case Code.Sub:
				case Code.Xor:
				case Code.Or:
				case Code.Nop:
				case Code.Dup:
				case Code.Mul:
				case Code.Rem:
				case Code.Div:
					emulator.emulate(instr);
					emulateIndex++;
					break;

				case Code.Br:
				case Code.Br_S:
				case Code.Beq:
				case Code.Beq_S:
				case Code.Bge:
				case Code.Bge_S:
				case Code.Bge_Un:
				case Code.Bge_Un_S:
				case Code.Bgt:
				case Code.Bgt_S:
				case Code.Bgt_Un:
				case Code.Bgt_Un_S:
				case Code.Ble:
				case Code.Ble_S:
				case Code.Ble_Un:
				case Code.Ble_Un_S:
				case Code.Blt:
				case Code.Blt_S:
				case Code.Blt_Un:
				case Code.Blt_Un_S:
				case Code.Bne_Un:
				case Code.Bne_Un_S:
				case Code.Brfalse:
				case Code.Brfalse_S:
				case Code.Brtrue:
				case Code.Brtrue_S:
				case Code.Switch:
					if (!branchEmulator.emulate(instr))
						return false;
					break;

				case Code.Ret:
					var retValue = emulator.pop();
					if (!retValue.isInt32())
						return false;
					var retValue2 = (Int32Value)retValue;
					if (!retValue2.allBitsValid())
						return false;
					newValue = retValue2.value;
					return true;

				default:
					if (instr.OpCode.OpCodeType != OpCodeType.Prefix)
						return false;
					emulateIndex++;
					break;
				}
			}
		}

		protected override bool isCompatibleType(int paramIndex, TypeReference origType, TypeReference newType) {
			if (MemberReferenceHelper.compareTypes(origType, newType))
				return true;
			if (newType.IsValueType || origType.IsValueType)
				return false;
			return newType.FullName == "System.Object";
		}
	}
}
