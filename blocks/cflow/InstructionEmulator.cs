/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace de4dot.blocks.cflow {
	class InstructionEmulator {
		ValueStack valueStack = new ValueStack();
		IList<ParameterDefinition> parameterDefinitions;
		IList<VariableDefinition> variableDefinitions;
		List<Value> args = new List<Value>();
		List<Value> locals = new List<Value>();

		public void init(bool initLocals, IList<ParameterDefinition> parameterDefinitions, IList<VariableDefinition> variableDefinitions) {
			this.parameterDefinitions = parameterDefinitions;
			this.variableDefinitions = variableDefinitions;
			valueStack.init();

			initValueList(args, parameterDefinitions.Count);

			if (initLocals) {
				locals.Clear();
				for (int i = 0; i < variableDefinitions.Count; i++) {
					var localType = variableDefinitions[i].VariableType;
					if (!localType.IsValueType)
						locals.Add(NullValue.Instance);
					else if (DotNetUtils.isAssembly(localType.Scope, "mscorlib")) {
						switch (localType.FullName) {
						case "System.Boolean":
						case "System.Byte":
						case "System.SByte":
						case "System.Int16":
						case "System.Int32":
						case "System.UInt16":
						case "System.UInt32":
							locals.Add(new Int32Value(0));
							break;
						case "System.Int64":
						case "System.UInt64":
							locals.Add(new Int64Value(0));
							break;
						case "System.Single":
						case "System.Double":
							locals.Add(new Real8Value(0));
							break;
						default:
							locals.Add(new UnknownValue());
							break;
						}
					}
					else
						locals.Add(new UnknownValue());
				}
			}
			else
				initValueList(locals, variableDefinitions.Count);
		}

		void initValueList(List<Value> list, int size) {
			list.Clear();
			for (int i = 0; i < size; i++)
				list.Add(new UnknownValue());
		}

		static Value getValue(List<Value> list, int i) {
			if (0 <= i && i < list.Count)
				return list[i];
			return new UnknownValue();
		}

		Value getArg(int i) {
			return getValue(args, i);
		}

		Value getArg(ParameterDefinition arg) {
			return getArg(arg.Index);
		}

		void setArg(int index, Value value) {
			if (0 <= index && index < args.Count)
				args[index] = value;
		}

		Value getLocal(int i) {
			return getValue(locals, i);
		}

		Value getLocal(VariableDefinition local) {
			return getLocal(local.Index);
		}

		void setLocal(int index, Value value) {
			if (0 <= index && index < locals.Count)
				locals[index] = value;
		}

		public Value pop() {
			return valueStack.pop();
		}

		public void emulate(Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Starg:
			case Code.Starg_S:	emulate_Starg(((ParameterDefinition)instr.Operand).Index); break;
			case Code.Stloc:
			case Code.Stloc_S:	emulate_Stloc(((VariableDefinition)instr.Operand).Index); break;
			case Code.Stloc_0:	emulate_Stloc(0); break;
			case Code.Stloc_1:	emulate_Stloc(1); break;
			case Code.Stloc_2:	emulate_Stloc(2); break;
			case Code.Stloc_3:	emulate_Stloc(3); break;

			case Code.Ldarg:
			case Code.Ldarg_S:	valueStack.push(getArg((ParameterDefinition)instr.Operand)); break;
			case Code.Ldarg_0:	valueStack.push(getArg(0)); break;
			case Code.Ldarg_1:	valueStack.push(getArg(1)); break;
			case Code.Ldarg_2:	valueStack.push(getArg(2)); break;
			case Code.Ldarg_3:	valueStack.push(getArg(3)); break;
			case Code.Ldloc:
			case Code.Ldloc_S:	valueStack.push(getLocal((VariableDefinition)instr.Operand)); break;
			case Code.Ldloc_0:	valueStack.push(getLocal(0)); break;
			case Code.Ldloc_1:	valueStack.push(getLocal(1)); break;
			case Code.Ldloc_2:	valueStack.push(getLocal(2)); break;
			case Code.Ldloc_3:	valueStack.push(getLocal(3)); break;

			case Code.Ldarga:
			case Code.Ldarga_S:	emulate_Ldarga(((ParameterDefinition)instr.Operand).Index); break;
			case Code.Ldloca:
			case Code.Ldloca_S:	emulate_Ldloca(((VariableDefinition)instr.Operand).Index); break;

			case Code.Dup:		valueStack.copyTop(); break;

			case Code.Ldc_I4:	valueStack.push(new Int32Value((int)instr.Operand)); break;
			case Code.Ldc_I4_S:	valueStack.push(new Int32Value((sbyte)instr.Operand)); break;
			case Code.Ldc_I8:	valueStack.push(new Int64Value((long)instr.Operand)); break;
			case Code.Ldc_R4:	valueStack.push(new Real8Value((float)instr.Operand)); break;
			case Code.Ldc_R8:	valueStack.push(new Real8Value((double)instr.Operand)); break;
			case Code.Ldc_I4_0:	valueStack.push(new Int32Value(0)); break;
			case Code.Ldc_I4_1:	valueStack.push(new Int32Value(1)); break;
			case Code.Ldc_I4_2:	valueStack.push(new Int32Value(2)); break;
			case Code.Ldc_I4_3:	valueStack.push(new Int32Value(3)); break;
			case Code.Ldc_I4_4:	valueStack.push(new Int32Value(4)); break;
			case Code.Ldc_I4_5:	valueStack.push(new Int32Value(5)); break;
			case Code.Ldc_I4_6:	valueStack.push(new Int32Value(6)); break;
			case Code.Ldc_I4_7:	valueStack.push(new Int32Value(7)); break;
			case Code.Ldc_I4_8:	valueStack.push(new Int32Value(8)); break;
			case Code.Ldc_I4_M1:valueStack.push(new Int32Value(-1)); break;
			case Code.Ldnull:	valueStack.push(NullValue.Instance); break;
			case Code.Ldstr:	valueStack.push(new StringValue((string)instr.Operand)); break;
			case Code.Box:		valueStack.push(new BoxedValue(valueStack.pop())); break;

			case Code.Conv_U1:	emulate_Conv_U1(instr); break;
			case Code.Conv_U2:	emulate_Conv_U2(instr); break;
			case Code.Conv_U4:	emulate_Conv_U4(instr); break;
			case Code.Conv_U8:	emulate_Conv_U8(instr); break;
			case Code.Conv_I1:	emulate_Conv_I1(instr); break;
			case Code.Conv_I2:	emulate_Conv_I2(instr); break;
			case Code.Conv_I4:	emulate_Conv_I4(instr); break;
			case Code.Conv_I8:	emulate_Conv_I8(instr); break;
			case Code.Add:		emulate_Add(instr); break;
			case Code.Sub:		emulate_Sub(instr); break;
			case Code.Mul:		emulate_Mul(instr); break;
			case Code.Div:		emulate_Div(instr); break;
			case Code.Div_Un:	emulate_Div_Un(instr); break;
			case Code.Rem:		emulate_Rem(instr); break;
			case Code.Rem_Un:	emulate_Rem_Un(instr); break;
			case Code.Neg:		emulate_Neg(instr); break;
			case Code.And:		emulate_And(instr); break;
			case Code.Or:		emulate_Or(instr); break;
			case Code.Xor:		emulate_Xor(instr); break;
			case Code.Not:		emulate_Not(instr); break;
			case Code.Shl:		emulate_Shl(instr); break;
			case Code.Shr:		emulate_Shr(instr); break;
			case Code.Shr_Un:	emulate_Shr_Un(instr); break;
			case Code.Ceq:		emulate_Ceq(instr); break;
			case Code.Cgt:		emulate_Cgt(instr); break;
			case Code.Cgt_Un:	emulate_Cgt_Un(instr); break;
			case Code.Clt:		emulate_Clt(instr); break;
			case Code.Clt_Un:	emulate_Clt_Un(instr); break;
			case Code.Unbox_Any:emulate_Unbox_Any(instr); break;

			case Code.Add_Ovf:
			case Code.Add_Ovf_Un:
			case Code.Sub_Ovf:
			case Code.Sub_Ovf_Un:
			case Code.Mul_Ovf:
			case Code.Mul_Ovf_Un:

			case Code.Unbox:

			case Code.Conv_R_Un:
			case Code.Conv_R4:
			case Code.Conv_R8:

			case Code.Arglist:
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
			case Code.Br:
			case Code.Break:
			case Code.Br_S:
			case Code.Call:
			case Code.Calli:
			case Code.Callvirt:
			case Code.Castclass:
			case Code.Ckfinite:
			case Code.Constrained:
			case Code.Conv_I:
			case Code.Conv_Ovf_I:
			case Code.Conv_Ovf_I1:
			case Code.Conv_Ovf_I1_Un:
			case Code.Conv_Ovf_I2:
			case Code.Conv_Ovf_I2_Un:
			case Code.Conv_Ovf_I4:
			case Code.Conv_Ovf_I4_Un:
			case Code.Conv_Ovf_I8:
			case Code.Conv_Ovf_I8_Un:
			case Code.Conv_Ovf_I_Un:
			case Code.Conv_Ovf_U:
			case Code.Conv_Ovf_U1:
			case Code.Conv_Ovf_U1_Un:
			case Code.Conv_Ovf_U2:
			case Code.Conv_Ovf_U2_Un:
			case Code.Conv_Ovf_U4:
			case Code.Conv_Ovf_U4_Un:
			case Code.Conv_Ovf_U8:
			case Code.Conv_Ovf_U8_Un:
			case Code.Conv_Ovf_U_Un:
			case Code.Conv_U:
			case Code.Cpblk:
			case Code.Cpobj:
			case Code.Endfilter:
			case Code.Endfinally:
			case Code.Initblk:
			case Code.Initobj:
			case Code.Isinst:
			case Code.Jmp:
			case Code.Ldelema:
			case Code.Ldelem_Any:
			case Code.Ldelem_I:
			case Code.Ldelem_I1:
			case Code.Ldelem_I2:
			case Code.Ldelem_I4:
			case Code.Ldelem_I8:
			case Code.Ldelem_R4:
			case Code.Ldelem_R8:
			case Code.Ldelem_Ref:
			case Code.Ldelem_U1:
			case Code.Ldelem_U2:
			case Code.Ldelem_U4:
			case Code.Ldfld:
			case Code.Ldflda:
			case Code.Ldftn:
			case Code.Ldind_I:
			case Code.Ldind_I1:
			case Code.Ldind_I2:
			case Code.Ldind_I4:
			case Code.Ldind_I8:
			case Code.Ldind_R4:
			case Code.Ldind_R8:
			case Code.Ldind_Ref:
			case Code.Ldind_U1:
			case Code.Ldind_U2:
			case Code.Ldind_U4:
			case Code.Ldlen:
			case Code.Ldobj:
			case Code.Ldsfld:
			case Code.Ldsflda:
			case Code.Ldtoken:
			case Code.Ldvirtftn:
			case Code.Leave:
			case Code.Leave_S:
			case Code.Localloc:
			case Code.Mkrefany:
			case Code.Newarr:
			case Code.Newobj:
			case Code.No:
			case Code.Nop:
			case Code.Pop:
			case Code.Readonly:
			case Code.Refanytype:
			case Code.Refanyval:
			case Code.Ret:
			case Code.Rethrow:
			case Code.Sizeof:
			case Code.Stelem_Any:
			case Code.Stelem_I:
			case Code.Stelem_I1:
			case Code.Stelem_I2:
			case Code.Stelem_I4:
			case Code.Stelem_I8:
			case Code.Stelem_R4:
			case Code.Stelem_R8:
			case Code.Stelem_Ref:
			case Code.Stfld:
			case Code.Stind_I:
			case Code.Stind_I1:
			case Code.Stind_I2:
			case Code.Stind_I4:
			case Code.Stind_I8:
			case Code.Stind_R4:
			case Code.Stind_R8:
			case Code.Stind_Ref:
			case Code.Stobj:
			case Code.Stsfld:
			case Code.Switch:
			case Code.Tail:
			case Code.Throw:
			case Code.Unaligned:
			case Code.Volatile:
			default:
				updateStack(instr);
				break;
			}
		}

		void updateStack(Instruction instr) {
			int pushes, pops;
			DotNetUtils.calculateStackUsage(instr, false, out pushes, out pops);
			if (pops == -1)
				valueStack.clear();
			else {
				valueStack.pop(pops);
				valueStack.push(pushes);
			}
		}

		void switchUnknownToSecondOperand(ref Value op1, ref Value op2) {
			if (op2.valueType == ValueType.Unknown)
				return;
			if (op1.valueType != ValueType.Unknown)
				return;
			Value tmp = op1;
			op1 = op2;
			op2 = tmp;
		}

		void emulate_Conv_U1(Instruction instr) {
			//TODO: Doc says that result is sign-extended. You zero-extend it. Is that correct?
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:
				var val32 = (Int32Value)val1;
				valueStack.push(new Int32Value((int)(byte)val32.value));
				break;

			case ValueType.Int64:
				var val64 = (Int64Value)val1;
				valueStack.push(new Int32Value((int)(byte)val64.value));
				break;

			case ValueType.Real8:
				var valr8 = (Real8Value)val1;
				valueStack.push(new Int32Value((int)(byte)valr8.value));
				break;

			default:
				valueStack.pushUnknown();
				break;
			}
		}

		void emulate_Conv_I1(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:
				var val32 = (Int32Value)val1;
				valueStack.push(new Int32Value((int)(sbyte)val32.value));
				break;

			case ValueType.Int64:
				var val64 = (Int64Value)val1;
				valueStack.push(new Int32Value((int)(sbyte)val64.value));
				break;

			case ValueType.Real8:
				var valr8 = (Real8Value)val1;
				valueStack.push(new Int32Value((int)(sbyte)valr8.value));
				break;

			default:
				valueStack.pushUnknown();
				break;
			}
		}

		void emulate_Conv_U2(Instruction instr) {
			//TODO: Doc says that result is sign-extended. You zero-extend it. Is that correct?
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:
				var val32 = (Int32Value)val1;
				valueStack.push(new Int32Value((int)(ushort)val32.value));
				break;

			case ValueType.Int64:
				var val64 = (Int64Value)val1;
				valueStack.push(new Int32Value((int)(ushort)val64.value));
				break;

			case ValueType.Real8:
				var valr8 = (Real8Value)val1;
				valueStack.push(new Int32Value((int)(ushort)valr8.value));
				break;

			default:
				valueStack.pushUnknown();
				break;
			}
		}

		void emulate_Conv_I2(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:
				var val32 = (Int32Value)val1;
				valueStack.push(new Int32Value((int)(short)val32.value));
				break;

			case ValueType.Int64:
				var val64 = (Int64Value)val1;
				valueStack.push(new Int32Value((int)(short)val64.value));
				break;

			case ValueType.Real8:
				var valr8 = (Real8Value)val1;
				valueStack.push(new Int32Value((int)(short)valr8.value));
				break;

			default:
				valueStack.pushUnknown();
				break;
			}
		}

		void emulate_Conv_U4(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:
				valueStack.push(val1);
				break;

			case ValueType.Int64:
				var val64 = (Int64Value)val1;
				valueStack.push(new Int32Value((int)(uint)val64.value));
				break;

			case ValueType.Real8:
				var valr8 = (Real8Value)val1;
				valueStack.push(new Int32Value((int)(uint)valr8.value));
				break;

			default:
				valueStack.pushUnknown();
				break;
			}
		}

		void emulate_Conv_I4(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:
				valueStack.push(val1);
				break;

			case ValueType.Int64:
				var val64 = (Int64Value)val1;
				valueStack.push(new Int32Value((int)val64.value));
				break;

			case ValueType.Real8:
				var valr8 = (Real8Value)val1;
				valueStack.push(new Int32Value((int)valr8.value));
				break;

			default:
				valueStack.pushUnknown();
				break;
			}
		}

		void emulate_Conv_U8(Instruction instr) {
			//TODO: Doc says that result is sign-extended. You zero-extend it. Is that correct?
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:
				var val32 = (Int32Value)val1;
				valueStack.push(new Int64Value((long)(ulong)(uint)val32.value));
				break;

			case ValueType.Int64:
				valueStack.push(val1);
				break;

			case ValueType.Real8:
				var valr8 = (Real8Value)val1;
				valueStack.push(new Int64Value((long)(ulong)valr8.value));
				break;

			default:
				valueStack.pushUnknown();
				break;
			}
		}

		void emulate_Conv_I8(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:
				var val32 = (Int32Value)val1;
				valueStack.push(new Int64Value((long)val32.value));
				break;

			case ValueType.Int64:
				valueStack.push(val1);
				break;

			case ValueType.Real8:
				var valr8 = (Real8Value)val1;
				valueStack.push(new Int64Value((long)valr8.value));
				break;

			default:
				valueStack.pushUnknown();
				break;
			}
		}

		void emulate_Add(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			switchUnknownToSecondOperand(ref val1, ref val2);

			if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == 0)
					valueStack.push(val2);
				else if (val2.valueType == ValueType.Int32) {
					var int2 = (Int32Value)val2;
					valueStack.push(new Int32Value(int1.value + int2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == 0)
					valueStack.push(val2);
				else if (val2.valueType == ValueType.Int64) {
					var long2 = (Int64Value)val2;
					valueStack.push(new Int64Value(long1.value + long2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				valueStack.push(new Real8Value(real1.value + real2.value));
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Sub(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				valueStack.push(new Int32Value(int1.value - int2.value));
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				valueStack.push(new Int64Value(long1.value - long2.value));
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				valueStack.push(new Real8Value(real1.value - real2.value));
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == 0)
					valueStack.push(val1);
				else
					valueStack.pushUnknown();
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == 0)
					valueStack.push(val1);
				else
					valueStack.pushUnknown();
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Mul(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			switchUnknownToSecondOperand(ref val1, ref val2);

			if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == 1)
					valueStack.push(val2);
				else if (int1.value == 0)
					valueStack.push(new Int32Value(0));
				else if (val2.valueType == ValueType.Int32) {
					var int2 = (Int32Value)val2;
					valueStack.push(new Int32Value(int1.value * int2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == 1)
					valueStack.push(val2);
				else if (long1.value == 0)
					valueStack.push(new Int64Value(0));
				else if (val2.valueType == ValueType.Int64) {
					var long2 = (Int64Value)val2;
					valueStack.push(new Int64Value(long1.value * long2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				valueStack.push(new Real8Value(real1.value * real2.value));
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Div(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				try {
					valueStack.push(new Int32Value(int1.value / int2.value));
				}
				catch (ArithmeticException) {
					valueStack.pushUnknown();
				}
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				try {
					valueStack.push(new Int64Value(long1.value / long2.value));
				}
				catch (ArithmeticException) {
					valueStack.pushUnknown();
				}
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				valueStack.push(new Real8Value(real1.value / real2.value));
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == 1)
					valueStack.push(val1);
				else
					valueStack.pushUnknown();
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == 1)
					valueStack.push(val1);
				else
					valueStack.pushUnknown();
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Div_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				try {
					valueStack.push(new Int32Value((int)((uint)int1.value / (uint)int2.value)));
				}
				catch (ArithmeticException) {
					valueStack.pushUnknown();
				}
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				try {
					valueStack.push(new Int64Value((long)((ulong)long1.value / (ulong)long2.value)));
				}
				catch (ArithmeticException) {
					valueStack.pushUnknown();
				}
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == 1)
					valueStack.push(val1);
				else
					valueStack.pushUnknown();
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == 1)
					valueStack.push(val1);
				else
					valueStack.pushUnknown();
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Rem(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				try {
					valueStack.push(new Int32Value(int1.value % int2.value));
				}
				catch (ArithmeticException) {
					valueStack.pushUnknown();
				}
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				try {
					valueStack.push(new Int64Value(long1.value % long2.value));
				}
				catch (ArithmeticException) {
					valueStack.pushUnknown();
				}
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				valueStack.push(new Real8Value(real1.value % real2.value));
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == 1)
					valueStack.push(new Int32Value(0));
				else
					valueStack.pushUnknown();
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == 1)
					valueStack.push(new Int64Value(0));
				else
					valueStack.pushUnknown();
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Rem_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				try {
					valueStack.push(new Int32Value((int)((uint)int1.value % (uint)int2.value)));
				}
				catch (ArithmeticException) {
					valueStack.pushUnknown();
				}
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				try {
					valueStack.push(new Int64Value((long)((ulong)long1.value % (ulong)long2.value)));
				}
				catch (ArithmeticException) {
					valueStack.pushUnknown();
				}
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == 1)
					valueStack.push(new Int32Value(0));
				else
					valueStack.pushUnknown();
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == 1)
					valueStack.push(new Int64Value(0));
				else
					valueStack.pushUnknown();
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Neg(Instruction instr) {
			var val1 = valueStack.pop();

			if (val1.valueType == ValueType.Int32)
				valueStack.push(new Int32Value(-((Int32Value)val1).value));
			else if (val1.valueType == ValueType.Int64)
				valueStack.push(new Int64Value(-((Int64Value)val1).value));
			else if (val1.valueType == ValueType.Real8)
				valueStack.push(new Real8Value(-((Real8Value)val1).value));
			else
				valueStack.pushUnknown();
		}

		void emulate_And(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			switchUnknownToSecondOperand(ref val1, ref val2);

			if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == 0)
					valueStack.push(new Int32Value(0));
				else if (int1.value == -1)
					valueStack.push(val2);
				else if (val2.valueType == ValueType.Int32) {
					var int2 = (Int32Value)val2;
					valueStack.push(new Int32Value(int1.value & int2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == 0)
					valueStack.push(new Int64Value(0));
				else if (long1.value == -1)
					valueStack.push(val2);
				else if (val2.valueType == ValueType.Int64) {
					var long2 = (Int64Value)val2;
					valueStack.push(new Int64Value(long1.value & long2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Or(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			switchUnknownToSecondOperand(ref val1, ref val2);

			if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == -1)
					valueStack.push(new Int32Value(-1));
				else if (int1.value == 0)
					valueStack.push(val2);
				else if (val2.valueType == ValueType.Int32) {
					var int2 = (Int32Value)val2;
					valueStack.push(new Int32Value(int1.value | int2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == -1)
					valueStack.push(new Int64Value(-1));
				else if (long1.value == 0)
					valueStack.push(val2);
				else if (val2.valueType == ValueType.Int64) {
					var long2 = (Int64Value)val2;
					valueStack.push(new Int64Value(long1.value | long2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Xor(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			switchUnknownToSecondOperand(ref val1, ref val2);

			if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == 0)
					valueStack.push(val2);
				else if (val2.valueType == ValueType.Int32) {
					var int2 = (Int32Value)val2;
					valueStack.push(new Int32Value(int1.value ^ int2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == 0)
					valueStack.push(val2);
				else if (val2.valueType == ValueType.Int64) {
					var long2 = (Int64Value)val2;
					valueStack.push(new Int64Value(long1.value ^ long2.value));
				}
				else
					valueStack.pushUnknown();
			}
			else {
				valueStack.pushUnknown();
			}
		}

		void emulate_Not(Instruction instr) {
			var val1 = valueStack.pop();

			if (val1.valueType == ValueType.Int32)
				valueStack.push(new Int32Value(~((Int32Value)val1).value));
			else if (val1.valueType == ValueType.Int64)
				valueStack.push(new Int64Value(~((Int64Value)val1).value));
			else
				valueStack.pushUnknown();
		}

		void emulate_Shl(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			int bits;
			if (val2.valueType == ValueType.Int32)
				bits = ((Int32Value)val2).value;
			else if (val2.valueType == ValueType.Int64)
				bits = (int)((Int64Value)val2).value;
			else {
				valueStack.pushUnknown();
				return;
			}

			if (bits == 0)
				valueStack.push(val1);
			else if (val1.valueType == ValueType.Int32)
				valueStack.push(new Int32Value(((Int32Value)val1).value << bits));
			else if (val1.valueType == ValueType.Int64)
				valueStack.push(new Int64Value(((Int64Value)val1).value << bits));
			else
				valueStack.pushUnknown();
		}

		void emulate_Shr(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			int bits;
			if (val2.valueType == ValueType.Int32)
				bits = ((Int32Value)val2).value;
			else if (val2.valueType == ValueType.Int64)
				bits = (int)((Int64Value)val2).value;
			else {
				valueStack.pushUnknown();
				return;
			}

			if (bits == 0)
				valueStack.push(val1);
			else if (val1.valueType == ValueType.Int32)
				valueStack.push(new Int32Value(((Int32Value)val1).value >> bits));
			else if (val1.valueType == ValueType.Int64)
				valueStack.push(new Int64Value(((Int64Value)val1).value >> bits));
			else
				valueStack.pushUnknown();
		}

		void emulate_Shr_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			int bits;
			if (val2.valueType == ValueType.Int32)
				bits = ((Int32Value)val2).value;
			else if (val2.valueType == ValueType.Int64)
				bits = (int)((Int64Value)val2).value;
			else {
				valueStack.pushUnknown();
				return;
			}

			if (bits == 0)
				valueStack.push(val1);
			else if (val1.valueType == ValueType.Int32)
				valueStack.push(new Int32Value((int)((uint)((Int32Value)val1).value >> bits)));
			else if (val1.valueType == ValueType.Int64)
				valueStack.push(new Int64Value((long)((ulong)((Int64Value)val1).value >> bits)));
			else
				valueStack.pushUnknown();
		}

		void emulate_Ceq(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			//TODO: If it's an unknown int32/64, push 1 if val1 is same ref as val2

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				valueStack.push(new Int32Value(int1.value == int2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				valueStack.push(new Int32Value(long1.value == long2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				valueStack.push(new Int32Value(real1.value == real2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Null && val2.valueType == ValueType.Null) {
				valueStack.push(new Int32Value(1));
			}
			else {
				valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
		}

		void emulate_Cgt(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			//TODO: If it's an unknown int32/64, push 0 if val1 is same ref as val2

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				valueStack.push(new Int32Value(int1.value > int2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				valueStack.push(new Int32Value(long1.value > long2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				valueStack.push(new Int32Value(real1.value > real2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == int.MinValue)
					valueStack.push(new Int32Value(0));	// min > x => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == int.MaxValue)
					valueStack.push(new Int32Value(0));	// x > max => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == long.MinValue)
					valueStack.push(new Int32Value(0));	// min > x => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == long.MaxValue)
					valueStack.push(new Int32Value(0));	// x > max => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else {
				valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
		}

		void emulate_Cgt_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			//TODO: If it's an unknown int32/64, push 0 if val1 is same ref as val2

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				valueStack.push(new Int32Value((uint)int1.value > (uint)int2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				valueStack.push(new Int32Value((ulong)long1.value > (ulong)long2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if ((uint)int1.value == uint.MinValue)
					valueStack.push(new Int32Value(0));	// min > x => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if ((uint)int2.value == uint.MaxValue)
					valueStack.push(new Int32Value(0));	// x > max => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if ((ulong)long1.value == ulong.MinValue)
					valueStack.push(new Int32Value(0));	// min > x => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if ((ulong)long2.value == ulong.MaxValue)
					valueStack.push(new Int32Value(0));	// x > max => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else {
				valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
		}

		void emulate_Clt(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			//TODO: If it's an unknown int32/64, push 0 if val1 is same ref as val2

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				valueStack.push(new Int32Value(int1.value < int2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				valueStack.push(new Int32Value(long1.value < long2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Real8 && val2.valueType == ValueType.Real8) {
				var real1 = (Real8Value)val1;
				var real2 = (Real8Value)val2;
				valueStack.push(new Int32Value(real1.value < real2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if (int1.value == int.MaxValue)
					valueStack.push(new Int32Value(0));	// max < x => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if (int2.value == int.MinValue)
					valueStack.push(new Int32Value(0));	// x < min => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if (long1.value == long.MaxValue)
					valueStack.push(new Int32Value(0));	// max < x => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if (long2.value == long.MinValue)
					valueStack.push(new Int32Value(0));	// x < min => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else {
				valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
		}

		void emulate_Clt_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			//TODO: If it's an unknown int32/64, push 0 if val1 is same ref as val2

			if (val1.valueType == ValueType.Int32 && val2.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				var int2 = (Int32Value)val2;
				valueStack.push(new Int32Value((uint)int1.value < (uint)int2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Int64 && val2.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				var long2 = (Int64Value)val2;
				valueStack.push(new Int32Value((ulong)long1.value < (ulong)long2.value ? 1 : 0));
			}
			else if (val1.valueType == ValueType.Int32) {
				var int1 = (Int32Value)val1;
				if ((uint)int1.value == uint.MaxValue)
					valueStack.push(new Int32Value(0));	// max < x => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val2.valueType == ValueType.Int32) {
				var int2 = (Int32Value)val2;
				if ((uint)int2.value == uint.MinValue)
					valueStack.push(new Int32Value(0));	// x < min => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val1.valueType == ValueType.Int64) {
				var long1 = (Int64Value)val1;
				if ((ulong)long1.value == ulong.MaxValue)
					valueStack.push(new Int32Value(0));	// max < x => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else if (val2.valueType == ValueType.Int64) {
				var long2 = (Int64Value)val2;
				if ((ulong)long2.value == ulong.MinValue)
					valueStack.push(new Int32Value(0));	// x < min => false
				else
					valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
			else {
				valueStack.pushUnknown();	//TODO: Push int32 with bit 0 unknown
			}
		}

		void emulate_Unbox_Any(Instruction instr) {
			var val1 = valueStack.pop();
			if (val1.valueType == ValueType.Boxed)
				valueStack.push(((BoxedValue)val1).value);
			else
				valueStack.pushUnknown();
		}

		void emulate_Starg(int index) {
			//TODO: You should truncate the value if necessary, eg. from int32 -> bool,
			//		int32 -> int16, double -> float, etc.
			setArg(index, valueStack.pop());
		}

		void emulate_Stloc(int index) {
			//TODO: You should truncate the value if necessary, eg. from int32 -> bool,
			//		int32 -> int16, double -> float, etc.
			setLocal(index, valueStack.pop());
		}

		void emulate_Ldarga(int index) {
			valueStack.pushUnknown();
			setArg(index, new UnknownValue());
		}

		void emulate_Ldloca(int index) {
			valueStack.pushUnknown();
			setLocal(index, new UnknownValue());
		}
	}
}
