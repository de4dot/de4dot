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
using dnlib.DotNet.Emit;

namespace de4dot.blocks {
	public class Instr {
		Instruction instruction;

		public OpCode OpCode {
			get { return instruction.OpCode; }
		}

		public object Operand {
			get { return instruction.Operand; }
			set { instruction.Operand = value; }
		}

		public Instr(Instruction instruction) {
			this.instruction = instruction;
		}

		public Instruction Instruction {
			get { return instruction; }
		}

		// Returns the variable or null if it's not a ldloc/stloc instruction. It does not return
		// a local variable if it's a ldloca/ldloca.s instruction.
		public static Local GetLocalVar(IList<Local> locals, Instr instr) {
			return instr.Instruction.GetLocal(locals);
		}

		static public bool IsFallThrough(OpCode opCode) {
			switch (opCode.FlowControl) {
			case FlowControl.Call:
				return opCode != OpCodes.Jmp;
			case FlowControl.Cond_Branch:
			case FlowControl.Next:
				return true;
			default:
				return false;
			}
		}

		// Returns true if the instruction only pushes one value onto the stack and pops nothing
		public bool IsSimpleLoad() {
			switch (OpCode.Code) {
			case Code.Ldarg:
			case Code.Ldarg_S:
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
			case Code.Ldarga:
			case Code.Ldarga_S:
			case Code.Ldc_I4:
			case Code.Ldc_I4_S:
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
			case Code.Ldc_I8:
			case Code.Ldc_R4:
			case Code.Ldc_R8:
			case Code.Ldloc:
			case Code.Ldloc_S:
			case Code.Ldloc_0:
			case Code.Ldloc_1:
			case Code.Ldloc_2:
			case Code.Ldloc_3:
			case Code.Ldloca:
			case Code.Ldloca_S:
			case Code.Ldnull:
			case Code.Ldstr:
			case Code.Ldtoken:
				return true;
			default:
				return false;
			}
		}

		public bool IsLdcI4() {
			return instruction.IsLdcI4();
		}

		public int GetLdcI4Value() {
			return instruction.GetLdcI4Value();
		}

		public bool IsLdarg() {
			return instruction.IsLdarg();
		}

		public bool IsStloc() {
			return instruction.IsStloc();
		}

		public bool IsLdloc() {
			return instruction.IsLdloc();
		}

		public bool IsNop() {
			return OpCode == OpCodes.Nop;
		}

		public bool IsPop() {
			return OpCode == OpCodes.Pop;
		}

		public bool IsLeave() {
			return instruction.IsLeave();
		}

		public bool IsBr() {
			return instruction.IsBr();
		}

		public bool IsBrfalse() {
			return instruction.IsBrfalse();
		}

		public bool IsBrtrue() {
			return instruction.IsBrtrue();
		}

		public bool IsConditionalBranch() {
			return instruction.IsConditionalBranch();
		}

		public bool GetFlippedBranchOpCode(out OpCode opcode) {
			switch (OpCode.Code) {
			case Code.Bge:		opcode = OpCodes.Blt; return true;
			case Code.Bge_S:	opcode = OpCodes.Blt_S; return true;
			case Code.Bge_Un:	opcode = OpCodes.Blt_Un; return true;
			case Code.Bge_Un_S: opcode = OpCodes.Blt_Un_S; return true;

			case Code.Blt:		opcode = OpCodes.Bge; return true;
			case Code.Blt_S:	opcode = OpCodes.Bge_S; return true;
			case Code.Blt_Un:	opcode = OpCodes.Bge_Un; return true;
			case Code.Blt_Un_S: opcode = OpCodes.Bge_Un_S; return true;

			case Code.Bgt:		opcode = OpCodes.Ble; return true;
			case Code.Bgt_S:	opcode = OpCodes.Ble_S; return true;
			case Code.Bgt_Un:	opcode = OpCodes.Ble_Un; return true;
			case Code.Bgt_Un_S: opcode = OpCodes.Ble_Un_S; return true;

			case Code.Ble:		opcode = OpCodes.Bgt; return true;
			case Code.Ble_S:	opcode = OpCodes.Bgt_S; return true;
			case Code.Ble_Un:	opcode = OpCodes.Bgt_Un; return true;
			case Code.Ble_Un_S: opcode = OpCodes.Bgt_Un_S; return true;

			case Code.Brfalse:	opcode = OpCodes.Brtrue; return true;
			case Code.Brfalse_S:opcode = OpCodes.Brtrue_S; return true;

			case Code.Brtrue:	opcode = OpCodes.Brfalse; return true;
			case Code.Brtrue_S: opcode = OpCodes.Brfalse_S; return true;

			// Can't flip beq and bne.un since it's object vs uint/float
			case Code.Beq:
			case Code.Beq_S:
			case Code.Bne_Un:
			case Code.Bne_Un_S:
			default:
				opcode = OpCodes.Nop;	// Whatever...
				return false;
			}
		}

		public void FlipConditonalBranch() {
			OpCode opcode;
			if (!GetFlippedBranchOpCode(out opcode))
				throw new ApplicationException("Can't flip conditional since it's not a supported conditional instruction");
			instruction.OpCode = opcode;
		}

		// Returns true if we can flip a conditional branch
		public bool CanFlipConditionalBranch() {
			OpCode opcode;
			return GetFlippedBranchOpCode(out opcode);
		}

		public void UpdateTargets(List<Instr> targets) {
			switch (OpCode.OperandType) {
			case OperandType.ShortInlineBrTarget:
			case OperandType.InlineBrTarget:
				if (targets.Count != 1)
					throw new ApplicationException("More than one target!");
				instruction.Operand = targets[0].Instruction;
				break;

			case OperandType.InlineSwitch:
				var switchTargets = new Instruction[targets.Count];
				for (var i = 0; i < targets.Count; i++)
					switchTargets[i] = targets[i].Instruction;
				instruction.Operand = switchTargets;
				break;

			default:
				if (targets.Count != 0)
					throw new ApplicationException("This instruction doesn't have any targets!");
				break;
			}
		}

		public override string ToString() {
			return instruction.ToString();
		}
	}
}
