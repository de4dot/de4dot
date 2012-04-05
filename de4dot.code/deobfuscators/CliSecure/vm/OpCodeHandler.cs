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
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CliSecure.vm {
	abstract class OpCodeHandler {
		protected abstract OpCodeHandlerSigInfo OpCodeHandlerSigInfo { get; }
		protected abstract bool detectInternal(UnknownHandlerInfo info);
		public abstract Instruction read(BinaryReader reader);
		public abstract string Name { get; }

		public bool detect(UnknownHandlerInfo info) {
			var sigInfo = OpCodeHandlerSigInfo;

			if (!compare(sigInfo.NumStaticMethods, info.NumStaticMethods))
				return false;
			if (!compare(sigInfo.NumInstanceMethods, info.NumInstanceMethods))
				return false;
			if (!compare(sigInfo.NumVirtualMethods, info.NumVirtualMethods))
				return false;
			if (!compare(sigInfo.NumCtors, info.NumCtors))
				return false;
			if (!compare(sigInfo.ExecuteMethodThrows, info.ExecuteMethodThrows))
				return false;
			if (!info.hasSameFieldTypes(sigInfo.RequiredFieldTypes))
				return false;
			if (sigInfo.ExecuteMethodLocals != null && !new LocalTypes(info.ExecuteMethod).all(sigInfo.ExecuteMethodLocals))
				return false;

			return detectInternal(info);
		}

		static bool compare(int? val1, int val2) {
			if (!val1.HasValue)
				return true;
			return val1.Value == val2;
		}

		public override string ToString() {
			return Name;
		}
	}

	class ArithmeticOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				FieldsInfo.EnumType,
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 14,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "arithmetic"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			switch (reader.ReadByte()) {
			case 0: return Instruction.Create(OpCodes.Add);
			case 1: return Instruction.Create(OpCodes.Add_Ovf);
			case 2: return Instruction.Create(OpCodes.Add_Ovf_Un);
			case 3: return Instruction.Create(OpCodes.Sub);
			case 4: return Instruction.Create(OpCodes.Sub_Ovf);
			case 5: return Instruction.Create(OpCodes.Sub_Ovf_Un);
			case 6: return Instruction.Create(OpCodes.Mul);
			case 7: return Instruction.Create(OpCodes.Mul_Ovf);
			case 8: return Instruction.Create(OpCodes.Mul_Ovf_Un);
			case 9: return Instruction.Create(OpCodes.Div);
			case 10: return Instruction.Create(OpCodes.Div_Un);
			case 11: return Instruction.Create(OpCodes.Rem);
			case 12: return Instruction.Create(OpCodes.Rem_Un);
			default: throw new ApplicationException("Invalid opcode");
			}
		}
	}

	class ArrayOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.UInt32",
			},
			ExecuteMethodLocals = new string[] {
				"System.Object",
				"System.Int32",
				"System.Type",
				"System.IntPtr",
			},
			ExecuteMethodThrows = 0,
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "newarr"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return DotNetUtils.callsMethod(info.ExecuteMethod, "System.Type System.Reflection.Module::ResolveType(System.Int32)");
		}

		public override Instruction read(BinaryReader reader) {
			return new Instruction {
				OpCode = OpCodes.Newarr,
				Operand = new TokenOperand(reader.ReadInt32()),
			};
		}
	}

	class BoxOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				FieldsInfo.EnumType,
				"System.UInt32",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 2,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "box/unbox"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			var instr = new Instruction();
			switch (reader.ReadByte()) {
			case 0: instr.OpCode = OpCodes.Box; break;
			case 1: instr.OpCode = OpCodes.Unbox_Any; break;
			default: throw new ApplicationException("Invalid opcode");
			}
			instr.Operand = new TokenOperand(reader.ReadInt32());
			return instr;
		}
	}

	class CallOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.Collections.Generic.Dictionary`2<System.String,System.Int32>",
				"System.Collections.Generic.Dictionary`2<System.Reflection.MethodInfo,System.Reflection.Emit.DynamicMethod>",
				"System.Reflection.MethodBase",
				"System.UInt32",
				FieldsInfo.EnumType,
			},
			NumStaticMethods = 2,
			NumInstanceMethods = 4,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "call"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			var instr = new Instruction();
			switch (reader.ReadByte()) {
			case 0: instr.OpCode = OpCodes.Newobj; break;
			case 1: instr.OpCode = OpCodes.Call; break;
			case 2: instr.OpCode = OpCodes.Callvirt; break;
			default: throw new ApplicationException("Invalid opcode");
			}
			instr.Operand = new TokenOperand(reader.ReadInt32());
			return instr;
		}
	}

	class CastOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.UInt32",
				FieldsInfo.EnumType,
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 2,
		};

		public override string Name {
			get { return "cast"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			var instr = new Instruction();
			switch (reader.ReadByte()) {
			case 0: instr.OpCode = OpCodes.Castclass; break;
			case 1: instr.OpCode = OpCodes.Isinst; break;
			default: throw new ApplicationException("Invalid opcode");
			}
			instr.Operand = new TokenOperand(reader.ReadInt32());
			return instr;
		}
	}

	class CompareOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.Int32",
				FieldsInfo.EnumType,
			},
			NumStaticMethods = 1,
			NumInstanceMethods = 7,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "compare"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			int type = reader.ReadByte();
			Instruction instr = new Instruction();
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
	}

	class ConvertOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				FieldsInfo.EnumType,
				"System.Boolean",
				"System.Boolean",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 13,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "convert"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		class InstructionInfo {
			public byte Type { get; set; }
			public bool Second { get; set; }
			public bool Third { get; set; }
			public OpCode OpCode { get; set; }
		}
		static List<InstructionInfo> instructionInfos = new List<InstructionInfo> {
			new InstructionInfo { Type = 0, Second = false, Third = false, OpCode = OpCodes.Conv_I1 },
			new InstructionInfo { Type = 1, Second = false, Third = false, OpCode = OpCodes.Conv_I2 },
			new InstructionInfo { Type = 2, Second = false, Third = false, OpCode = OpCodes.Conv_I4 },
			new InstructionInfo { Type = 3, Second = false, Third = false, OpCode = OpCodes.Conv_I8 },
			new InstructionInfo { Type = 4, Second = false, Third = false, OpCode = OpCodes.Conv_R4 },
			new InstructionInfo { Type = 5, Second = false, Third = false, OpCode = OpCodes.Conv_R8 },
			new InstructionInfo { Type = 6, Second = false, Third = false, OpCode = OpCodes.Conv_U1 },
			new InstructionInfo { Type = 7, Second = false, Third = false, OpCode = OpCodes.Conv_U2 },
			new InstructionInfo { Type = 8, Second = false, Third = false, OpCode = OpCodes.Conv_U4 },
			new InstructionInfo { Type = 9, Second = false, Third = false, OpCode = OpCodes.Conv_U8 },
			new InstructionInfo { Type = 10, Second = false, Third = false, OpCode = OpCodes.Conv_I },
			new InstructionInfo { Type = 11, Second = false, Third = false, OpCode = OpCodes.Conv_U },

			new InstructionInfo { Type = 0, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I1 },
			new InstructionInfo { Type = 1, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I2 },
			new InstructionInfo { Type = 2, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I4 },
			new InstructionInfo { Type = 3, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I8 },
			new InstructionInfo { Type = 6, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U1 },
			new InstructionInfo { Type = 7, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U2 },
			new InstructionInfo { Type = 8, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U4 },
			new InstructionInfo { Type = 9, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U8 },
			new InstructionInfo { Type = 10, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_I },
			new InstructionInfo { Type = 11, Second = true, Third = false, OpCode = OpCodes.Conv_Ovf_U },

			new InstructionInfo { Type = 0, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I1_Un },
			new InstructionInfo { Type = 1, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I2_Un },
			new InstructionInfo { Type = 2, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I4_Un },
			new InstructionInfo { Type = 3, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I8_Un },
			new InstructionInfo { Type = 6, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U1_Un },
			new InstructionInfo { Type = 7, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U2_Un },
			new InstructionInfo { Type = 8, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U4_Un },
			new InstructionInfo { Type = 9, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U8_Un },
			new InstructionInfo { Type = 10, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_I_Un },
			new InstructionInfo { Type = 11, Second = true, Third = true, OpCode = OpCodes.Conv_Ovf_U_Un },
			new InstructionInfo { Type = 12, Second = true, Third = true, OpCode = OpCodes.Conv_R_Un },
		};
		public override Instruction read(BinaryReader reader) {
			byte type = reader.ReadByte();
			bool second = reader.ReadBoolean();
			bool third = reader.ReadBoolean();

			Instruction instr = null;
			foreach (var info in instructionInfos) {
				if (type != info.Type || info.Second != second || info.Third != third)
					continue;

				instr = new Instruction { OpCode = info.OpCode };
				break;
			}
			if (instr == null)
				throw new ApplicationException("Invalid opcode");

			return instr;
		}
	}

	class DupPopOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				FieldsInfo.EnumType,
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "dup/pop"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			switch (reader.ReadByte()) {
			case 0: return Instruction.Create(OpCodes.Dup);
			case 1: return Instruction.Create(OpCodes.Pop);
			default: throw new ApplicationException("Invalid opcode");
			}
		}
	}

	class ElemOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.Boolean",
				"System.Boolean",
				FieldsInfo.EnumType,
				"System.UInt32",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "ldelem/stelem"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		class InstructionInfo {
			public bool First { get; set; }
			public bool Second { get; set; }
			public int Value { get; set; }
			public OpCode OpCode { get; set; }
		}
		static List<InstructionInfo> instructionInfos = new List<InstructionInfo> {
			new InstructionInfo { First = false, Second = true, Value = 24, OpCode = OpCodes.Stelem_I },
			new InstructionInfo { First = false, Second = true, Value = 4, OpCode = OpCodes.Stelem_I1 },
			new InstructionInfo { First = false, Second = true, Value = 6, OpCode = OpCodes.Stelem_I2 },
			new InstructionInfo { First = false, Second = true, Value = 8, OpCode = OpCodes.Stelem_I4 },
			new InstructionInfo { First = false, Second = true, Value = 10, OpCode = OpCodes.Stelem_I8 },
			new InstructionInfo { First = false, Second = true, Value = 12, OpCode = OpCodes.Stelem_R4 },
			new InstructionInfo { First = false, Second = true, Value = 13, OpCode = OpCodes.Stelem_R8 },
			new InstructionInfo { First = false, Second = true, Value = 28, OpCode = OpCodes.Stelem_Ref },
			new InstructionInfo { First = false, Second = false, Value = 0, OpCode = OpCodes.Stelem_Any },

			new InstructionInfo { First = true, Second = true, Value = 24, OpCode = OpCodes.Ldelem_I },
			new InstructionInfo { First = true, Second = true, Value = 4, OpCode = OpCodes.Ldelem_I1 },
			new InstructionInfo { First = true, Second = true, Value = 6, OpCode = OpCodes.Ldelem_I2 },
			new InstructionInfo { First = true, Second = true, Value = 8, OpCode = OpCodes.Ldelem_I4 },
			new InstructionInfo { First = true, Second = true, Value = 10, OpCode = OpCodes.Ldelem_I8 },
			new InstructionInfo { First = true, Second = true, Value = 5, OpCode = OpCodes.Ldelem_U1 },
			new InstructionInfo { First = true, Second = true, Value = 7, OpCode = OpCodes.Ldelem_U2 },
			new InstructionInfo { First = true, Second = true, Value = 9, OpCode = OpCodes.Ldelem_U4 },
			new InstructionInfo { First = true, Second = true, Value = 12, OpCode = OpCodes.Ldelem_R4 },
			new InstructionInfo { First = true, Second = true, Value = 13, OpCode = OpCodes.Ldelem_R8 },
			new InstructionInfo { First = true, Second = true, Value = 28, OpCode = OpCodes.Ldelem_Ref },
			new InstructionInfo { First = true, Second = false, Value = 0, OpCode = OpCodes.Ldelem_Any },
		};
		public override Instruction read(BinaryReader reader) {
			Instruction instr = null;
			bool first = reader.ReadBoolean();
			bool second = reader.ReadBoolean();
			int value = reader.ReadInt32();
			foreach (var info in instructionInfos) {
				if (info.First != first || info.Second != second)
					continue;
				if (second && value != info.Value)
					continue;

				if (second)
					instr = new Instruction { OpCode = info.OpCode };
				else
					instr = new Instruction { OpCode = info.OpCode, Operand = new TokenOperand(value) };
				break;
			}
			if (instr == null)
				throw new ApplicationException("Invalid opcode");

			return instr;
		}
	}

	class EndfinallyOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[0],
			ExecuteMethodThrows = 2,
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "endfinally"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return DotNetUtils.callsMethod(info.ExecuteMethod, "System.Reflection.MethodInfo System.Type::GetMethod(System.String,System.Reflection.BindingFlags)");
		}

		public override Instruction read(BinaryReader reader) {
			return Instruction.Create(OpCodes.Endfinally);
		}
	}

	class FieldOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.UInt32",
				FieldsInfo.EnumType,
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "load/store field"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			var instr = new Instruction();
			switch (reader.ReadByte()) {
			case 0: instr.Operand = new LoadFieldOperand(reader.ReadInt32()); break;
			case 1: instr.Operand = new LoadFieldAddressOperand(reader.ReadInt32()); break;
			case 2: instr.Operand = new StoreFieldOperand(reader.ReadInt32()); break;
			default: throw new ApplicationException("Invalid opcode");
			}
			return instr;
		}
	}

	class InitobjOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.UInt32",
			},
			ExecuteMethodLocals = new string[] {
				"System.Type",
			},
			ExecuteMethodThrows = 1,
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "initobj"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			return new Instruction {
				OpCode = OpCodes.Initobj,
				Operand = new TokenOperand(reader.ReadInt32()),
			};
		}
	}

	class LdLocalArgOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.Boolean",
				"System.UInt16",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "load local/arg"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
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
	}

	class LdLocalArgAddrOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.Boolean",
				"System.UInt32",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "load local/arg address"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			Instruction instr = new Instruction();
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
	}

	class LdelemaOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[0],
			ExecuteMethodLocals = new string[] {
				"System.Int32",
				"System.Array",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "ldelema"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			return new Instruction {
				OpCode = OpCodes.Ldelema,
				Operand = null,
			};
		}
	}

	class LdlenOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[0],
			ExecuteMethodLocals = new string[] {
				"System.Array",
				"System.Object",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "ldlen"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			return Instruction.Create(OpCodes.Ldlen);
		}
	}

	class LdobjOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[0],
			ExecuteMethodThrows = 1,
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "ldobj"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return info.ExecuteMethod.Body.Variables.Count == 2;
		}

		public override Instruction read(BinaryReader reader) {
			return new Instruction {
				OpCode = OpCodes.Ldobj,
				Operand = null,
			};
		}
	}

	class LdstrOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.String",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "ldstr"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			return Instruction.Create(OpCodes.Ldstr, reader.ReadString());
		}
	}

	class LdtokenOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.UInt32",
			},
			ExecuteMethodLocals = new string[] {
				"System.Object",
				"System.Reflection.MemberInfo",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "ldtoken"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return DotNetUtils.callsMethod(info.ExecuteMethod, "System.Reflection.MemberInfo System.Reflection.Module::ResolveMember(System.Int32)");
		}

		public override Instruction read(BinaryReader reader) {
			return new Instruction {
				OpCode = OpCodes.Ldtoken,
				Operand = new TokenOperand(reader.ReadInt32()),
			};
		}
	}

	class LeaveOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.Int32",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "leave"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return !DotNetUtils.callsMethod(info.ExecuteMethod, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)") &&
				!DotNetUtils.callsMethod(info.ExecuteMethod, "System.Type System.Reflection.Module::ResolveType(System.Int32)") &&
				!DotNetUtils.callsMethod(info.ExecuteMethod, "System.Reflection.MemberInfo System.Reflection.Module::ResolveMember(System.Int32)");
		}

		public override Instruction read(BinaryReader reader) {
			int displacement = reader.ReadInt32();
			return new Instruction {
				OpCode = OpCodes.Leave,
				Operand = new TargetDisplOperand(displacement),
			};
		}
	}

	class LoadConstantOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.Object",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "load constant"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			switch ((ElementType)reader.ReadByte()) {
			case ElementType.I4: return DotNetUtils.createLdci4(reader.ReadInt32());
			case ElementType.I8: return Instruction.Create(OpCodes.Ldc_I8, reader.ReadInt64());
			case ElementType.R4: return Instruction.Create(OpCodes.Ldc_R4, reader.ReadSingle());
			case ElementType.R8: return Instruction.Create(OpCodes.Ldc_R8, reader.ReadDouble());
			case ElementType.Object: return Instruction.Create(OpCodes.Ldnull);
			default: throw new ApplicationException("Invalid opcode");
			}
		}
	}

	class LoadFuncOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				FieldsInfo.EnumType,
				"System.UInt32",
				"System.UInt32",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "load func"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			byte code = reader.ReadByte();
			int token = reader.ReadInt32();

			Instruction instr;
			switch (code) {
			case 0:
				instr = new Instruction { OpCode = OpCodes.Ldftn, Operand = new TokenOperand(token) };
				break;

			case 1:
				reader.ReadInt32();	// token of newobj .ctor
				instr = new Instruction { OpCode = OpCodes.Ldvirtftn, Operand = new TokenOperand(token) };
				break;

			default:
				throw new ApplicationException("Invalid opcode");
			}

			return instr;
		}
	}

	class LogicalOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				FieldsInfo.EnumType,
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 6,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "logical"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			switch (reader.ReadByte()) {
			case 0: return Instruction.Create(OpCodes.And);
			case 1: return Instruction.Create(OpCodes.Or);
			case 2: return Instruction.Create(OpCodes.Xor);
			case 3: return Instruction.Create(OpCodes.Shl);
			case 4: return Instruction.Create(OpCodes.Shr);
			case 5: return Instruction.Create(OpCodes.Shr_Un);
			default: throw new ApplicationException("Invalid opcode");
			}
		}
	}

	class NopOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[0],
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};


		public override string Name {
			get { return "nop"; }
		}
		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return isEmptyMethod(info.ReadMethod) && isEmptyMethod(info.ExecuteMethod);
		}

		static bool isEmptyMethod(MethodDefinition method) {
			return method.Body.Instructions.Count == 1 &&
				method.Body.Instructions[0].OpCode.Code == Code.Ret;
		}

		public override Instruction read(BinaryReader reader) {
			return Instruction.Create(OpCodes.Nop);
		}
	}

	class RetOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.UInt32",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "ret"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return DotNetUtils.callsMethod(info.ExecuteMethod, "System.Reflection.MethodBase System.Reflection.Module::ResolveMethod(System.Int32)");
		}

		public override Instruction read(BinaryReader reader) {
			reader.ReadInt32();	// token of current method
			return Instruction.Create(OpCodes.Ret);
		}
	}

	class RethrowOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[0],
			ExecuteMethodThrows = 1,
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "rethrow"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return info.ExecuteMethod.Body.Variables.Count == 0;
		}

		public override Instruction read(BinaryReader reader) {
			return Instruction.Create(OpCodes.Rethrow);
		}
	}

	class StLocalArgOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.Boolean",
				"System.UInt16",
				FieldsInfo.EnumType,
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "store local/arg"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
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
	}

	class StobjOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[0],
			ExecuteMethodThrows = 1,
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "stobj"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return info.ExecuteMethod.Body.Variables.Count == 3;
		}

		public override Instruction read(BinaryReader reader) {
			return new Instruction {
				OpCode = OpCodes.Stobj,
				Operand = null,
			};
		}
	}

	class SwitchOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				"System.UInt32",
				"System.Int32[]",
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "switch"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			int numTargets = reader.ReadInt32();
			int[] targetDispls = new int[numTargets];
			for (int i = 0; i < targetDispls.Length; i++)
				targetDispls[i] = reader.ReadInt32();
			return new Instruction {
				OpCode = OpCodes.Switch,
				Operand = new SwitchTargetDisplOperand(targetDispls),
			};
		}
	}

	class ThrowOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[0],
			ExecuteMethodLocals = new string[] {
				"System.Object",
			},
			ExecuteMethodThrows = 2,
			NumStaticMethods = 0,
			NumInstanceMethods = 0,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "throw"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return !DotNetUtils.callsMethod(info.ExecuteMethod, "System.Reflection.MethodInfo System.Type::GetMethod(System.String,System.Reflection.BindingFlags)");
		}

		public override Instruction read(BinaryReader reader) {
			return Instruction.Create(OpCodes.Throw);
		}
	}

	class UnaryOpCodeHandler : OpCodeHandler {
		static readonly OpCodeHandlerSigInfo sigInfo = new OpCodeHandlerSigInfo {
			RequiredFieldTypes = new object[] {
				FieldsInfo.EnumType,
			},
			NumStaticMethods = 0,
			NumInstanceMethods = 2,
			NumVirtualMethods = 2,
			NumCtors = 1,
		};

		public override string Name {
			get { return "neg/not"; }
		}

		protected override OpCodeHandlerSigInfo OpCodeHandlerSigInfo {
			get { return sigInfo; }
		}

		protected override bool detectInternal(UnknownHandlerInfo info) {
			return true;
		}

		public override Instruction read(BinaryReader reader) {
			switch (reader.ReadByte()) {
			case 0: return Instruction.Create(OpCodes.Neg);
			case 1: return Instruction.Create(OpCodes.Not);
			default: throw new ApplicationException("Invalid opcode");
			}
		}
	}
}
