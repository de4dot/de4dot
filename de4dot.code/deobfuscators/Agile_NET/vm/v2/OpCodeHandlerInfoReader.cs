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
using dnlib.DotNet.Emit;
using dnlib.DotNet;

namespace de4dot.code.deobfuscators.Agile_NET.vm.v2 {
	class OpCodeHandlerInfoReader {
		IInstructionOperandResolver resolver;
		Dictionary<HandlerTypeCode, Func<BinaryReader, Instruction>> readHandlers;
		readonly GenericParamContext gpContext;

		public OpCodeHandlerInfoReader(IInstructionOperandResolver resolver, GenericParamContext gpContext) {
			this.resolver = resolver;
			this.gpContext = gpContext;
			this.readHandlers = new Dictionary<HandlerTypeCode, Func<BinaryReader, Instruction>> {
				{ HandlerTypeCode.Add,			Handler_Add },
				{ HandlerTypeCode.Add_Ovf,		Handler_Add_Ovf },
				{ HandlerTypeCode.Add_Ovf_Un,	Handler_Add_Ovf_Un },
				{ HandlerTypeCode.And,			Handler_And },
				{ HandlerTypeCode.Beq,			Handler_Beq },
				{ HandlerTypeCode.Bge,			Handler_Bge },
				{ HandlerTypeCode.Bge_Un,		Handler_Bge_Un },
				{ HandlerTypeCode.Bgt,			Handler_Bgt },
				{ HandlerTypeCode.Bgt_Un,		Handler_Bgt_Un },
				{ HandlerTypeCode.Ble,			Handler_Ble },
				{ HandlerTypeCode.Ble_Un,		Handler_Ble_Un },
				{ HandlerTypeCode.Blt,			Handler_Blt },
				{ HandlerTypeCode.Blt_Un,		Handler_Blt_Un },
				{ HandlerTypeCode.Bne_Un,		Handler_Bne_Un },
				{ HandlerTypeCode.Box,			Handler_Box },
				{ HandlerTypeCode.Br,			Handler_Br },
				{ HandlerTypeCode.Brfalse,		Handler_Brfalse },
				{ HandlerTypeCode.Brtrue,		Handler_Brtrue },
				{ HandlerTypeCode.Call,			Handler_Call },
				{ HandlerTypeCode.Callvirt,		Handler_Callvirt },
				{ HandlerTypeCode.Castclass,	Handler_Castclass },
				{ HandlerTypeCode.Ceq,			Handler_Ceq },
				{ HandlerTypeCode.Cgt,			Handler_Cgt },
				{ HandlerTypeCode.Cgt_Un,		Handler_Cgt_Un },
				{ HandlerTypeCode.Clt,			Handler_Clt },
				{ HandlerTypeCode.Clt_Un,		Handler_Clt_Un },
				{ HandlerTypeCode.Conv,			Handler_Conv },
				{ HandlerTypeCode.Div,			Handler_Div },
				{ HandlerTypeCode.Div_Un,		Handler_Div_Un },
				{ HandlerTypeCode.Dup,			Handler_Dup },
				{ HandlerTypeCode.Endfinally,	Handler_Endfinally },
				{ HandlerTypeCode.Initobj,		Handler_Initobj },
				{ HandlerTypeCode.Isinst,		Handler_Isinst },
				{ HandlerTypeCode.Ldarg,		Handler_Ldarg },
				{ HandlerTypeCode.Ldarga,		Handler_Ldarga },
				{ HandlerTypeCode.Ldc,			Handler_Ldc },
				{ HandlerTypeCode.Ldelem,		Handler_Ldelem },
				{ HandlerTypeCode.Ldelema,		Handler_Ldelema },
				{ HandlerTypeCode.Ldfld_Ldsfld,	Handler_Ldfld_Ldsfld },
				{ HandlerTypeCode.Ldflda_Ldsflda, Handler_Ldflda_Ldsflda },
				{ HandlerTypeCode.Ldftn,		Handler_Ldftn },
				{ HandlerTypeCode.Ldlen,		Handler_Ldlen },
				{ HandlerTypeCode.Ldloc,		Handler_Ldloc },
				{ HandlerTypeCode.Ldloca,		Handler_Ldloca },
				{ HandlerTypeCode.Ldobj,		Handler_Ldobj },
				{ HandlerTypeCode.Ldstr,		Handler_Ldstr },
				{ HandlerTypeCode.Ldtoken,		Handler_Ldtoken },
				{ HandlerTypeCode.Ldvirtftn,	Handler_Ldvirtftn },
				{ HandlerTypeCode.Leave,		Handler_Leave },
				{ HandlerTypeCode.Mul,			Handler_Mul },
				{ HandlerTypeCode.Mul_Ovf,		Handler_Mul_Ovf },
				{ HandlerTypeCode.Mul_Ovf_Un,	Handler_Mul_Ovf_Un },
				{ HandlerTypeCode.Neg,			Handler_Neg },
				{ HandlerTypeCode.Newarr,		Handler_Newarr },
				{ HandlerTypeCode.Newobj,		Handler_Newobj },
				{ HandlerTypeCode.Nop,			Handler_Nop },
				{ HandlerTypeCode.Not,			Handler_Not },
				{ HandlerTypeCode.Or,			Handler_Or },
				{ HandlerTypeCode.Pop,			Handler_Pop },
				{ HandlerTypeCode.Rem,			Handler_Rem },
				{ HandlerTypeCode.Rem_Un,		Handler_Rem_Un },
				{ HandlerTypeCode.Ret,			Handler_Ret },
				{ HandlerTypeCode.Rethrow,		Handler_Rethrow },
				{ HandlerTypeCode.Shl,			Handler_Shl },
				{ HandlerTypeCode.Shr,			Handler_Shr },
				{ HandlerTypeCode.Shr_Un,		Handler_Shr_Un },
				{ HandlerTypeCode.Starg,		Handler_Starg },
				{ HandlerTypeCode.Stelem,		Handler_Stelem },
				{ HandlerTypeCode.Stfld_Stsfld,	Handler_Stfld_Stsfld },
				{ HandlerTypeCode.Stloc,		Handler_Stloc },
				{ HandlerTypeCode.Stobj,		Handler_Stobj },
				{ HandlerTypeCode.Sub,			Handler_Sub },
				{ HandlerTypeCode.Sub_Ovf,		Handler_Sub_Ovf },
				{ HandlerTypeCode.Sub_Ovf_Un,	Handler_Sub_Ovf_Un },
				{ HandlerTypeCode.Switch,		Handler_Switch },
				{ HandlerTypeCode.Throw,		Handler_Throw },
				{ HandlerTypeCode.Unbox_Any,	Handler_Unbox_Any },
				{ HandlerTypeCode.Xor,			Handler_Xor },
			};
		}

