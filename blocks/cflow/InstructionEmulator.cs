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

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;

namespace de4dot.blocks.cflow {
	public class InstructionEmulator {
		ValueStack valueStack = new ValueStack();
		Dictionary<Value, bool> protectedStackValues = new Dictionary<Value, bool>();
		IList<ParameterDefinition> parameterDefinitions;
		IList<VariableDefinition> variableDefinitions;
		List<Value> args = new List<Value>();
		List<Value> locals = new List<Value>();
		int argBase;

		MethodDefinition prev_method;
		List<Value> cached_args = new List<Value>();
		List<Value> cached_locals = new List<Value>();
		int cached_argBase;

		public InstructionEmulator() {
		}

		public InstructionEmulator(MethodDefinition method) {
			init(method);
		}

		public void init(Blocks blocks) {
			init(blocks.Method);
		}

		public void init(MethodDefinition method) {
			this.parameterDefinitions = method.Parameters;
			this.variableDefinitions = method.Body.Variables;
			valueStack.init();
			protectedStackValues.Clear();

			if (method != prev_method) {
				prev_method = method;

				cached_args.Clear();
				cached_argBase = 0;
				if (method.HasImplicitThis) {
					cached_argBase = 1;
					cached_args.Add(new UnknownValue());
				}
				for (int i = 0; i < parameterDefinitions.Count; i++)
					cached_args.Add(getUnknownValue(parameterDefinitions[i].ParameterType));

				cached_locals.Clear();
				for (int i = 0; i < variableDefinitions.Count; i++)
					cached_locals.Add(getUnknownValue(variableDefinitions[i].VariableType));
			}

			argBase = cached_argBase;
			args.Clear();
			args.AddRange(cached_args);
			locals.Clear();
			locals.AddRange(cached_locals);
		}

		public void setProtected(Value value) {
			protectedStackValues[value] = true;
		}

		static Value getUnknownValue(TypeReference typeReference) {
			if (typeReference == null)
				return new UnknownValue();
			switch (typeReference.EType) {
			case ElementType.Boolean: return Int32Value.createUnknownBool();
			case ElementType.I1: return Int32Value.createUnknown();
			case ElementType.U1: return Int32Value.createUnknownUInt8();
			case ElementType.I2: return Int32Value.createUnknown();
			case ElementType.U2: return Int32Value.createUnknownUInt16();
			case ElementType.I4: return Int32Value.createUnknown();
			case ElementType.U4: return Int32Value.createUnknown();
			case ElementType.I8: return Int64Value.createUnknown();
			case ElementType.U8: return Int64Value.createUnknown();
			}
			return new UnknownValue();
		}

		Value truncateValue(Value value, TypeReference typeReference) {
			if (typeReference == null)
				return value;
			if (protectedStackValues.ContainsKey(value))
				return value;

			switch (typeReference.EType) {
			case ElementType.Boolean:
				if (value.isInt32())
					return ((Int32Value)value).toBoolean();
				return Int32Value.createUnknownBool();

			case ElementType.I1:
				if (value.isInt32())
					return ((Int32Value)value).toInt8();
				return Int32Value.createUnknown();

			case ElementType.U1:
				if (value.isInt32())
					return ((Int32Value)value).toUInt8();
				return Int32Value.createUnknownUInt8();

			case ElementType.I2:
				if (value.isInt32())
					return ((Int32Value)value).toInt16();
				return Int32Value.createUnknown();

			case ElementType.U2:
				if (value.isInt32())
					return ((Int32Value)value).toUInt16();
				return Int32Value.createUnknownUInt16();

			case ElementType.I4:
			case ElementType.U4:
				if (value.isInt32())
					return value;
				return Int32Value.createUnknown();

			case ElementType.I8:
			case ElementType.U8:
				if (value.isInt64())
					return value;
				return Int64Value.createUnknown();

			case ElementType.R4:
				if (value.isReal8())
					return new Real8Value((float)((Real8Value)value).value);
				return new UnknownValue();

			case ElementType.R8:
				if (value.isReal8())
					return value;
				return new UnknownValue();
			}
			return value;
		}

