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

namespace de4dot.blocks.cflow {
	public class InstructionEmulator {
		ValueStack valueStack = new ValueStack();
		Dictionary<Value, bool> protectedStackValues = new Dictionary<Value, bool>();
		IList<Parameter> parameterDefs;
		IList<Local> localDefs;
		List<Value> args = new List<Value>();
		List<Value> locals = new List<Value>();

		MethodDef prev_method;
		List<Value> cached_args = new List<Value>();
		List<Value> cached_locals = new List<Value>();
		List<Value> cached_zeroed_locals = new List<Value>();

		public InstructionEmulator() {
		}

		public InstructionEmulator(MethodDef method) => Initialize(method, false);
		public void Initialize(Blocks blocks, bool emulateFromFirstInstruction) =>
			Initialize(blocks.Method, emulateFromFirstInstruction);
		public void Initialize(MethodDef method) => Initialize(method, false);
		public void Initialize(MethodDef method, bool emulateFromFirstInstruction) =>
			Initialize(method, method.Parameters, method.Body.Variables, method.Body.InitLocals, emulateFromFirstInstruction);

		public void Initialize(MethodDef method, IList<Parameter> methodParameters, IList<Local> methodLocals, bool initLocals, bool emulateFromFirstInstruction) {
			parameterDefs = methodParameters;
			localDefs = methodLocals;
			valueStack.Initialize();
			protectedStackValues.Clear();

			if (method != prev_method) {
				prev_method = method;

				cached_args.Clear();
				for (int i = 0; i < parameterDefs.Count; i++)
					cached_args.Add(GetUnknownValue(parameterDefs[i].Type));

				cached_locals.Clear();
				cached_zeroed_locals.Clear();
				for (int i = 0; i < localDefs.Count; i++) {
					cached_locals.Add(GetUnknownValue(localDefs[i].Type));
					cached_zeroed_locals.Add(GetDefaultValue(localDefs[i].Type));
				}
			}

			args.Clear();
			args.AddRange(cached_args);
			locals.Clear();
			locals.AddRange(initLocals && emulateFromFirstInstruction ? cached_zeroed_locals : cached_locals);
		}

		public void SetProtected(Value value) => protectedStackValues[value] = true;
		static Value GetUnknownValue(ITypeDefOrRef type) => GetUnknownValue(type.ToTypeSig(false));

		static Value GetUnknownValue(TypeSig type) {
			if (type == null)
				return new UnknownValue();
			switch (type.ElementType) {
			case ElementType.Boolean: return Int32Value.CreateUnknownBool();
			case ElementType.I1: return Int32Value.CreateUnknown();
			case ElementType.U1: return Int32Value.CreateUnknownUInt8();
			case ElementType.I2: return Int32Value.CreateUnknown();
			case ElementType.U2: return Int32Value.CreateUnknownUInt16();
			case ElementType.I4: return Int32Value.CreateUnknown();
			case ElementType.U4: return Int32Value.CreateUnknown();
			case ElementType.I8: return Int64Value.CreateUnknown();
			case ElementType.U8: return Int64Value.CreateUnknown();
			}
			return new UnknownValue();
		}

		static Value GetDefaultValue(TypeSig type) {
			if (type == null)
				return new UnknownValue();
			switch (type.ElementType) {
			case ElementType.Boolean:
			case ElementType.I1:
			case ElementType.U1:
			case ElementType.I2:
			case ElementType.U2:
			case ElementType.I4:
			case ElementType.U4:
				return Int32Value.Zero;
			case ElementType.I8:
			case ElementType.U8:
				return Int64Value.Zero;
			}
			return new UnknownValue();
		}

