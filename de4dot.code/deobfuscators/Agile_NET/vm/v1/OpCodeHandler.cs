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

using System;
using System.Collections.Generic;
using System.IO;
using de4dot.blocks;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v1 {
	partial class OpCodeHandler {
		public string Name { get; set; }
		public OpCodeHandlerSigInfo OpCodeHandlerSigInfo { get; set; }
		public Predicate<UnknownHandlerInfo> Check { get; set; }
		public Func<BinaryReader, IInstructionOperandResolver, GenericParamContext, Instruction> Read { get; set; }

		public bool Detect(UnknownHandlerInfo info) {
			var sigInfo = OpCodeHandlerSigInfo;

			if (!Compare(sigInfo.NumStaticMethods, info.NumStaticMethods))
				return false;
			if (!Compare(sigInfo.NumInstanceMethods, info.NumInstanceMethods))
				return false;
			if (!Compare(sigInfo.NumVirtualMethods, info.NumVirtualMethods))
				return false;
			if (!Compare(sigInfo.NumCtors, info.NumCtors))
				return false;
			if (!Compare(sigInfo.ExecuteMethodThrows, info.ExecuteMethodThrows))
				return false;
			if (!Compare(sigInfo.ExecuteMethodPops, info.ExecuteMethodPops))
				return false;
			if (!info.HasSameFieldTypes(sigInfo.RequiredFieldTypes))
				return false;
			if (sigInfo.ExecuteMethodLocals != null && !new LocalTypes(info.ExecuteMethod).All(sigInfo.ExecuteMethodLocals))
				return false;

			if (Check != null)
				return Check(info);
			return true;
		}

		static bool Compare(int? val1, int val2) {
			if (!val1.HasValue)
				return true;
			return val1.Value == val2;
		}

		public override string ToString() => Name;
	}

	static partial class OpCodeHandlers {
		static Instruction arithmetic_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			switch (reader.ReadByte()) {
			case 0: return OpCodes.Add.ToInstruction();
			case 1: return OpCodes.Add_Ovf.ToInstruction();
			case 2: return OpCodes.Add_Ovf_Un.ToInstruction();
			case 3: return OpCodes.Sub.ToInstruction();
			case 4: return OpCodes.Sub_Ovf.ToInstruction();
			case 5: return OpCodes.Sub_Ovf_Un.ToInstruction();
			case 6: return OpCodes.Mul.ToInstruction();
			case 7: return OpCodes.Mul_Ovf.ToInstruction();
			case 8: return OpCodes.Mul_Ovf_Un.ToInstruction();
			case 9: return OpCodes.Div.ToInstruction();
			case 10: return OpCodes.Div_Un.ToInstruction();
			case 11: return OpCodes.Rem.ToInstruction();
			case 12: return OpCodes.Rem_Un.ToInstruction();
			default: throw new ApplicationException("Invalid opcode");
			}
		}

		static bool newarr_check(UnknownHandlerInfo info) =>
			DotNetUtils.CallsMethod(info.ExecuteMethod, "System.Type System.Reflection.Module::ResolveType(System.Int32)");

		static Instruction newarr_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			new Instruction(OpCodes.Newarr, resolver.ResolveToken(reader.ReadUInt32(), gpContext));

		static Instruction box_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			var instr = new Instruction();
			switch (reader.ReadByte()) {
			case 0: instr.OpCode = OpCodes.Box; break;
			case 1: instr.OpCode = OpCodes.Unbox_Any; break;
			default: throw new ApplicationException("Invalid opcode");
			}
			instr.Operand = resolver.ResolveToken(reader.ReadUInt32(), gpContext);
			return instr;
		}

		static Instruction call_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			var instr = new Instruction();
			switch (reader.ReadByte()) {
			case 0: instr.OpCode = OpCodes.Newobj; break;
			case 1: instr.OpCode = OpCodes.Call; break;
			case 2: instr.OpCode = OpCodes.Callvirt; break;
			default: throw new ApplicationException("Invalid opcode");
			}
			instr.Operand = resolver.ResolveToken(reader.ReadUInt32(), gpContext);
			return instr;
		}

		static Instruction cast_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			var instr = new Instruction();
			switch (reader.ReadByte()) {
			case 0: instr.OpCode = OpCodes.Castclass; break;
			case 1: instr.OpCode = OpCodes.Isinst; break;
			default: throw new ApplicationException("Invalid opcode");
			}
			instr.Operand = resolver.ResolveToken(reader.ReadUInt32(), gpContext);
			return instr;
		}

		static Instruction compare_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			int type = reader.ReadByte();
			var instr = new Instruction();
			switch (type) {
			case 0: instr.OpCode = OpCodes.Br; break;
			case 1: instr.OpCode = OpCodes.Brtrue; break;
			case 2: instr.OpCode = OpCodes.Brfalse; break;
			case 3: instr.OpCode = OpCodes.Beq; break;
			case 4: instr.OpCode = OpCodes.Bge; break;
			case 5: instr.OpCode = OpCodes.Bgt; break;
			case 6: instr.OpCode = OpCodes.Ble; break;
			case 7: instr.OpCode = OpCodes.Blt; break;
			case 8: instr.OpCode = OpCodes.Bne_Un; break;
			case 9: instr.OpCode = OpCodes.Bge_Un; break;
			case 10: instr.OpCode = OpCodes.Bgt_Un; break;
			case 11: instr.OpCode = OpCodes.Ble_Un; break;
			case 12: instr.OpCode = OpCodes.Blt_Un; break;
			case 13: instr.OpCode = OpCodes.Ceq; break;
			case 14: instr.OpCode = OpCodes.Cgt; break;
			case 15: instr.OpCode = OpCodes.Clt; break;
			case 16: instr.OpCode = OpCodes.Cgt_Un; break;
			case 17: instr.OpCode = OpCodes.Clt_Un; break;
			default: throw new ApplicationException("Invalid opcode");
			}
			if (type < 13)
				instr.Operand = new TargetDisplOperand(reader.ReadInt32());

			return instr;
		}

		class InstructionInfo1 {
			public byte Type { get; set; }
			public bool Second { get; set; }
			public bool Third { get; set; }
			public OpCode OpCode { get; set; }
		}
		static List<InstructionInfo1> instructionInfos1 = new List<InstructionInfo1> {
			new InstructionInfo1 { Type = 0, Second = false, Third = false, OpCode = OpCodes.Conv_I1 },
			new InstructionInfo1 { Type = 1, Second = false, Third = false, OpCode = OpCodes.Conv_I2 },
			new InstructionInfo1 { Type = 2, Second = false, Third = false, OpCode = OpCodes.Conv_I4 },
			new InstructionInfo1 { Type = 3, Second = false, Third = false, OpCode = OpCodes.Conv_I8 },
			new InstructionInfo1 { Type = 4, Second = false, Third = false, OpCode = OpCodes.Conv_R4 },
			new InstructionInfo1 { Type = 5, Second = false, Third = false, OpCode = OpCodes.Conv_R8 },
			new InstructionInfo1 { Type = 6, Second = false, Third = false, OpCode = OpCodes.Conv_U1 },
			new InstructionInfo1 { Type = 7, Second = false, Third = false, OpCode = OpCodes.Conv_U2 },
			new InstructionInfo1 { Type = 8, Second = false, Third = false, OpCode = OpCodes.Conv_U4 },
			new InstructionInfo1 { Type = 9, Second = false, Third = false, OpCode = OpCodes.Conv_U8 },
			new InstructionInfo1 { Type = 10, Second = false, Third = false, OpCode = OpCodes.Conv_I },
			new InstructionInfo1 { Type = 11, Second = false, Third = false, OpCode = OpCodes.Conv_U },

			new InstructionInfo1 { Type = 0, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I1 },
			new InstructionInfo1 { Type = 1, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I2 },
			new InstructionInfo1 { Type = 2, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I4 },
			new InstructionInfo1 { Type = 3, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I8 },
			new InstructionInfo1 { Type = 6, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U1 },
			new InstructionInfo1 { Type = 7, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U2 },
			new InstructionInfo1 { Type = 8, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U4 },
			new InstructionInfo1 { Type = 9, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U8 },
			new InstructionInfo1 { Type = 10, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I },
			new InstructionInfo1 { Type = 11, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U },

			new InstructionInfo1 { Type = 0, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I1_Un },
			new InstructionInfo1 { Type = 1, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I2_Un },
			new InstructionInfo1 { Type = 2, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I4_Un },
			new InstructionInfo1 { Type = 3, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I8_Un },
			new InstructionInfo1 { Type = 6, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U1_Un },
			new InstructionInfo1 { Type = 7, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U2_Un },
			new InstructionInfo1 { Type = 8, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U4_Un },
			new InstructionInfo1 { Type = 9, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U8_Un },
			new InstructionInfo1 { Type = 10, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I_Un },
			new InstructionInfo1 { Type = 11, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U_Un },
			new InstructionInfo1 { Type = 12, Second = true, Third = true, OpCode = OpCodes.Conv_R_Un },
		};
		static Instruction convert_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			byte type = reader.ReadByte();
			bool second = reader.ReadBoolean();
			bool third = reader.ReadBoolean();

			Instruction instr = null;
			foreach (var info in instructionInfos1) {
				if (type != info.Type || info.Second != second || info.Third != third)
					continue;

				instr = new Instruction(info.OpCode);
				break;
			}
			if (instr == null)
				throw new ApplicationException("Invalid opcode");

			return instr;
		}

		static Instruction dup_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			switch (reader.ReadByte()) {
			case 0: return OpCodes.Dup.ToInstruction();
			case 1: return OpCodes.Pop.ToInstruction();
			default: throw new ApplicationException("Invalid opcode");
			}
		}

		class InstructionInfo2 {
			public bool First { get; set; }
			public bool Second { get; set; }
			public int Value { get; set; }
			public OpCode OpCode { get; set; }
		}
		static List<InstructionInfo2> instructionInfos2 = new List<InstructionInfo2> {
			new InstructionInfo2 { First = false, Second = true, Value = 24, OpCode = OpCodes.Stelem_I },
			new InstructionInfo2 { First = false, Second = true, Value = 4, OpCode = OpCodes.Stelem_I1 },
			new InstructionInfo2 { First = false, Second = true, Value = 6, OpCode = OpCodes.Stelem_I2 },
			new InstructionInfo2 { First = false, Second = true, Value = 8, OpCode = OpCodes.Stelem_I4 },
			new InstructionInfo2 { First = false, Second = true, Value = 10, OpCode = OpCodes.Stelem_I8 },
			new InstructionInfo2 { First = false, Second = true, Value = 12, OpCode = OpCodes.Stelem_R4 },
			new InstructionInfo2 { First = false, Second = true, Value = 13, OpCode = OpCodes.Stelem_R8 },
			new InstructionInfo2 { First = false, Second = true, Value = 28, OpCode = OpCodes.Stelem_Ref },
			new InstructionInfo2 { First = false, Second = false, Value = 0, OpCode = OpCodes.Stelem },

			new InstructionInfo2 { First = true, Second = true, Value = 24, OpCode = OpCodes.Ldelem_I },
			new InstructionInfo2 { First = true, Second = true, Value = 4, OpCode = OpCodes.Ldelem_I1 },
			new InstructionInfo2 { First = true, Second = true, Value = 6, OpCode = OpCodes.Ldelem_I2 },
			new InstructionInfo2 { First = true, Second = true, Value = 8, OpCode = OpCodes.Ldelem_I4 },
			new InstructionInfo2 { First = true, Second = true, Value = 10, OpCode = OpCodes.Ldelem_I8 },
			new InstructionInfo2 { First = true, Second = true, Value = 5, OpCode = OpCodes.Ldelem_U1 },
			new InstructionInfo2 { First = true, Second = true, Value = 7, OpCode = OpCodes.Ldelem_U2 },
			new InstructionInfo2 { First = true, Second = true, Value = 9, OpCode = OpCodes.Ldelem_U4 },
			new InstructionInfo2 { First = true, Second = true, Value = 12, OpCode = OpCodes.Ldelem_R4 },
			new InstructionInfo2 { First = true, Second = true, Value = 13, OpCode = OpCodes.Ldelem_R8 },
			new InstructionInfo2 { First = true, Second = true, Value = 28, OpCode = OpCodes.Ldelem_Ref },
			new InstructionInfo2 { First = true, Second = false, Value = 0, OpCode = OpCodes.Ldelem },
		};
		static Instruction ldelem_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			Instruction instr = null;
			bool first = reader.ReadBoolean();
			bool second = reader.ReadBoolean();
			int value = reader.ReadInt32();
			foreach (var info in instructionInfos2) {
				if (info.First != first || info.Second != second)
					continue;
				if (second && value != info.Value)
					continue;

				if (second)
					instr = new Instruction(info.OpCode);
				else
					instr = new Instruction(info.OpCode, resolver.ResolveToken((uint)value, gpContext));
				break;
			}
			if (instr == null)
				throw new ApplicationException("Invalid opcode");

			return instr;
		}

		static bool endfinally_check(UnknownHandlerInfo info) =>
			DotNetUtils.CallsMethod(info.ExecuteMethod, "System.Reflection.MethodInfo System.Type::GetMethod(System.String,System.Reflection.BindingFlags)");

		static Instruction endfinally_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			OpCodes.Endfinally.ToInstruction();

		static Instruction ldfld_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			byte b = reader.ReadByte();
			var field = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as IField;
			switch (b) {
			case 0: return new Instruction(null, new FieldInstructionOperand(OpCodes.Ldsfld, OpCodes.Ldfld, field));
			case 1: return new Instruction(null, new FieldInstructionOperand(OpCodes.Ldsflda, OpCodes.Ldflda, field));
			case 2: return new Instruction(null, new FieldInstructionOperand(OpCodes.Stsfld, OpCodes.Stfld, field));
			default: throw new ApplicationException("Invalid opcode");
			}
		}

		static Instruction initobj_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			new Instruction(OpCodes.Initobj, resolver.ResolveToken(reader.ReadUInt32(), gpContext));

		static Instruction ldloc_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			bool isLdarg = reader.ReadBoolean();
			ushort index = reader.ReadUInt16();

			var instr = new Instruction();
			if (isLdarg) {
				instr.OpCode = OpCodes.Ldarg;
				instr.Operand = new ArgOperand(index);
			}
			else {
				instr.OpCode = OpCodes.Ldloc;
				instr.Operand = new LocalOperand(index);
			}

			return instr;
		}

		static Instruction ldloca_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			var instr = new Instruction();
			if (reader.ReadBoolean()) {
				instr.OpCode = OpCodes.Ldarga;
				instr.Operand = new ArgOperand(reader.ReadUInt16());
			}
			else {
				instr.OpCode = OpCodes.Ldloca;
				instr.Operand = new LocalOperand(reader.ReadUInt16());
			}

			return instr;
		}

		static Instruction ldelema_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			new Instruction(OpCodes.Ldelema, null);

		static Instruction ldlen_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			OpCodes.Ldlen.ToInstruction();

		static Instruction ldobj_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			new Instruction(OpCodes.Ldobj, null);

		static Instruction ldstr_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			OpCodes.Ldstr.ToInstruction(reader.ReadString());

		static bool ldtoken_check(UnknownHandlerInfo info) =>
			DotNetUtils.CallsMethod(info.ExecuteMethod, "System.Reflection.MemberInfo System.Reflection.Module::ResolveMember(System.Int32)");

		static Instruction ldtoken_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			new Instruction(OpCodes.Ldtoken, resolver.ResolveToken(reader.ReadUInt32(), gpContext));

		static bool leave_check(UnknownHandlerInfo info) =>
			!DotNetUtils.CallsMethod(info.ExecuteMethod, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)") &&
			!DotNetUtils.CallsMethod(info.ExecuteMethod, "System.Type System.Reflection.Module::ResolveType(System.Int32)") &&
			!DotNetUtils.CallsMethod(info.ExecuteMethod, "System.Reflection.MemberInfo System.Reflection.Module::ResolveMember(System.Int32)");

		static Instruction leave_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			int displacement = reader.ReadInt32();
			return new Instruction(OpCodes.Leave, new TargetDisplOperand(displacement));
		}

		static Instruction ldc_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			switch ((ElementType)reader.ReadByte()) {
			case ElementType.I4: return Instruction.CreateLdcI4(reader.ReadInt32());
			case ElementType.I8: return OpCodes.Ldc_I8.ToInstruction(reader.ReadInt64());
			case ElementType.R4: return OpCodes.Ldc_R4.ToInstruction(reader.ReadSingle());
			case ElementType.R8: return OpCodes.Ldc_R8.ToInstruction(reader.ReadDouble());
			case ElementType.Object: return OpCodes.Ldnull.ToInstruction();
			default: throw new ApplicationException("Invalid opcode");
			}
		}

		static Instruction ldftn_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			byte code = reader.ReadByte();
			uint token = reader.ReadUInt32();

			switch (code) {
			case 0:
				return new Instruction(OpCodes.Ldftn, resolver.ResolveToken(token, gpContext));

			case 1:
				reader.ReadInt32();	// token of newobj .ctor
				return new Instruction(OpCodes.Ldvirtftn, resolver.ResolveToken(token, gpContext));

			default:
				throw new ApplicationException("Invalid opcode");
			}
		}

		static Instruction logical_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			switch (reader.ReadByte()) {
			case 0: return OpCodes.And.ToInstruction();
			case 1: return OpCodes.Or.ToInstruction();
			case 2: return OpCodes.Xor.ToInstruction();
			case 3: return OpCodes.Shl.ToInstruction();
			case 4: return OpCodes.Shr.ToInstruction();
			case 5: return OpCodes.Shr_Un.ToInstruction();
			default: throw new ApplicationException("Invalid opcode");
			}
		}

		static bool nop_check(UnknownHandlerInfo info) => IsEmptyMethod(info.ReadMethod) && IsEmptyMethod(info.ExecuteMethod);

		static bool IsEmptyMethod(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Ret)
					return true;
				if (instr.OpCode.Code != Code.Nop)
					break;
			}
			return false;
		}

		static Instruction nop_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			OpCodes.Nop.ToInstruction();

		static bool ret_check(UnknownHandlerInfo info) =>
			DotNetUtils.CallsMethod(info.ExecuteMethod, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)");

		static Instruction ret_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			reader.ReadInt32();	// token of current method
			return OpCodes.Ret.ToInstruction();
		}

		static bool rethrow_check(UnknownHandlerInfo info) => info.ExecuteMethod.Body.Variables.Count == 0;
		static Instruction rethrow_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			OpCodes.Rethrow.ToInstruction();

		static Instruction stloc_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			bool isStarg = reader.ReadBoolean();
			ushort index = reader.ReadUInt16();

			var instr = new Instruction();
			if (isStarg) {
				instr.OpCode = OpCodes.Starg;
				instr.Operand = new ArgOperand(index);
			}
			else {
				instr.OpCode = OpCodes.Stloc;
				instr.Operand = new LocalOperand(index);
				reader.ReadInt32();	// ElementType of local
			}

			return instr;
		}

		static Instruction stobj_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			new Instruction(OpCodes.Stobj, null);

		static Instruction switch_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			int numTargets = reader.ReadInt32();
			int[] targetDispls = new int[numTargets];
			for (int i = 0; i < targetDispls.Length; i++)
				targetDispls[i] = reader.ReadInt32();
			return new Instruction(OpCodes.Switch, new SwitchTargetDisplOperand(targetDispls));
		}

		static bool throw_check(UnknownHandlerInfo info) =>
			!DotNetUtils.CallsMethod(info.ExecuteMethod, "System.Reflection.MethodInfo System.Type::GetMethod(System.String,System.Reflection.BindingFlags)");

		static Instruction throw_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) =>
			OpCodes.Throw.ToInstruction();

		static Instruction neg_read(BinaryReader reader, IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			switch (reader.ReadByte()) {
			case 0: return OpCodes.Neg.ToInstruction();
			case 1: return OpCodes.Not.ToInstruction();
			default: throw new ApplicationException("Invalid opcode");
			}
		}
	}
}