		static Value getValue(List<Value> list, int i) {
			if (0 <= i && i < list.Count)
				return list[i];
			return new UnknownValue();
		}

		public Value getArg(int i) {
			return getValue(args, i);
		}

		int index(ParameterDefinition arg) {
			return arg.Sequence;
		}

		public Value getArg(ParameterDefinition arg) {
			return getArg(index(arg));
		}

		TypeReference getArgType(int index) {
			index -= argBase;
			if (0 <= index && index < parameterDefinitions.Count)
				return parameterDefinitions[index].ParameterType;
			return null;
		}

		public void setArg(ParameterDefinition arg, Value value) {
			setArg(index(arg), value);
		}

		public void makeArgUnknown(ParameterDefinition arg) {
			setArg(arg, getUnknownArg(index(arg)));
		}

		void setArg(int index, Value value) {
			if (0 <= index && index < args.Count)
				args[index] = truncateValue(value, getArgType(index));
		}

		Value getUnknownArg(int index) {
			return getUnknownValue(getArgType(index));
		}

		public Value getLocal(int i) {
			return getValue(locals, i);
		}

		public Value getLocal(VariableDefinition local) {
			return getLocal(local.Index);
		}

		public void setLocal(VariableDefinition local, Value value) {
			setLocal(local.Index, value);
		}

		public void makeLocalUnknown(VariableDefinition local) {
			setLocal(local.Index, getUnknownLocal(local.Index));
		}

		void setLocal(int index, Value value) {
			if (0 <= index && index < locals.Count)
				locals[index] = truncateValue(value, variableDefinitions[index].VariableType);
		}

		Value getUnknownLocal(int index) {
			if (0 <= index && index < variableDefinitions.Count)
				return getUnknownValue(variableDefinitions[index].VariableType);
			return new UnknownValue();
		}

		public int stackSize() {
			return valueStack.Size;
		}

		public void push(Value value) {
			valueStack.push(value);
		}

		public Value pop() {
			return valueStack.pop();
		}

		public Value peek() {
			return valueStack.peek();
		}

		public void emulate(IEnumerable<Instr> instructions) {
			foreach (var instr in instructions)
				emulate(instr.Instruction);
		}

		public void emulate(IList<Instr> instructions, int start, int end) {
			for (int i = start; i < end; i++)
				emulate(instructions[i].Instruction);
		}