		Value TruncateValue(Value value, TypeSig type) {
			if (type == null)
				return value;
			if (protectedStackValues.ContainsKey(value))
				return value;

			switch (type.ElementType) {
			case ElementType.Boolean:
				if (value.IsInt32())
					return ((Int32Value)value).ToBoolean();
				return Int32Value.CreateUnknownBool();

			case ElementType.I1:
				if (value.IsInt32())
					return ((Int32Value)value).ToInt8();
				return Int32Value.CreateUnknown();

			case ElementType.U1:
				if (value.IsInt32())
					return ((Int32Value)value).ToUInt8();
				return Int32Value.CreateUnknownUInt8();

			case ElementType.I2:
				if (value.IsInt32())
					return ((Int32Value)value).ToInt16();
				return Int32Value.CreateUnknown();

			case ElementType.U2:
				if (value.IsInt32())
					return ((Int32Value)value).ToUInt16();
				return Int32Value.CreateUnknownUInt16();

			case ElementType.I4:
			case ElementType.U4:
				if (value.IsInt32())
					return value;
				return Int32Value.CreateUnknown();

			case ElementType.I8:
			case ElementType.U8:
				if (value.IsInt64())
					return value;
				return Int64Value.CreateUnknown();

			case ElementType.R4:
				if (value.IsReal8())
					return ((Real8Value)value).ToSingle();
				return new UnknownValue();

			case ElementType.R8:
				if (value.IsReal8())
					return value;
				return new UnknownValue();
			}
			return value;
		}

		static Value GetValue(List<Value> list, int i) {
			if (0 <= i && i < list.Count)
				return list[i];
			return new UnknownValue();
		}

		public Value GetArg(int i) => GetValue(args, i);

		public Value GetArg(Parameter arg) {
			if (arg == null)
				return new UnknownValue();
			return GetArg(arg.Index);
		}

		TypeSig GetArgType(int index) {
			if (0 <= index && index < parameterDefs.Count)
				return parameterDefs[index].Type;
			return null;
		}

		public void SetArg(Parameter arg, Value value) {
			if (arg != null)
				SetArg(arg.Index, value);
		}

		public void MakeArgUnknown(Parameter arg) {
			if (arg != null)
				SetArg(arg, GetUnknownArg(arg.Index));
		}

		void SetArg(int index, Value value) {
			if (0 <= index && index < args.Count)
				args[index] = TruncateValue(value, GetArgType(index));
		}

		Value GetUnknownArg(int index) => GetUnknownValue(GetArgType(index));
		public Value GetLocal(int i) => GetValue(locals, i);

		public Value GetLocal(Local local) {
			if (local == null)
				return new UnknownValue();
			return GetLocal(local.Index);
		}

		public void SetLocal(Local local, Value value) {
			if (local != null)
				SetLocal(local.Index, value);
		}

		public void MakeLocalUnknown(Local local) {
			if (local != null)
				SetLocal(local.Index, GetUnknownLocal(local.Index));
		}

		void SetLocal(int index, Value value) {
			if (0 <= index && index < locals.Count)
				locals[index] = TruncateValue(value, localDefs[index].Type);
		}

		Value GetUnknownLocal(int index) {
			if (0 <= index && index < localDefs.Count)
				return GetUnknownValue(localDefs[index].Type);
			return new UnknownValue();
		}

		public int StackSize() => valueStack.Size;
		public void Push(Value value) => valueStack.Push(value);
		public void ClearStack() => valueStack.Clear();

		public void Pop(int num) {
			if (num < 0)
				valueStack.Clear();
			else
				valueStack.Pop(num);
		}

		public Value Pop() => valueStack.Pop();
		public Value Peek() => valueStack.Peek();

		public void Emulate(IEnumerable<Instr> instructions) {
			foreach (var instr in instructions)
				Emulate(instr.Instruction);
		}

		public void Emulate(IList<Instr> instructions, int start, int end) {
			for (int i = start; i < end; i++)
				Emulate(instructions[i].Instruction);
		}