		public Instruction Read(HandlerTypeCode typeCode, BinaryReader reader) {
			Func<BinaryReader, Instruction> readHandler;
			if (!readHandlers.TryGetValue(typeCode, out readHandler))
				throw new ApplicationException("Invalid handler type");
			return readHandler(reader);
		}

		Instruction Handler_Add(BinaryReader reader) {
			return OpCodes.Add.ToInstruction();
		}

		Instruction Handler_Add_Ovf(BinaryReader reader) {
			return OpCodes.Add_Ovf.ToInstruction();
		}

		Instruction Handler_Add_Ovf_Un(BinaryReader reader) {
			return OpCodes.Add_Ovf_Un.ToInstruction();
		}

		Instruction Handler_And(BinaryReader reader) {
			return OpCodes.And.ToInstruction();
		}

		Instruction Handler_Beq(BinaryReader reader) {
			return new Instruction(OpCodes.Beq, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Bge(BinaryReader reader) {
			return new Instruction(OpCodes.Bge, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Bge_Un(BinaryReader reader) {
			return new Instruction(OpCodes.Bge_Un, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Bgt(BinaryReader reader) {
			return new Instruction(OpCodes.Bgt, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Bgt_Un(BinaryReader reader) {
			return new Instruction(OpCodes.Bgt_Un, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Ble(BinaryReader reader) {
			return new Instruction(OpCodes.Ble, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Ble_Un(BinaryReader reader) {
			return new Instruction(OpCodes.Ble_Un, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Blt(BinaryReader reader) {
			return new Instruction(OpCodes.Blt, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Blt_Un(BinaryReader reader) {
			return new Instruction(OpCodes.Blt_Un, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Bne_Un(BinaryReader reader) {
			return new Instruction(OpCodes.Bne_Un, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Box(BinaryReader reader) {
			var type = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as ITypeDefOrRef;
			return OpCodes.Box.ToInstruction(type);
		}

		Instruction Handler_Br(BinaryReader reader) {
			return new Instruction(OpCodes.Br, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Brfalse(BinaryReader reader) {
			return new Instruction(OpCodes.Brfalse, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Brtrue(BinaryReader reader) {
			return new Instruction(OpCodes.Brtrue, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Call(BinaryReader reader) {
			var method = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as IMethod;
			return OpCodes.Call.ToInstruction(method);
		}

		Instruction Handler_Callvirt(BinaryReader reader) {
			var method = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as IMethod;
			return OpCodes.Callvirt.ToInstruction(method);
		}

		Instruction Handler_Castclass(BinaryReader reader) {
			var type = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as ITypeDefOrRef;
			return OpCodes.Castclass.ToInstruction(type);
		}

		Instruction Handler_Ceq(BinaryReader reader) {
			return OpCodes.Ceq.ToInstruction();
		}

		Instruction Handler_Cgt(BinaryReader reader) {
			return OpCodes.Cgt.ToInstruction();
		}

		Instruction Handler_Cgt_Un(BinaryReader reader) {
			return OpCodes.Cgt_Un.ToInstruction();
		}

		Instruction Handler_Clt(BinaryReader reader) {
			return OpCodes.Clt.ToInstruction();
		}

		Instruction Handler_Clt_Un(BinaryReader reader) {
			return OpCodes.Clt_Un.ToInstruction();
		}

		class ConvInfo {
			public byte Type { get; private set; }
			public bool Second { get; private set; }
			public bool Third { get; private set; }
			public OpCode OpCode { get; private set; }
			public ConvInfo(byte type, bool second, bool third, OpCode opCode) {
				this.Type = type;
				this.Second = second;
				this.Third = third;
				this.OpCode = opCode;
			}
		}
		readonly static List<ConvInfo> instructionInfos1 = new List<ConvInfo> {
			new ConvInfo(0, false, false, OpCodes.Conv_I1),
			new ConvInfo(1, false, false, OpCodes.Conv_I2),
			new ConvInfo(2, false, false, OpCodes.Conv_I4),
			new ConvInfo(3, false, false, OpCodes.Conv_I8),
			new ConvInfo(4, false, false, OpCodes.Conv_R4),
			new ConvInfo(5, false, false, OpCodes.Conv_R8),
			new ConvInfo(6, false, false, OpCodes.Conv_U1),
			new ConvInfo(7, false, false, OpCodes.Conv_U2),
			new ConvInfo(8, false, false, OpCodes.Conv_U4),
			new ConvInfo(9, false, false, OpCodes.Conv_U8),
			new ConvInfo(10, false, false, OpCodes.Conv_I),
			new ConvInfo(11, false, false, OpCodes.Conv_U),

			new ConvInfo(0, true, false, OpCodes.Conv_Ovf_I1),
			new ConvInfo(1, true, false, OpCodes.Conv_Ovf_I2),
			new ConvInfo(2, true, false, OpCodes.Conv_Ovf_I4),
			new ConvInfo(3, true, false, OpCodes.Conv_Ovf_I8),
			new ConvInfo(6, true, false, OpCodes.Conv_Ovf_U1),
			new ConvInfo(7, true, false, OpCodes.Conv_Ovf_U2),
			new ConvInfo(8, true, false, OpCodes.Conv_Ovf_U4),
			new ConvInfo(9, true, false, OpCodes.Conv_Ovf_U8),
			new ConvInfo(10, true, false, OpCodes.Conv_Ovf_I),
			new ConvInfo(11, true, false, OpCodes.Conv_Ovf_U),

			new ConvInfo(0, true, true, OpCodes.Conv_Ovf_I1_Un),
			new ConvInfo(1, true, true, OpCodes.Conv_Ovf_I2_Un),
			new ConvInfo(2, true, true, OpCodes.Conv_Ovf_I4_Un),
			new ConvInfo(3, true, true, OpCodes.Conv_Ovf_I8_Un),
			new ConvInfo(6, true, true, OpCodes.Conv_Ovf_U1_Un),
			new ConvInfo(7, true, true, OpCodes.Conv_Ovf_U2_Un),
			new ConvInfo(8, true, true, OpCodes.Conv_Ovf_U4_Un),
			new ConvInfo(9, true, true, OpCodes.Conv_Ovf_U8_Un),
			new ConvInfo(10, true, true, OpCodes.Conv_Ovf_I_Un),
			new ConvInfo(11, true, true, OpCodes.Conv_Ovf_U_Un),
			new ConvInfo(12, true, true, OpCodes.Conv_R_Un),
		};
		Instruction Handler_Conv(BinaryReader reader) {
			byte type = reader.ReadByte();
			bool second = reader.ReadBoolean();
			bool third = reader.ReadBoolean();

			Instruction instr = null;
			foreach (var info in instructionInfos1) {
				if (type != info.Type || info.Second != second || info.Third != third)
					continue;

				instr = new Instruction { OpCode = info.OpCode };
				break;
			}
			if (instr == null)
				throw new ApplicationException("Invalid opcode");

			return instr;
		}

		Instruction Handler_Div(BinaryReader reader) {
			return OpCodes.Div.ToInstruction();
		}

		Instruction Handler_Div_Un(BinaryReader reader) {
			return OpCodes.Div_Un.ToInstruction();
		}

		Instruction Handler_Dup(BinaryReader reader) {
			return OpCodes.Dup.ToInstruction();
		}

		Instruction Handler_Endfinally(BinaryReader reader) {
			return OpCodes.Endfinally.ToInstruction();
		}

		Instruction Handler_Initobj(BinaryReader reader) {
			var type = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as ITypeDefOrRef;
			return OpCodes.Initobj.ToInstruction(type);
		}

		Instruction Handler_Isinst(BinaryReader reader) {
			var type = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as ITypeDefOrRef;
			return OpCodes.Isinst.ToInstruction(type);
		}

		Instruction Handler_Ldarg(BinaryReader reader) {
			return new Instruction(OpCodes.Ldarg, new ArgOperand(reader.ReadUInt16()));
		}

		Instruction Handler_Ldarga(BinaryReader reader) {
			return new Instruction(OpCodes.Ldarga, new ArgOperand(reader.ReadUInt16()));
		}

		Instruction Handler_Ldc(BinaryReader reader) {
			switch ((ElementType)reader.ReadByte()) {
			case ElementType.I4:		return Instruction.CreateLdcI4(reader.ReadInt32());
			case ElementType.I8:		return OpCodes.Ldc_I8.ToInstruction(reader.ReadInt64());
			case ElementType.R4:		return OpCodes.Ldc_R4.ToInstruction(reader.ReadSingle());
			case ElementType.R8:		return OpCodes.Ldc_R8.ToInstruction(reader.ReadDouble());
			case ElementType.Object:	return OpCodes.Ldnull.ToInstruction();
			default: throw new ApplicationException("Invalid instruction");
			}
		}

		Instruction Handler_Ldelem(BinaryReader reader) {
			return new Instruction(OpCodes.Ldelem, null);
		}

		Instruction Handler_Ldelema(BinaryReader reader) {
			return new Instruction(OpCodes.Ldelema, null);
		}

		Instruction Handler_Ldfld_Ldsfld(BinaryReader reader) {
			var field = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as IField;
			return new Instruction(null, new FieldInstructionOperand(OpCodes.Ldsfld, OpCodes.Ldfld, field));
		}

		Instruction Handler_Ldflda_Ldsflda(BinaryReader reader) {
			var field = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as IField;
			return new Instruction(null, new FieldInstructionOperand(OpCodes.Ldsflda, OpCodes.Ldflda, field));
		}

		Instruction Handler_Ldftn(BinaryReader reader) {
			var method = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as IMethod;
			return OpCodes.Ldftn.ToInstruction(method);
		}

		Instruction Handler_Ldlen(BinaryReader reader) {
			return OpCodes.Ldlen.ToInstruction();
		}

		Instruction Handler_Ldloc(BinaryReader reader) {
			return new Instruction(OpCodes.Ldloc, new LocalOperand(reader.ReadUInt16()));
		}

		Instruction Handler_Ldloca(BinaryReader reader) {
			return new Instruction(OpCodes.Ldloca, new LocalOperand(reader.ReadUInt16()));
		}

		Instruction Handler_Ldobj(BinaryReader reader) {
			return new Instruction(OpCodes.Ldobj, null);
		}

		Instruction Handler_Ldstr(BinaryReader reader) {
			return OpCodes.Ldstr.ToInstruction(reader.ReadString());
		}

		Instruction Handler_Ldtoken(BinaryReader reader) {
			var member = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as ITokenOperand;
			return OpCodes.Ldtoken.ToInstruction(member);
		}

		Instruction Handler_Ldvirtftn(BinaryReader reader) {
			var method = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as IMethod;
			reader.ReadUInt32();
			return OpCodes.Ldvirtftn.ToInstruction(method);
		}

		Instruction Handler_Leave(BinaryReader reader) {
			return new Instruction(OpCodes.Leave, new TargetDisplOperand(reader.ReadInt32()));
		}

		Instruction Handler_Mul(BinaryReader reader) {
			return OpCodes.Mul.ToInstruction();
		}

		Instruction Handler_Mul_Ovf(BinaryReader reader) {
			return OpCodes.Mul_Ovf.ToInstruction();
		}

		Instruction Handler_Mul_Ovf_Un(BinaryReader reader) {
			return OpCodes.Mul_Ovf_Un.ToInstruction();
		}

		Instruction Handler_Neg(BinaryReader reader) {
			return OpCodes.Neg.ToInstruction();
		}

		Instruction Handler_Newarr(BinaryReader reader) {
			var type = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as ITypeDefOrRef;
			return OpCodes.Newarr.ToInstruction(type);
		}

		Instruction Handler_Newobj(BinaryReader reader) {
			var method = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as IMethod;
			return OpCodes.Newobj.ToInstruction(method);
		}

		Instruction Handler_Nop(BinaryReader reader) {
			return OpCodes.Nop.ToInstruction();
		}

		Instruction Handler_Not(BinaryReader reader) {
			return OpCodes.Not.ToInstruction();
		}

		Instruction Handler_Or(BinaryReader reader) {
			return OpCodes.Or.ToInstruction();
		}

		Instruction Handler_Pop(BinaryReader reader) {
			return OpCodes.Pop.ToInstruction();
		}

		Instruction Handler_Rem(BinaryReader reader) {
			return OpCodes.Rem.ToInstruction();
		}

		Instruction Handler_Rem_Un(BinaryReader reader) {
			return OpCodes.Rem_Un.ToInstruction();
		}

		Instruction Handler_Ret(BinaryReader reader) {
			/*var method =*/ resolver.ResolveToken(reader.ReadUInt32(), gpContext) /*as IMethod*/;
			return OpCodes.Ret.ToInstruction();
		}

		Instruction Handler_Rethrow(BinaryReader reader) {
			return OpCodes.Rethrow.ToInstruction();
		}

		Instruction Handler_Shl(BinaryReader reader) {
			return OpCodes.Shl.ToInstruction();
		}

		Instruction Handler_Shr(BinaryReader reader) {
			return OpCodes.Shr.ToInstruction();
		}

		Instruction Handler_Shr_Un(BinaryReader reader) {
			return OpCodes.Shr_Un.ToInstruction();
		}

		Instruction Handler_Starg(BinaryReader reader) {
			return new Instruction(OpCodes.Starg, new ArgOperand(reader.ReadUInt16()));
		}

		Instruction Handler_Stelem(BinaryReader reader) {
			return new Instruction(OpCodes.Stelem, null);
		}

		Instruction Handler_Stfld_Stsfld(BinaryReader reader) {
			var field = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as IField;
			return new Instruction(null, new FieldInstructionOperand(OpCodes.Stsfld, OpCodes.Stfld, field));
		}

		Instruction Handler_Stloc(BinaryReader reader) {
			ushort loc = reader.ReadUInt16();
			/*var etype = (ElementType)*/reader.ReadInt32();
			return new Instruction(OpCodes.Stloc, new LocalOperand(loc));
		}

		Instruction Handler_Stobj(BinaryReader reader) {
			return new Instruction(OpCodes.Stobj, null);
		}

		Instruction Handler_Sub(BinaryReader reader) {
			return OpCodes.Sub.ToInstruction();
		}

		Instruction Handler_Sub_Ovf(BinaryReader reader) {
			return OpCodes.Sub_Ovf.ToInstruction();
		}

		Instruction Handler_Sub_Ovf_Un(BinaryReader reader) {
			return OpCodes.Sub_Ovf_Un.ToInstruction();
		}

		Instruction Handler_Switch(BinaryReader reader) {
			int size = reader.ReadInt32();
			var offsets = new int[size];
			for (int i = 0; i < size; i++)
				offsets[i] = reader.ReadInt32();
			return new Instruction(OpCodes.Switch, new SwitchTargetDisplOperand(offsets));
		}

		Instruction Handler_Throw(BinaryReader reader) {
			return OpCodes.Throw.ToInstruction();
		}

		Instruction Handler_Unbox_Any(BinaryReader reader) {
			var type = resolver.ResolveToken(reader.ReadUInt32(), gpContext) as ITypeDefOrRef;
			return OpCodes.Unbox_Any.ToInstruction(type);
		}

		Instruction Handler_Xor(BinaryReader reader) {
			return OpCodes.Xor.ToInstruction();
		}
	}
}