		public void emulate(Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Starg:
			case Code.Starg_S:	emulate_Starg((ParameterDefinition)instr.Operand); break;
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
			case Code.Ldarga_S:	emulate_Ldarga((ParameterDefinition)instr.Operand); break;
			case Code.Ldloca:
			case Code.Ldloca_S:	emulate_Ldloca(((VariableDefinition)instr.Operand).Index); break;

			case Code.Dup:		valueStack.copyTop(); break;

			case Code.Ldc_I4:	valueStack.push(new Int32Value((int)instr.Operand)); break;
			case Code.Ldc_I4_S:	valueStack.push(new Int32Value((sbyte)instr.Operand)); break;
			case Code.Ldc_I8:	valueStack.push(new Int64Value((long)instr.Operand)); break;
			case Code.Ldc_R4:	valueStack.push(new Real8Value((float)instr.Operand)); break;
			case Code.Ldc_R8:	valueStack.push(new Real8Value((double)instr.Operand)); break;
			case Code.Ldc_I4_0:	valueStack.push(Int32Value.zero); break;
			case Code.Ldc_I4_1:	valueStack.push(Int32Value.one); break;
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

			case Code.Call:		emulate_Call(instr); break;
			case Code.Callvirt:	emulate_Callvirt(instr); break;

			case Code.Castclass: emulate_Castclass(instr); break;
			case Code.Isinst:	emulate_Isinst(instr); break;

			case Code.Add_Ovf:	emulateIntOps2(); break;
			case Code.Add_Ovf_Un: emulateIntOps2(); break;
			case Code.Sub_Ovf:	emulateIntOps2(); break;
			case Code.Sub_Ovf_Un: emulateIntOps2(); break;
			case Code.Mul_Ovf:	emulateIntOps2(); break;
			case Code.Mul_Ovf_Un: emulateIntOps2(); break;

			case Code.Conv_Ovf_I1:
			case Code.Conv_Ovf_I1_Un: valueStack.pop(); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Conv_Ovf_I2:
			case Code.Conv_Ovf_I2_Un: valueStack.pop(); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Conv_Ovf_I4:
			case Code.Conv_Ovf_I4_Un: valueStack.pop(); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Conv_Ovf_I8:
			case Code.Conv_Ovf_I8_Un: valueStack.pop(); valueStack.push(Int64Value.createUnknown()); break;
			case Code.Conv_Ovf_U1:
			case Code.Conv_Ovf_U1_Un: valueStack.pop(); valueStack.push(Int32Value.createUnknownUInt8()); break;
			case Code.Conv_Ovf_U2:
			case Code.Conv_Ovf_U2_Un: valueStack.pop(); valueStack.push(Int32Value.createUnknownUInt16()); break;
			case Code.Conv_Ovf_U4:
			case Code.Conv_Ovf_U4_Un: valueStack.pop(); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Conv_Ovf_U8:
			case Code.Conv_Ovf_U8_Un: valueStack.pop(); valueStack.push(Int64Value.createUnknown()); break;

			case Code.Ldelem_I1: valueStack.pop(2); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Ldelem_I2: valueStack.pop(2); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Ldelem_I4: valueStack.pop(2); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Ldelem_I8: valueStack.pop(2); valueStack.push(Int64Value.createUnknown()); break;
			case Code.Ldelem_U1: valueStack.pop(2); valueStack.push(Int32Value.createUnknownUInt8()); break;
			case Code.Ldelem_U2: valueStack.pop(2); valueStack.push(Int32Value.createUnknownUInt16()); break;
			case Code.Ldelem_U4: valueStack.pop(2); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Ldelem_Any:valueStack.pop(2); valueStack.push(getUnknownValue(instr.Operand as TypeReference)); break;

			case Code.Ldind_I1:	valueStack.pop(); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Ldind_I2:	valueStack.pop(); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Ldind_I4:	valueStack.pop(); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Ldind_I8:	valueStack.pop(); valueStack.push(Int64Value.createUnknown()); break;
			case Code.Ldind_U1:	valueStack.pop(); valueStack.push(Int32Value.createUnknownUInt8()); break;
			case Code.Ldind_U2:	valueStack.pop(); valueStack.push(Int32Value.createUnknownUInt16()); break;
			case Code.Ldind_U4:	valueStack.pop(); valueStack.push(Int32Value.createUnknown()); break;

			case Code.Ldlen:	valueStack.pop(); valueStack.push(Int32Value.createUnknown()); break;
			case Code.Sizeof:	valueStack.push(Int32Value.createUnknown()); break;

			case Code.Ldfld:	emulate_Ldfld(instr); break;
			case Code.Ldsfld:	emulate_Ldsfld(instr); break;

			case Code.Ldftn:	valueStack.push(new ObjectValue(instr.Operand)); break;
			case Code.Ldsflda:	valueStack.push(new ObjectValue(instr.Operand)); break;
			case Code.Ldtoken:	valueStack.push(new ObjectValue(instr.Operand)); break;
			case Code.Ldvirtftn:valueStack.pop(); valueStack.push(new ObjectValue()); break;
			case Code.Ldflda:	valueStack.pop(); valueStack.push(new ObjectValue()); break;

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
			case Code.Br_S:
			case Code.Break:
			case Code.Calli:
			case Code.Ckfinite:
			case Code.Constrained:
			case Code.Conv_I:
			case Code.Conv_Ovf_I:
			case Code.Conv_Ovf_I_Un:
			case Code.Conv_Ovf_U:
			case Code.Conv_Ovf_U_Un:
			case Code.Conv_U:
			case Code.Cpblk:
			case Code.Cpobj:
			case Code.Endfilter:
			case Code.Endfinally:
			case Code.Initblk:
			case Code.Initobj:
			case Code.Jmp:
			case Code.Ldelema:
			case Code.Ldelem_I:
			case Code.Ldelem_R4:
			case Code.Ldelem_R8:
			case Code.Ldelem_Ref:
			case Code.Ldind_I:
			case Code.Ldind_R4:
			case Code.Ldind_R8:
			case Code.Ldind_Ref:
			case Code.Ldobj:
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

		void emulate_Conv_U1(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.push(Int32Value.Conv_U1((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.push(Int32Value.Conv_U1((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.push(Int32Value.Conv_U1((Real8Value)val1)); break;
			default:				valueStack.push(Int32Value.createUnknownUInt8()); break;
			}
		}

		void emulate_Conv_I1(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.push(Int32Value.Conv_I1((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.push(Int32Value.Conv_I1((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.push(Int32Value.Conv_I1((Real8Value)val1)); break;
			default:				valueStack.push(Int32Value.createUnknown()); break;
			}
		}

		void emulate_Conv_U2(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.push(Int32Value.Conv_U2((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.push(Int32Value.Conv_U2((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.push(Int32Value.Conv_U2((Real8Value)val1)); break;
			default:				valueStack.push(Int32Value.createUnknownUInt16()); break;
			}
		}

		void emulate_Conv_I2(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.push(Int32Value.Conv_I2((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.push(Int32Value.Conv_I2((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.push(Int32Value.Conv_I2((Real8Value)val1)); break;
			default:				valueStack.push(Int32Value.createUnknown()); break;
			}
		}

		void emulate_Conv_U4(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.push(Int32Value.Conv_U4((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.push(Int32Value.Conv_U4((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.push(Int32Value.Conv_U4((Real8Value)val1)); break;
			default:				valueStack.push(Int32Value.createUnknown()); break;
			}
		}

		void emulate_Conv_I4(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.push(Int32Value.Conv_I4((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.push(Int32Value.Conv_I4((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.push(Int32Value.Conv_I4((Real8Value)val1)); break;
			default:				valueStack.push(Int32Value.createUnknown()); break;
			}
		}

		void emulate_Conv_U8(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.push(Int64Value.Conv_U8((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.push(Int64Value.Conv_U8((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.push(Int64Value.Conv_U8((Real8Value)val1)); break;
			default:				valueStack.push(Int64Value.createUnknown()); break;
			}
		}

		void emulate_Conv_I8(Instruction instr) {
			var val1 = valueStack.pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.push(Int64Value.Conv_I8((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.push(Int64Value.Conv_I8((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.push(Int64Value.Conv_I8((Real8Value)val1)); break;
			default:				valueStack.push(Int64Value.createUnknown()); break;
			}
		}

		void emulate_Add(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Add((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Add((Int64Value)val1, (Int64Value)val2));
			else if (val1.isReal8() && val2.isReal8())
				valueStack.push(Real8Value.Add((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Sub(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Sub((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Sub((Int64Value)val1, (Int64Value)val2));
			else if (val1.isReal8() && val2.isReal8())
				valueStack.push(Real8Value.Sub((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Mul(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Mul((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Mul((Int64Value)val1, (Int64Value)val2));
			else if (val1.isReal8() && val2.isReal8())
				valueStack.push(Real8Value.Mul((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Div(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Div((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Div((Int64Value)val1, (Int64Value)val2));
			else if (val1.isReal8() && val2.isReal8())
				valueStack.push(Real8Value.Div((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Div_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Div_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Div_Un((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Rem(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Rem((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Rem((Int64Value)val1, (Int64Value)val2));
			else if (val1.isReal8() && val2.isReal8())
				valueStack.push(Real8Value.Rem((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Rem_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Rem_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Rem_Un((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Neg(Instruction instr) {
			var val1 = valueStack.pop();

			if (val1.isInt32())
				valueStack.push(Int32Value.Neg((Int32Value)val1));
			else if (val1.isInt64())
				valueStack.push(Int64Value.Neg((Int64Value)val1));
			else if (val1.isReal8())
				valueStack.push(Real8Value.Neg((Real8Value)val1));
			else
				valueStack.pushUnknown();
		}

		void emulate_And(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.And((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.And((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Or(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Or((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Or((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Xor(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Xor((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Xor((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Not(Instruction instr) {
			var val1 = valueStack.pop();

			if (val1.isInt32())
				valueStack.push(Int32Value.Not((Int32Value)val1));
			else if (val1.isInt64())
				valueStack.push(Int64Value.Not((Int64Value)val1));
			else
				valueStack.pushUnknown();
		}

		void emulate_Shl(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Shl((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt32())
				valueStack.push(Int64Value.Shl((Int64Value)val1, (Int32Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Shr(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Shr((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt32())
				valueStack.push(Int64Value.Shr((Int64Value)val1, (Int32Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Shr_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Shr_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt32())
				valueStack.push(Int64Value.Shr_Un((Int64Value)val1, (Int32Value)val2));
			else
				valueStack.pushUnknown();
		}

		void emulate_Ceq(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Ceq((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Ceq((Int64Value)val1, (Int64Value)val2));
			else if (val1.isNull() && val2.isNull())
				valueStack.push(Int32Value.one);
			else
				valueStack.push(Int32Value.createUnknownBool());
		}

		void emulate_Cgt(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Cgt((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Cgt((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.push(Int32Value.createUnknownBool());
		}

		void emulate_Cgt_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Cgt_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Cgt_Un((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.push(Int32Value.createUnknownBool());
		}

		void emulate_Clt(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Clt((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Clt((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.push(Int32Value.createUnknownBool());
		}

		void emulate_Clt_Un(Instruction instr) {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();

			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.Clt_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.Clt_Un((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.push(Int32Value.createUnknownBool());
		}

		void emulate_Unbox_Any(Instruction instr) {
			var val1 = valueStack.pop();
			if (val1.isBoxed())
				valueStack.push(((BoxedValue)val1).value);
			else
				valueStack.pushUnknown();
		}

		void emulate_Starg(ParameterDefinition arg) {
			setArg(index(arg), valueStack.pop());
		}

		void emulate_Stloc(int index) {
			setLocal(index, valueStack.pop());
		}

		void emulate_Ldarga(ParameterDefinition arg) {
			valueStack.pushUnknown();
			makeArgUnknown(arg);
		}

		void emulate_Ldloca(int index) {
			valueStack.pushUnknown();
			setLocal(index, getUnknownLocal(index));
		}

		void emulate_Call(Instruction instr) {
			emulate_Call(instr, (MethodReference)instr.Operand);
		}

		void emulate_Callvirt(Instruction instr) {
			emulate_Call(instr, (MethodReference)instr.Operand);
		}

		void emulate_Call(Instruction instr, MethodReference method) {
			int pushes, pops;
			DotNetUtils.calculateStackUsage(instr, false, out pushes, out pops);
			valueStack.pop(pops);
			if (pushes == 1)
				valueStack.push(getUnknownValue(method.MethodReturnType.ReturnType));
			else
				valueStack.push(pushes);
		}

		void emulate_Castclass(Instruction instr) {
			var val1 = valueStack.pop();

			if (val1.isNull())
				valueStack.push(val1);
			else
				valueStack.pushUnknown();
		}

		void emulate_Isinst(Instruction instr) {
			var val1 = valueStack.pop();

			if (val1.isNull())
				valueStack.push(val1);
			else
				valueStack.pushUnknown();
		}

		void emulate_Ldfld(Instruction instr) {
			var val1 = valueStack.pop();
			emulateLoadField(instr.Operand as FieldReference);
		}

		void emulate_Ldsfld(Instruction instr) {
			emulateLoadField(instr.Operand as FieldReference);
		}

		void emulateLoadField(FieldReference fieldReference) {
			if (fieldReference != null)
				valueStack.push(getUnknownValue(fieldReference.FieldType));
			else
				valueStack.pushUnknown();
		}

		void emulateIntOps2() {
			var val2 = valueStack.pop();
			var val1 = valueStack.pop();
			if (val1.isInt32() && val2.isInt32())
				valueStack.push(Int32Value.createUnknown());
			else if (val1.isInt64() && val2.isInt64())
				valueStack.push(Int64Value.createUnknown());
			else
				valueStack.pushUnknown();
		}
	}
}