		public void Emulate(Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Starg:
			case Code.Starg_S:	Emulate_Starg((Parameter)instr.Operand); break;
			case Code.Stloc:
			case Code.Stloc_S:	Emulate_Stloc((Local)instr.Operand); break;
			case Code.Stloc_0:	Emulate_Stloc(0); break;
			case Code.Stloc_1:	Emulate_Stloc(1); break;
			case Code.Stloc_2:	Emulate_Stloc(2); break;
			case Code.Stloc_3:	Emulate_Stloc(3); break;

			case Code.Ldarg:
			case Code.Ldarg_S:	valueStack.Push(GetArg((Parameter)instr.Operand)); break;
			case Code.Ldarg_0:	valueStack.Push(GetArg(0)); break;
			case Code.Ldarg_1:	valueStack.Push(GetArg(1)); break;
			case Code.Ldarg_2:	valueStack.Push(GetArg(2)); break;
			case Code.Ldarg_3:	valueStack.Push(GetArg(3)); break;
			case Code.Ldloc:
			case Code.Ldloc_S:	valueStack.Push(GetLocal((Local)instr.Operand)); break;
			case Code.Ldloc_0:	valueStack.Push(GetLocal(0)); break;
			case Code.Ldloc_1:	valueStack.Push(GetLocal(1)); break;
			case Code.Ldloc_2:	valueStack.Push(GetLocal(2)); break;
			case Code.Ldloc_3:	valueStack.Push(GetLocal(3)); break;

			case Code.Ldarga:
			case Code.Ldarga_S:	Emulate_Ldarga((Parameter)instr.Operand); break;
			case Code.Ldloca:
			case Code.Ldloca_S:	Emulate_Ldloca((Local)instr.Operand); break;

			case Code.Dup:		valueStack.CopyTop(); break;

			case Code.Ldc_I4:	valueStack.Push(new Int32Value((int)instr.Operand)); break;
			case Code.Ldc_I4_S:	valueStack.Push(new Int32Value((sbyte)instr.Operand)); break;
			case Code.Ldc_I8:	valueStack.Push(new Int64Value((long)instr.Operand)); break;
			case Code.Ldc_R4:	valueStack.Push(new Real8Value((float)instr.Operand)); break;
			case Code.Ldc_R8:	valueStack.Push(new Real8Value((double)instr.Operand)); break;
			case Code.Ldc_I4_0:	valueStack.Push(Int32Value.Zero); break;
			case Code.Ldc_I4_1:	valueStack.Push(Int32Value.One); break;
			case Code.Ldc_I4_2:	valueStack.Push(new Int32Value(2)); break;
			case Code.Ldc_I4_3:	valueStack.Push(new Int32Value(3)); break;
			case Code.Ldc_I4_4:	valueStack.Push(new Int32Value(4)); break;
			case Code.Ldc_I4_5:	valueStack.Push(new Int32Value(5)); break;
			case Code.Ldc_I4_6:	valueStack.Push(new Int32Value(6)); break;
			case Code.Ldc_I4_7:	valueStack.Push(new Int32Value(7)); break;
			case Code.Ldc_I4_8:	valueStack.Push(new Int32Value(8)); break;
			case Code.Ldc_I4_M1:valueStack.Push(new Int32Value(-1)); break;
			case Code.Ldnull:	valueStack.Push(NullValue.Instance); break;
			case Code.Ldstr:	valueStack.Push(new StringValue((string)instr.Operand)); break;
			case Code.Box:		valueStack.Push(new BoxedValue(valueStack.Pop())); break;

			case Code.Conv_U1:	Emulate_Conv_U1(instr); break;
			case Code.Conv_U2:	Emulate_Conv_U2(instr); break;
			case Code.Conv_U4:	Emulate_Conv_U4(instr); break;
			case Code.Conv_U8:	Emulate_Conv_U8(instr); break;
			case Code.Conv_I1:	Emulate_Conv_I1(instr); break;
			case Code.Conv_I2:	Emulate_Conv_I2(instr); break;
			case Code.Conv_I4:	Emulate_Conv_I4(instr); break;
			case Code.Conv_I8:	Emulate_Conv_I8(instr); break;
			case Code.Add:		Emulate_Add(instr); break;
			case Code.Sub:		Emulate_Sub(instr); break;
			case Code.Mul:		Emulate_Mul(instr); break;
			case Code.Div:		Emulate_Div(instr); break;
			case Code.Div_Un:	Emulate_Div_Un(instr); break;
			case Code.Rem:		Emulate_Rem(instr); break;
			case Code.Rem_Un:	Emulate_Rem_Un(instr); break;
			case Code.Neg:		Emulate_Neg(instr); break;
			case Code.And:		Emulate_And(instr); break;
			case Code.Or:		Emulate_Or(instr); break;
			case Code.Xor:		Emulate_Xor(instr); break;
			case Code.Not:		Emulate_Not(instr); break;
			case Code.Shl:		Emulate_Shl(instr); break;
			case Code.Shr:		Emulate_Shr(instr); break;
			case Code.Shr_Un:	Emulate_Shr_Un(instr); break;
			case Code.Ceq:		Emulate_Ceq(instr); break;
			case Code.Cgt:		Emulate_Cgt(instr); break;
			case Code.Cgt_Un:	Emulate_Cgt_Un(instr); break;
			case Code.Clt:		Emulate_Clt(instr); break;
			case Code.Clt_Un:	Emulate_Clt_Un(instr); break;
			case Code.Unbox_Any:Emulate_Unbox_Any(instr); break;

			case Code.Call:		Emulate_Call(instr); break;
			case Code.Callvirt:	Emulate_Callvirt(instr); break;

			case Code.Castclass: Emulate_Castclass(instr); break;
			case Code.Isinst:	Emulate_Isinst(instr); break;

			case Code.Add_Ovf:	Emulate_Add_Ovf(instr); break;
			case Code.Add_Ovf_Un: Emulate_Add_Ovf_Un(instr); break;
			case Code.Sub_Ovf:	Emulate_Sub_Ovf(instr); break;
			case Code.Sub_Ovf_Un: Emulate_Sub_Ovf_Un(instr); break;
			case Code.Mul_Ovf:	Emulate_Mul_Ovf(instr); break;
			case Code.Mul_Ovf_Un: Emulate_Mul_Ovf_Un(instr); break;

			case Code.Conv_Ovf_I1:		Emulate_Conv_Ovf_I1(instr); break;
			case Code.Conv_Ovf_I1_Un:	Emulate_Conv_Ovf_I1_Un(instr); break;
			case Code.Conv_Ovf_I2:		Emulate_Conv_Ovf_I2(instr); break;
			case Code.Conv_Ovf_I2_Un:	Emulate_Conv_Ovf_I2_Un(instr); break;
			case Code.Conv_Ovf_I4:		Emulate_Conv_Ovf_I4(instr); break;
			case Code.Conv_Ovf_I4_Un:	Emulate_Conv_Ovf_I4_Un(instr); break;
			case Code.Conv_Ovf_I8:		Emulate_Conv_Ovf_I8(instr); break;
			case Code.Conv_Ovf_I8_Un:	Emulate_Conv_Ovf_I8_Un(instr); break;
			case Code.Conv_Ovf_U1:		Emulate_Conv_Ovf_U1(instr); break;
			case Code.Conv_Ovf_U1_Un:	Emulate_Conv_Ovf_U1_Un(instr); break;
			case Code.Conv_Ovf_U2:		Emulate_Conv_Ovf_U2(instr); break;
			case Code.Conv_Ovf_U2_Un:	Emulate_Conv_Ovf_U2_Un(instr); break;
			case Code.Conv_Ovf_U4:		Emulate_Conv_Ovf_U4(instr); break;
			case Code.Conv_Ovf_U4_Un:	Emulate_Conv_Ovf_U4_Un(instr); break;
			case Code.Conv_Ovf_U8:		Emulate_Conv_Ovf_U8(instr); break;
			case Code.Conv_Ovf_U8_Un:	Emulate_Conv_Ovf_U8_Un(instr); break;

			case Code.Ldelem_I1: valueStack.Pop(2); valueStack.Push(Int32Value.CreateUnknown()); break;
			case Code.Ldelem_I2: valueStack.Pop(2); valueStack.Push(Int32Value.CreateUnknown()); break;
			case Code.Ldelem_I4: valueStack.Pop(2); valueStack.Push(Int32Value.CreateUnknown()); break;
			case Code.Ldelem_I8: valueStack.Pop(2); valueStack.Push(Int64Value.CreateUnknown()); break;
			case Code.Ldelem_U1: valueStack.Pop(2); valueStack.Push(Int32Value.CreateUnknownUInt8()); break;
			case Code.Ldelem_U2: valueStack.Pop(2); valueStack.Push(Int32Value.CreateUnknownUInt16()); break;
			case Code.Ldelem_U4: valueStack.Pop(2); valueStack.Push(Int32Value.CreateUnknown()); break;
			case Code.Ldelem:	 valueStack.Pop(2); valueStack.Push(GetUnknownValue(instr.Operand as ITypeDefOrRef)); break;

			case Code.Ldind_I1:	valueStack.Pop(); valueStack.Push(Int32Value.CreateUnknown()); break;
			case Code.Ldind_I2:	valueStack.Pop(); valueStack.Push(Int32Value.CreateUnknown()); break;
			case Code.Ldind_I4:	valueStack.Pop(); valueStack.Push(Int32Value.CreateUnknown()); break;
			case Code.Ldind_I8:	valueStack.Pop(); valueStack.Push(Int64Value.CreateUnknown()); break;
			case Code.Ldind_U1:	valueStack.Pop(); valueStack.Push(Int32Value.CreateUnknownUInt8()); break;
			case Code.Ldind_U2:	valueStack.Pop(); valueStack.Push(Int32Value.CreateUnknownUInt16()); break;
			case Code.Ldind_U4:	valueStack.Pop(); valueStack.Push(Int32Value.CreateUnknown()); break;

			case Code.Ldlen:	valueStack.Pop(); valueStack.Push(Int32Value.CreateUnknown()); break;
			case Code.Sizeof:	valueStack.Push(Int32Value.CreateUnknown()); break;

			case Code.Ldfld:	Emulate_Ldfld(instr); break;
			case Code.Ldsfld:	Emulate_Ldsfld(instr); break;

			case Code.Ldftn:	valueStack.Push(new ObjectValue(instr.Operand)); break;
			case Code.Ldsflda:	valueStack.Push(new ObjectValue(instr.Operand)); break;
			case Code.Ldtoken:	valueStack.Push(new ObjectValue(instr.Operand)); break;
			case Code.Ldvirtftn:valueStack.Pop(); valueStack.Push(new ObjectValue()); break;
			case Code.Ldflda:	valueStack.Pop(); valueStack.Push(new ObjectValue()); break;

			case Code.Unbox:

			case Code.Conv_R_Un:Emulate_Conv_R_Un(instr); break;
			case Code.Conv_R4:	Emulate_Conv_R4(instr); break;
			case Code.Conv_R8:	Emulate_Conv_R8(instr); break;

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
			case Code.Nop:
			case Code.Pop:
			case Code.Readonly:
			case Code.Refanytype:
			case Code.Refanyval:
			case Code.Ret:
			case Code.Rethrow:
			case Code.Stelem:
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
			case Code.Tailcall:
			case Code.Throw:
			case Code.Unaligned:
			case Code.Volatile:
			default:
				UpdateStack(instr);
				break;
			}
		}

