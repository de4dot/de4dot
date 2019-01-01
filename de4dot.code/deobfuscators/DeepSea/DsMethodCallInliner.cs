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
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.DeepSea {
	class DsMethodCallInliner : MethodCallInlinerBase {
		InstructionEmulator instructionEmulator = new InstructionEmulator();
		IList<Parameter> parameters;
		Parameter arg1, arg2;
		Value returnValue;
		MethodDef methodToInline;
		CachedCflowDeobfuscator cflowDeobfuscator;

		public DsMethodCallInliner(CachedCflowDeobfuscator cflowDeobfuscator) =>
			this.cflowDeobfuscator = cflowDeobfuscator;

		protected override bool DeobfuscateInternal() {
			bool modified = false;

			var instructions = block.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instr = instructions[i].Instruction;
				if (instr.OpCode.Code == Code.Call)
					modified |= InlineMethod(instr, i);
			}

			return modified;
		}

		bool InlineMethod(Instruction callInstr, int instrIndex) {
			var method = callInstr.Operand as MethodDef;
			if (method == null) {
				if (callInstr.Operand is MethodSpec ms)
					method = ms.Method as MethodDef;
				if (method == null)
					return false;
			}
			if (!CanInline(method))
				return false;

			if (instrIndex < 2)
				return false;
			var ldci4_1st = block.Instructions[instrIndex - 2];
			var ldci4_2nd = block.Instructions[instrIndex - 1];
			if (!ldci4_1st.IsLdcI4() || !ldci4_2nd.IsLdcI4())
				return false;

			if (!InlineMethod(method, instrIndex, ldci4_1st.GetLdcI4Value(), ldci4_2nd.GetLdcI4Value()))
				return false;

			return true;
		}

		protected override Instruction OnAfterLoadArg(MethodDef methodToInline, Instruction instr, ref int instrIndex) {
			if (instr.OpCode.Code != Code.Box)
				return instr;
			if (methodToInline.MethodSig.GetGenParamCount() == 0)
				return instr;
			return DotNetUtils.GetInstruction(methodToInline.Body.Instructions, ref instrIndex);
		}

		bool InlineMethod(MethodDef methodToInline, int instrIndex, int const1, int const2) {
			this.methodToInline = methodToInline = cflowDeobfuscator.Deobfuscate(methodToInline);

			parameters = methodToInline.Parameters;
			arg1 = parameters[parameters.Count - 2];
			arg2 = parameters[parameters.Count - 1];
			returnValue = null;

			instructionEmulator.Initialize(methodToInline);
			foreach (var arg in parameters) {
				if (!arg.IsNormalMethodParameter)
					continue;
				if (arg.Type.ElementType >= ElementType.Boolean && arg.Type.ElementType <= ElementType.U4)
					instructionEmulator.SetArg(arg, new Int32Value(0));
			}
			instructionEmulator.SetArg(arg1, new Int32Value(const1));
			instructionEmulator.SetArg(arg2, new Int32Value(const2));

			int index = 0;
			if (!EmulateInstructions(ref index, false))
				return false;
			var patcher = TryInlineOtherMethod(instrIndex, methodToInline, methodToInline.Body.Instructions[index], index + 1, 2);
			if (patcher == null)
				return false;
			if (!EmulateToReturn(patcher.afterIndex, patcher.lastInstr))
				return false;
			patcher.Patch(block);
			block.Insert(instrIndex, OpCodes.Pop.ToInstruction());
			block.Insert(instrIndex, OpCodes.Pop.ToInstruction());
			return true;
		}

		bool EmulateInstructions(ref int index, bool allowUnknownArgs) {
			Instruction instr;
			var instrs = methodToInline.Body.Instructions;
			int counter = 0;
			var foundOpCodes = new Dictionary<Code, bool>();
			bool checkInstrs = false;
			while (true) {
				if (counter++ >= 50)
					return false;
				if (index < 0 || index >= instrs.Count)
					return false;
				instr = instrs[index];
				foundOpCodes[instr.OpCode.Code] = true;
				switch (instr.OpCode.Code) {
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
					instructionEmulator.Emulate(instr);
					index++;
					break;

				case Code.Ldarg:
				case Code.Ldarg_S:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
					var arg = instr.GetParameter(parameters);
					if (arg != arg1 && arg != arg2) {
						if (!allowUnknownArgs)
							goto done;
						checkInstrs = true;
					}
					instructionEmulator.Emulate(instr);
					index++;
					break;

				case Code.Call:
				case Code.Callvirt:
				case Code.Newobj:
					goto done;

				case Code.Switch:
					var value = instructionEmulator.Pop() as Int32Value;
					if (value == null || !value.AllBitsValid())
						return false;
					var targets = (Instruction[])instr.Operand;
					if (value.Value >= 0 && value.Value < targets.Length)
						index = instrs.IndexOf(targets[value.Value]);
					else
						index++;
					break;

				case Code.Br:
				case Code.Br_S:
					index = instrs.IndexOf((Instruction)instr.Operand);
					break;

				case Code.Brtrue:
				case Code.Brtrue_S:
					index = EmulateBrtrue(index);
					break;

				case Code.Brfalse:
				case Code.Brfalse_S:
					index = EmulateBrfalse(index);
					break;

				case Code.Isinst:
				case Code.Castclass:
					if (returnValue != null && instructionEmulator.Peek() == returnValue) {
						// Do nothing
					}
					else
						instructionEmulator.Emulate(instr);
					index++;
					break;

				default:
					if (instr.OpCode.OpCodeType != OpCodeType.Prefix)
						goto done;
					index++;
					break;
				}
			}
done:
			if (checkInstrs) {
				if (!foundOpCodes.ContainsKey(Code.Ldc_I4_1))
					return false;
				if (!foundOpCodes.ContainsKey(Code.Ldc_I4_2))
					return false;
				if (!foundOpCodes.ContainsKey(Code.Add))
					return false;
				if (!foundOpCodes.ContainsKey(Code.Dup))
					return false;
				if (!foundOpCodes.ContainsKey(Code.Mul))
					return false;
				if (!foundOpCodes.ContainsKey(Code.Rem))
					return false;
				if (!foundOpCodes.ContainsKey(Code.Brtrue) && !foundOpCodes.ContainsKey(Code.Brtrue_S) &&
					!foundOpCodes.ContainsKey(Code.Brfalse) && !foundOpCodes.ContainsKey(Code.Brfalse_S))
					return false;
			}
			return true;
		}

		int EmulateBranch(int stackArgs, Bool3 cond, Instruction instrTrue, Instruction instrFalse) {
			if (cond == Bool3.Unknown)
				return -1;
			var instr = cond == Bool3.True ? instrTrue : instrFalse;
			return methodToInline.Body.Instructions.IndexOf(instr);
		}

		int EmulateBrtrue(int instrIndex) {
			var val1 = instructionEmulator.Pop();

			var instr = methodToInline.Body.Instructions[instrIndex];
			var instrTrue = (Instruction)instr.Operand;
			var instrFalse = methodToInline.Body.Instructions[instrIndex + 1];

			if (val1.IsInt32())
				return EmulateBranch(1, Int32Value.CompareTrue((Int32Value)val1), instrTrue, instrFalse);
			return -1;
		}

		int EmulateBrfalse(int instrIndex) {
			var val1 = instructionEmulator.Pop();

			var instr = methodToInline.Body.Instructions[instrIndex];
			var instrTrue = (Instruction)instr.Operand;
			var instrFalse = methodToInline.Body.Instructions[instrIndex + 1];

			if (val1.IsInt32())
				return EmulateBranch(1, Int32Value.CompareFalse((Int32Value)val1), instrTrue, instrFalse);
			return -1;
		}

		bool EmulateToReturn(int index, Instruction lastInstr) {
			lastInstr.CalculateStackUsage(false, out int pushes, out int pops);
			instructionEmulator.Pop(pops);

			returnValue = null;
			if (pushes != 0) {
				returnValue = new UnknownValue();
				instructionEmulator.SetProtected(returnValue);
				instructionEmulator.Push(returnValue);
			}

			if (!EmulateInstructions(ref index, true))
				return false;
			if (index >= methodToInline.Body.Instructions.Count)
				return false;
			if (methodToInline.Body.Instructions[index].OpCode.Code != Code.Ret)
				return false;

			if (returnValue != null) {
				if (instructionEmulator.Pop() != returnValue)
					return false;
			}
			return instructionEmulator.StackSize() == 0;
		}

		public static bool CanInline(MethodDef method) {
			if (method == null || method.Body == null)
				return false;
			if (method.Attributes != (MethodAttributes.Assembly | MethodAttributes.Static))
				return false;
			if (method.Body.ExceptionHandlers.Count > 0)
				return false;

			var parameters = method.MethodSig.GetParams();
			int paramCount = parameters.Count;
			if (paramCount < 2)
				return false;

			if (method.GenericParameters.Count > 0) {
				foreach (var gp in method.GenericParameters) {
					if (gp.GenericParamConstraints.Count == 0)
						return false;
				}
			}

			var param1 = parameters[paramCount - 1];
			var param2 = parameters[paramCount - 2];
			if (!IsIntType(param1.ElementType))
				return false;
			if (!IsIntType(param2.ElementType))
				return false;

			return true;
		}

		static bool IsIntType(ElementType etype) =>
			etype == ElementType.Char || etype == ElementType.I2 || etype == ElementType.I4;

		protected override bool IsReturn(MethodDef methodToInline, int instrIndex) {
			int oldIndex = instrIndex;
			if (base.IsReturn(methodToInline, oldIndex))
				return true;

			return false;
		}
	}
}