		void UpdateStack(Instruction instr) {
			instr.CalculateStackUsage(out int pushes, out int pops);
			if (pops == -1)
				valueStack.Clear();
			else {
				valueStack.Pop(pops);
				valueStack.Push(pushes);
			}
		}

		void Emulate_Conv_U1(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_U1((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int32Value.Conv_U1((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Int32Value.Conv_U1((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknownUInt8()); break;
			}
		}

		void Emulate_Conv_I1(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_I1((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int32Value.Conv_I1((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Int32Value.Conv_I1((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_U2(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_U2((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int32Value.Conv_U2((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Int32Value.Conv_U2((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknownUInt16()); break;
			}
		}

		void Emulate_Conv_I2(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_I2((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int32Value.Conv_I2((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Int32Value.Conv_I2((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_U4(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_U4((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int32Value.Conv_U4((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Int32Value.Conv_U4((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_I4(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_I4((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int32Value.Conv_I4((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Int32Value.Conv_I4((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_U8(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int64Value.Conv_U8((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_U8((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Int64Value.Conv_U8((Real8Value)val1)); break;
			default:				valueStack.Push(Int64Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_I8(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int64Value.Conv_I8((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_I8((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Int64Value.Conv_I8((Real8Value)val1)); break;
			default:				valueStack.Push(Int64Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_I1(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_I1((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_I1((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_I1((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_I1_Un(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_I1_Un((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_I1_Un((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_I1_Un((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_I2(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_I2((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_I2((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_I2((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_I2_Un(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_I2_Un((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_I2_Un((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_I2_Un((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_I4(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_I4((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_I4((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_I4((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_I4_Un(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_I4_Un((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_I4_Un((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_I4_Un((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_I8(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_I8((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_I8((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_I8((Real8Value)val1)); break;
			default:				valueStack.Push(Int64Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_I8_Un(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_I8_Un((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_I8_Un((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_I8_Un((Real8Value)val1)); break;
			default:				valueStack.Push(Int64Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_U1(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_U1((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_U1((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_U1((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknownUInt8()); break;
			}
		}

		void Emulate_Conv_Ovf_U1_Un(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_U1_Un((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_U1_Un((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_U1_Un((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknownUInt8()); break;
			}
		}

		void Emulate_Conv_Ovf_U2(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_U2((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_U2((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_U2((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknownUInt16()); break;
			}
		}

		void Emulate_Conv_Ovf_U2_Un(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_U2_Un((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_U2_Un((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_U2_Un((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknownUInt16()); break;
			}
		}

		void Emulate_Conv_Ovf_U4(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_U4((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_U4((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_U4((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_U4_Un(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_U4_Un((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_U4_Un((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_U4_Un((Real8Value)val1)); break;
			default:				valueStack.Push(Int32Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_U8(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_U8((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_U8((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_U8((Real8Value)val1)); break;
			default:				valueStack.Push(Int64Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_Ovf_U8_Un(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_Ovf_U8_Un((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_Ovf_U8_Un((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_Ovf_U8_Un((Real8Value)val1)); break;
			default:				valueStack.Push(Int64Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_R_Un(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_R_Un((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_R_Un((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_R_Un((Real8Value)val1)); break;
			default:				valueStack.Push(Real8Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_R4(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_R4((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_R4((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_R4((Real8Value)val1)); break;
			default:				valueStack.Push(Real8Value.CreateUnknown()); break;
			}
		}

		void Emulate_Conv_R8(Instruction instr) {
			var val1 = valueStack.Pop();
			switch (val1.valueType) {
			case ValueType.Int32:	valueStack.Push(Int32Value.Conv_R8((Int32Value)val1)); break;
			case ValueType.Int64:	valueStack.Push(Int64Value.Conv_R8((Int64Value)val1)); break;
			case ValueType.Real8:	valueStack.Push(Real8Value.Conv_R8((Real8Value)val1)); break;
			default:				valueStack.Push(Real8Value.CreateUnknown()); break;
			}
		}

		void Emulate_Add(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Add((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Add((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Add((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Sub(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Sub((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Sub((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Sub((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Mul(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Mul((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Mul((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Mul((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Div(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Div((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Div((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Div((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Div_Un(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Div_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Div_Un((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Rem(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Rem((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Rem((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Rem((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Rem_Un(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Rem_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Rem_Un((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Neg(Instruction instr) {
			var val1 = valueStack.Pop();

			if (val1.IsInt32())
				valueStack.Push(Int32Value.Neg((Int32Value)val1));
			else if (val1.IsInt64())
				valueStack.Push(Int64Value.Neg((Int64Value)val1));
			else if (val1.IsReal8())
				valueStack.Push(Real8Value.Neg((Real8Value)val1));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Add_Ovf(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Add_Ovf((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Add_Ovf((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Add_Ovf((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Add_Ovf_Un(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Add_Ovf_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Add_Ovf_Un((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Add_Ovf_Un((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Sub_Ovf(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Sub_Ovf((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Sub_Ovf((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Sub_Ovf((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Sub_Ovf_Un(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Sub_Ovf_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Sub_Ovf_Un((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Sub_Ovf_Un((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Mul_Ovf(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Mul_Ovf((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Mul_Ovf((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Mul_Ovf((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Mul_Ovf_Un(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Mul_Ovf_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Mul_Ovf_Un((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Mul_Ovf_Un((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_And(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.And((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.And((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Or(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Or((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Or((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Xor(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Xor((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Xor((Int64Value)val1, (Int64Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Not(Instruction instr) {
			var val1 = valueStack.Pop();

			if (val1.IsInt32())
				valueStack.Push(Int32Value.Not((Int32Value)val1));
			else if (val1.IsInt64())
				valueStack.Push(Int64Value.Not((Int64Value)val1));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Shl(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Shl((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt32())
				valueStack.Push(Int64Value.Shl((Int64Value)val1, (Int32Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Shr(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Shr((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt32())
				valueStack.Push(Int64Value.Shr((Int64Value)val1, (Int32Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Shr_Un(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Shr_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt32())
				valueStack.Push(Int64Value.Shr_Un((Int64Value)val1, (Int32Value)val2));
			else
				valueStack.PushUnknown();
		}

		void Emulate_Ceq(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Ceq((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Ceq((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Ceq((Real8Value)val1, (Real8Value)val2));
			else if (val1.IsNull() && val2.IsNull())
				valueStack.Push(Int32Value.One);
			else
				valueStack.Push(Int32Value.CreateUnknownBool());
		}

		void Emulate_Cgt(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Cgt((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Cgt((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Cgt((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.Push(Int32Value.CreateUnknownBool());
		}

		void Emulate_Cgt_Un(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Cgt_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Cgt_Un((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Cgt_Un((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.Push(Int32Value.CreateUnknownBool());
		}

		void Emulate_Clt(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Clt((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Clt((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Clt((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.Push(Int32Value.CreateUnknownBool());
		}

		void Emulate_Clt_Un(Instruction instr) {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();

			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.Clt_Un((Int32Value)val1, (Int32Value)val2));
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.Clt_Un((Int64Value)val1, (Int64Value)val2));
			else if (val1.IsReal8() && val2.IsReal8())
				valueStack.Push(Real8Value.Clt_Un((Real8Value)val1, (Real8Value)val2));
			else
				valueStack.Push(Int32Value.CreateUnknownBool());
		}

		void Emulate_Unbox_Any(Instruction instr) {
			var val1 = valueStack.Pop();
			if (val1.IsBoxed())
				valueStack.Push(((BoxedValue)val1).value);
			else
				valueStack.PushUnknown();
		}

		void Emulate_Starg(Parameter arg) => SetArg(arg == null ? -1 : arg.Index, valueStack.Pop());
		void Emulate_Stloc(Local local) => Emulate_Stloc(local == null ? -1 : local.Index);
		void Emulate_Stloc(int index) => SetLocal(index, valueStack.Pop());

		void Emulate_Ldarga(Parameter arg) {
			valueStack.PushUnknown();
			MakeArgUnknown(arg);
		}

		void Emulate_Ldloca(Local local) => Emulate_Ldloca(local == null ? -1 : local.Index);

		void Emulate_Ldloca(int index) {
			valueStack.PushUnknown();
			SetLocal(index, GetUnknownLocal(index));
		}

		void Emulate_Call(Instruction instr) => Emulate_Call(instr, (IMethod)instr.Operand);
		void Emulate_Callvirt(Instruction instr) => Emulate_Call(instr, (IMethod)instr.Operand);

		void Emulate_Call(Instruction instr, IMethod method) {
			instr.CalculateStackUsage(out int pushes, out int pops);
			valueStack.Pop(pops);
			if (pushes == 1)
				valueStack.Push(GetUnknownValue(method.MethodSig.GetRetType()));
			else
				valueStack.Push(pushes);
		}

		void Emulate_Castclass(Instruction instr) {
			var val1 = valueStack.Pop();

			if (val1.IsNull())
				valueStack.Push(val1);
			else
				valueStack.PushUnknown();
		}

		void Emulate_Isinst(Instruction instr) {
			var val1 = valueStack.Pop();

			if (val1.IsNull())
				valueStack.Push(val1);
			else
				valueStack.PushUnknown();
		}

		void Emulate_Ldfld(Instruction instr) {
			/*var val1 =*/ valueStack.Pop();
			EmulateLoadField(instr.Operand as IField);
		}

		void Emulate_Ldsfld(Instruction instr) => EmulateLoadField(instr.Operand as IField);

		void EmulateLoadField(IField field) {
			if (field != null)
				valueStack.Push(GetUnknownValue(field.FieldSig.GetFieldType()));
			else
				valueStack.PushUnknown();
		}

		void EmulateIntOps2() {
			var val2 = valueStack.Pop();
			var val1 = valueStack.Pop();
			if (val1.IsInt32() && val2.IsInt32())
				valueStack.Push(Int32Value.CreateUnknown());
			else if (val1.IsInt64() && val2.IsInt64())
				valueStack.Push(Int64Value.CreateUnknown());
			else
				valueStack.PushUnknown();
		}
	}
}
